namespace Siri.Modules.Live.Application;

/// <summary>
/// Tells an instructor something about their live-session rooms needs attention (P11-03 contract section 6.3b) or about the automatic recording import
/// (P11-13). Each method <em>stages</em> a notification on the shared context without saving — the caller (a job or service) owns the transaction.
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

    /// <summary>P11-13: the class's Google Meet recording was imported and is now a lesson of the course — enrolled learners can watch it.</summary>
    Task RecordingImportedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken);

    /// <summary>P11-13: the automatic import of this class's recording gave up (retries exhausted, file too large, transcode failed, ...) — the instructor uploads
    /// the recording by hand, or retries. (A class nobody recorded — <c>NoRecording</c> — is deliberately not announced.)</summary>
    Task RecordingImportFailedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken);

    /// <summary>P11-13: Google no longer lets the platform read this class's recording (access revoked or the recording permission missing) — the instructor must
    /// reconnect Google (and grant recording access) or upload by hand.</summary>
    Task RecordingNeedsReconnectAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken);
}
