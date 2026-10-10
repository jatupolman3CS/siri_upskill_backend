using Siri.Modules.Catalog.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>Stable <c>reason</c> sub-codes of the learner/instructor Live API (docs/contracts/P11-FE-live-dto-appendix.md section 0).</summary>
public static class LiveReasons
{
    public const string NotFound = "live.not_found";
    public const string SessionCancelled = "live.session_cancelled";
    public const string SessionEnded = "live.session_ended";
    public const string WindowNotOpen = "live.window_not_open";
    public const string MeetingNotReady = "live.meeting_not_ready";

    /// <summary>P11-13: the automatic recording import is switched off, or the instructor's Google account is not a Workspace account.</summary>
    public const string RecordingNotAvailable = "live.recording_not_available";

    /// <summary>P11-13: the import cannot be retried now (not in a retryable state, outside the 30-day window, or the session already has a recording).</summary>
    public const string RecordingImportNotRetryable = "live.recording_import_not_retryable";
}

/// <summary>
/// Errors of the learner-facing Live queries. <see cref="NotFound"/> is <b>one static instance on purpose</b>: "no such session", "not your session" and
/// "your enrollment is over" must produce byte-identical responses (same status, title, <c>errorCode</c>, <c>reason</c>), so a probe can learn nothing about who
/// is entitled to what. Never build a second, similar-looking not-found for these paths.
/// </summary>
public static class LiveErrors
{
    public static readonly DomainError NotFound =
        DomainError.NotFound("ไม่พบคาบเรียนสดนี้หรือคุณไม่มีสิทธิ์เข้าถึง").WithReason(LiveReasons.NotFound);

    private static readonly DomainError CancelledError =
        DomainError.Conflict("คาบเรียนสดนี้ถูกยกเลิกแล้ว").WithReason(LiveReasons.SessionCancelled);

    private static readonly DomainError MeetingNotReadyError =
        DomainError.Unavailable("ห้องเรียนยังไม่พร้อม กรุณาลองใหม่อีกครั้งในอีกสักครู่").WithReason(LiveReasons.MeetingNotReady);

    public static DomainError SessionCancelled() => CancelledError;

    /// <summary>409 <c>live.recording_not_available</c> (P11-13).</summary>
    public static readonly DomainError RecordingNotAvailable =
        DomainError.Conflict("การนำเข้าบันทึกอัตโนมัติยังไม่เปิดให้ใช้กับบัญชี Google นี้").WithReason(LiveReasons.RecordingNotAvailable);

    /// <summary>409 <c>live.recording_import_not_retryable</c> (P11-13).</summary>
    public static readonly DomainError RecordingImportNotRetryable =
        DomainError.Conflict("ยังลองนำเข้าบันทึกของคาบนี้ใหม่ไม่ได้").WithReason(LiveReasons.RecordingImportNotRetryable);

    /// <summary>The session is over. For the join gate the extensions point the learner to the recording (<c>recordingEpisodeId</c>, only when one is attached) and the
    /// course (<c>courseSlug</c>); both are facts the caller is already entitled to.</summary>
    public static DomainError SessionEnded(LiveSessionContext context, bool withRecordingHint)
    {
        ArgumentNullException.ThrowIfNull(context);

        var error = DomainError.Conflict("คาบเรียนสดนี้จบแล้ว");
        if (!withRecordingHint)
        {
            return error.WithReason(LiveReasons.SessionEnded);
        }

        var extensions = new Dictionary<string, object?> { ["courseSlug"] = context.CourseSlug };
        if (context.RecordingEpisodeId is { } episodeId)
        {
            extensions["recordingEpisodeId"] = episodeId;
        }

        return error.WithReason(LiveReasons.SessionEnded, extensions);
    }

    public static DomainError WindowNotOpen(DateTime opensAtUtc, DateTime serverTimeUtc) =>
        DomainError.Conflict("ยังไม่ถึงเวลาเปิดห้องเรียน")
            .WithReason(LiveReasons.WindowNotOpen, new Dictionary<string, object?>
            {
                ["opensAtUtc"] = opensAtUtc,
                ["serverTimeUtc"] = serverTimeUtc,
            });

    public static DomainError MeetingNotReady() => MeetingNotReadyError;
}
