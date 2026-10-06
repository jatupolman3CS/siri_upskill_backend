using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// Recurring job (registered every minute from Siri.Workers/RecurringJobsRegistration.cs, alongside
/// EmailOutboxSenderJob) that actually delivers <see cref="ANNOUNCEMENT"/>s (X-31) — finds rows that are
/// due, resolves real recipients through <see cref="IAnnouncementRecipientResolver"/>, stages a
/// <see cref="USER_NOTIFICATION"/> for every learner active-enrolled at that moment plus (if
/// <see cref="ANNOUNCEMENT.SendEmail"/>) an <see cref="EMAIL_OUTBOX_MESSAGE"/> per recipient, then closes
/// with <see cref="ANNOUNCEMENT.MarkSent"/> using the real recipient count — everything for one
/// announcement is committed by its own <see cref="AppDbContext.SaveChangesAsync"/> (all-or-nothing per
/// announcement; one bad announcement cannot block the others in the same run).
/// </summary>
public sealed class AnnouncementDispatchJob(
    AppDbContext dbContext,
    IAnnouncementRecipientResolver recipientResolver,
    IEmailOutbox emailOutbox,
    IClock clock,
    ILogger<AnnouncementDispatchJob> logger)
{
    /// <summary>Max <see cref="ANNOUNCEMENT"/> rows processed per run (not a recipient cap) — bounds the
    /// work done per minute if several courses announce at once. Staging outbox/notification rows is just
    /// an insert, not actual delivery (real sending is throttled separately by EmailOutboxSenderJob's own
    /// BatchSize=100/minute), so there is no need to cap recipients per announcement — RecipientCount is a
    /// single real number produced in one run, never split across multiple runs.</summary>
    private const int MaxAnnouncementsPerRun = 10;

    private const string AnnouncementNotificationType = "course.announcement";

    /// <summary>USER_NOTIFICATION.Body is varchar(2000) while ANNOUNCEMENT.Body is unbounded text — the
    /// in-app copy is cut to fit (the email, which has no such limit, carries the full body).</summary>
    private const int NotificationBodyMaxLength = 2000;

    // No course-detail slug is reachable from Notification/Learning without adding another cross-module
    // call just for a cosmetic link — points at the learner's own course list instead, adjustable later if
    // a real course-announcements FE page exists.
    private const string AnnouncementLinkUrl = "/my-courses";

    [DisableConcurrentExecution(timeoutInSeconds: 110)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var due = await dbContext.Announcements()
            .Where(a => a.DispatchStatus == AnnouncementDispatchStatus.Pending
                && (a.ScheduledAtUtc == null || a.ScheduledAtUtc <= now))
            .OrderBy(a => a.Id) // UUIDv7 PK -> oldest-created-first, same as EmailOutboxSenderJob
            .Take(MaxAnnouncementsPerRun)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var announcement in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var recipients = await recipientResolver
                    .GetRecipientsAsync(announcement.CourseId, cancellationToken)
                    .ConfigureAwait(false);

                var notificationBody = announcement.Body.Length > NotificationBodyMaxLength
                    ? announcement.Body[..NotificationBodyMaxLength]
                    : announcement.Body;

                foreach (var recipient in recipients)
                {
                    dbContext.UserNotifications().Add(USER_NOTIFICATION.Create(
                        recipient.UserId,
                        AnnouncementNotificationType,
                        announcement.Title,
                        notificationBody,
                        AnnouncementLinkUrl,
                        clock));

                    if (announcement.SendEmail)
                    {
                        emailOutbox.Enqueue(
                            toEmail: recipient.Email,
                            subject: announcement.Title,
                            bodyHtml: BuildEmailBodyHtml(announcement),
                            templateKey: "course-announcement");
                    }
                }

                announcement.MarkSent(recipients.Count, clock);

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nothing for this announcement was saved (its own SaveChangesAsync is the last step above),
                // but the rows staged for it are still tracked — drop them, plus the announcement's pending
                // MarkSent change, so the next announcement's save cannot persist them (duplicate
                // notifications/emails on retry). This row stays Pending and is retried on the next run.
                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                dbContext.Entry(announcement).State = EntityState.Detached;

                logger.LogError(
                    ex,
                    "Failed to dispatch announcement {AnnouncementId} for course {CourseId}",
                    announcement.Id,
                    announcement.CourseId);
            }
        }
    }

    // Title/Body are free text from the instructor, not CMS content (security.md's sanitizer requirement
    // covers "HTML from CMS" specifically) — HTML-encoded here before being embedded in the outgoing
    // email so any tag/script-like content in the text can't become live HTML in the sent email.
    private static string BuildEmailBodyHtml(ANNOUNCEMENT announcement)
    {
        var encodedTitle = WebUtility.HtmlEncode(announcement.Title);
        var encodedBody = WebUtility.HtmlEncode(announcement.Body).Replace("\n", "<br/>", StringComparison.Ordinal);
        return $"<h2>{encodedTitle}</h2><p>{encodedBody}</p>";
    }
}
