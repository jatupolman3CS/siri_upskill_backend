namespace Siri.Modules.Notification.Domain;

/// <summary>
/// Real delivery status of an <see cref="ANNOUNCEMENT"/> — a different axis than
/// <see cref="ANNOUNCEMENT.SendEmail"/> (should fan-out also enqueue email?) and
/// <see cref="ANNOUNCEMENT.ScheduledAtUtc"/> (when is fan-out allowed to run?). X-31 added this to
/// replace relying on <see cref="ANNOUNCEMENT.SentAtUtc"/> alone as the "has this been sent" signal (the
/// old code set it at creation time with nothing ever actually sent). "Sent" is now a fact recorded only
/// when <see cref="ANNOUNCEMENT.MarkSent"/> is actually called (from
/// <c>Infrastructure/AnnouncementDispatchJob.cs</c>, after resolving recipients and staging their
/// email/in-app notifications). Only two values (no "Failed") because <c>AnnouncementDispatchJob</c> fans
/// out one announcement inside a single <c>SaveChangesAsync</c> — a failed run persists nothing for that
/// announcement, so it is retried wholesale on the next run (same reasoning
/// <c>EMAIL_OUTBOX_MESSAGE</c> uses a nullable <c>NextRetryAtUtc</c> instead of a third status — see
/// <see cref="EmailOutboxStatus"/>'s own doc comment).
/// </summary>
public enum AnnouncementDispatchStatus
{
    /// <summary>Not yet sent — just created (immediate), or waiting for <see cref="ANNOUNCEMENT.ScheduledAtUtc"/>
    /// to arrive, or already due and waiting for the next <c>AnnouncementDispatchJob</c> run.</summary>
    Pending,

    /// <summary>Fan-out completed — every learner who was active-enrolled at dispatch time has an in-app
    /// notification, and (if <see cref="ANNOUNCEMENT.SendEmail"/>) an outbox row. Terminal.</summary>
    Sent,
}
