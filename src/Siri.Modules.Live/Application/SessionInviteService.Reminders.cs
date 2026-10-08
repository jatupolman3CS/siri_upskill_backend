using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

// Reminders (docs/contracts/P11-04-live-invites-ics-reminders.md §4.5) — the same aggregate (SESSION_INVITE) and the same
// idempotency rule as the reconcile: a reminder is only ever sent together with the flag column that records it.
public sealed partial class SessionInviteService
{
    /// <summary>Sessions whose invites are loaded, processed and committed together — a failure rolls back only this slice.</summary>
    public const int SessionsPerReminderChunk = 10;

    /// <summary>How far ahead the reminder window looks (24 h plus slack for a late run).</summary>
    public static readonly TimeSpan ReminderHorizon = TimeSpan.FromHours(25);

    /// <summary>One reminder pass: 24 h and 1 h reminders to every invited learner and to the instructor, then the "room still not
    /// ready" warning to instructors of sessions that start within a day.</summary>
    public async Task<ReminderRunResult> SendRemindersAsync(CancellationToken cancellationToken)
    {
        var run = RunContext.Create(Options, clock.UtcNow);

        var contexts = await LiveScheduleWindow
            .GetAllAsync(schedule, run.Now, run.Now + ReminderHorizon, includeCancelled: false, cancellationToken)
            .ConfigureAwait(false);
        if (contexts.Count == 0)
        {
            return new ReminderRunResult(0, 0, 0, 0);
        }

        var meetingRows = await meetings
            .GetBySessionIdsAsync(contexts.Select(c => c.SessionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        var meetingBySession = meetingRows.ToDictionary(m => m.SESSION_ID);

        var sent = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var chunk in contexts.OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId).Chunk(SessionsPerReminderChunk))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = await RemindChunkAsync(run, chunk, meetingBySession, cancellationToken).ConfigureAwait(false);
                sent += outcome.Sent;
                skipped += outcome.Skipped;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                invites.ClearTracking();
                failed++;
                logger.LogError("Live reminders failed for a slice of {Count} session(s) ({ExceptionType}); retried next run.", chunk.Length, ex.GetType().Name);
            }
        }

        var alerts = 0;
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!meetingBySession.TryGetValue(context.SessionId, out var snapshot)
                || !SessionInvitePolicy.NeedsReadinessAlert(snapshot, context, run.Now))
            {
                continue;
            }

            try
            {
                if (await AlertRoomNotReadyAsync(run, context, cancellationToken).ConfigureAwait(false))
                {
                    alerts++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                meetings.ClearTracking();
                failed++;
                logger.LogError("Live room-readiness alert failed for session {SessionId} ({ExceptionType}); retried next run.", context.SessionId, ex.GetType().Name);
            }
        }

        logger.LogInformation(
            "Live reminders: {Sent} sent, {Skipped} skipped (no contact), {Alerts} room alert(s), {Failed} slice(s) failed.",
            sent,
            skipped,
            alerts,
            failed);

        return new ReminderRunResult(sent, alerts, skipped, failed);
    }

    private async Task<(int Sent, int Skipped)> RemindChunkAsync(
        RunContext run,
        LiveSessionContext[] chunk,
        IReadOnlyDictionary<Guid, SESSION_MEETING> meetingBySession,
        CancellationToken cancellationToken)
    {
        var contextBySession = chunk.ToDictionary(c => c.SessionId);

        var candidates = await invites
            .GetInvitedAwaitingReminderAsync(chunk.Select(c => c.SessionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        var due = candidates
            .Select(invite => (Invite: invite, Context: contextBySession[invite.SESSION_ID], Kind: SessionInvitePolicy.DueReminder(invite, contextBySession[invite.SESSION_ID], run.Now)))
            .Where(x => x.Kind != ReminderKind.None)
            .OrderBy(x => x.Context.StartsAtUtc)
            .ThenBy(x => x.Invite.USER_ID)
            .ToList();

        if (due.Count == 0)
        {
            invites.ClearTracking();
            return (0, 0);
        }

        var contactMap = (await contacts
                .GetUsersContactInfoAsync(due.Select(x => x.Invite.USER_ID).Distinct().ToArray(), cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var sent = 0;
        var skipped = 0;
        foreach (var (invite, context, kind) in due)
        {
            if (!contactMap.TryGetValue(invite.USER_ID, out var contact) || !IsDeliverableEmail(contact.Email))
            {
                invite.MarkSkipped(NoContactCode);
                skipped++;
                continue;
            }

            var email = contact.Email.Trim();
            var name = string.IsNullOrWhiteSpace(contact.DisplayName) ? null : LiveEmailTemplates.Clean(run.Settings, contact.DisplayName);
            meetingBySession.TryGetValue(context.SessionId, out var meeting);
            var isInstructor = invite.ROLE == LiveParticipantRole.Instructor;
            var oneHour = kind == ReminderKind.OneHour;

            var message = isInstructor
                ? LiveEmailTemplates.InstructorReminder(run.Settings, context.CourseTitle, context, meeting?.IsUsable == true, oneHour)
                : oneHour
                    ? LiveEmailTemplates.LearnerReminder1h(run.Settings, context.CourseTitle, context.CourseSlug, context)
                    : LiveEmailTemplates.LearnerReminder24h(run.Settings, context.CourseTitle, context.CourseSlug, context);

            // The 24 h reminder re-sends the calendar entry (same UID and SEQUENCE — clients de-duplicate it); the 1 h one carries
            // none. An instructor whose room is a Google event on their own calendar never gets a calendar file from us.
            string? ics = null;
            IcsMethod? method = null;
            if (!oneHour && !(isInstructor && SessionInvitePolicy.IsGoogleEvent(meeting)))
            {
                method = IcsMethod.Request;
                ics = run.BuildIcs(
                    IcsMethod.Request,
                    [run.ToEvent(context.CourseTitle, context, invite.ICS_SEQUENCE_SENT ?? 0, cancelled: false)],
                    email,
                    name);
            }

            Stage(email, invite.USER_ID, message, method, ics);

            if (oneHour)
            {
                invite.MarkReminder1h(clock);
            }
            else
            {
                invite.MarkReminder24h(clock);
            }

            sent++;
        }

        await invites.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invites.ClearTracking();
        return (sent, skipped);
    }

    /// <summary>Warns the instructor (once) that a session starting within 24 hours still has no usable room. Returns whether it did.</summary>
    private async Task<bool> AlertRoomNotReadyAsync(RunContext run, LiveSessionContext context, CancellationToken cancellationToken)
    {
        // Load the row tracked only now that the untracked snapshot says an alert is due.
        var meeting = await meetings.GetBySessionIdAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null || !SessionInvitePolicy.NeedsReadinessAlert(meeting, context, run.Now))
        {
            meetings.ClearTracking();
            return false;
        }

        var message = LiveEmailTemplates.InstructorMeetingAlert(run.Settings, context.CourseTitle, context);
        notifications.Stage(context.InstructorUserId, message.InApp.Type, message.InApp.Title, message.InApp.Body, message.InApp.LinkPath);

        var email = await contacts.GetEmailAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false);
        if (IsDeliverableEmail(email))
        {
            emailOutbox.Enqueue(email!.Trim(), message.Subject, message.BodyHtml, message.TemplateKey, calendar: null);
        }
        else
        {
            // Never log the address itself — only that there was nobody to e-mail.
            logger.LogWarning("Live room-readiness alert for session {SessionId}: instructor {InstructorUserId} has no usable e-mail address; in-app only.", context.SessionId, context.InstructorUserId);
        }

        meeting.MarkReadinessAlertSent(clock);
        await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        meetings.ClearTracking();
        return true;
    }
}
