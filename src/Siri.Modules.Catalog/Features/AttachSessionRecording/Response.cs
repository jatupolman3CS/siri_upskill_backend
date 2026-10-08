namespace Siri.Modules.Catalog.Features.AttachSessionRecording;

/// <summary>
/// The lesson that now plays the session's recording (docs/contracts/P11-06-live-recording-catchup.md §3).
/// </summary>
/// <param name="ReplacedExisting"><c>true</c> when the session already had a recording that this call replaced (new media on the same lesson, or a different lesson
/// linked in mode B); <c>false</c> for a first attach and for an idempotent repeat.</param>
public sealed record LiveSessionRecordingResponse(
    Guid SessionId,
    Guid RecordingEpisodeId,
    Guid SectionId,
    string EpisodeTitle,
    int DurationSeconds,
    Guid MediaAssetId,
    bool ReplacedExisting);

/// <summary>
/// Content defaults for the lesson/section this feature creates. They are <b>data the instructor can rename</b>, not UI text — a course is authored
/// content, so these values are stored on the section/lesson rows exactly like anything the instructor types (docs/contracts/P11-06 §3.1).
/// </summary>
public static class LiveRecordingDefaults
{
    public const string SectionTitle = "บันทึกการสอนสด";

    public const string EpisodeTitlePrefix = "บันทึก: ";
}

/// <summary>Stable <c>reason</c> sub-codes (appendix §0) the front end maps to i18n messages.</summary>
public static class LiveRecordingReasons
{
    public const string SessionNotStarted = "live.session_not_started";

    public const string AssetNotReady = "live.recording_asset_not_ready";

    public const string AssetInUse = "live.recording_asset_in_use";

    public const string EpisodeHasNoMedia = "live.recording_episode_has_no_media";

    public const string EpisodeInUse = "live.recording_episode_in_use";
}
