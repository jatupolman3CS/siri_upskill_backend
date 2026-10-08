namespace Siri.Modules.Live.Application;

/// <summary>
/// Tells an instructor something about their live-session rooms needs attention (P11-03 contract section 6.3b). Each method
/// <em>stages</em> a notification on the shared context without saving — the caller (a job or service) owns the transaction.
/// An instructor without a known e-mail address is skipped. Content never includes a room URL or any secret.
/// </summary>
public interface IInstructorAlertSender
{
    /// <summary>The instructor's Google connection stopped working; <paramref name="affectedCount"/> upcoming classes need a room.</summary>
    Task GoogleReconnectNeededAsync(Guid instructorUserId, int affectedCount, CancellationToken cancellationToken);

    /// <summary>This class has no room and the instructor has no Google connection: they must paste a link.</summary>
    Task MeetingNeedsLinkAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken);

    /// <summary>Creating this class's Google Meet room failed after every retry.</summary>
    Task MeetingFailedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken);
}
