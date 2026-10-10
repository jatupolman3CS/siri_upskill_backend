namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// The stable <see cref="Siri.SharedKernel.DomainError.Reason"/> values <see cref="ILiveRecordingAttacher.AttachAsync"/> can fail with — the same ones
/// the manual attach endpoint answers (P11-06), published here so a caller in another module (the Live import job) can branch on them without a
/// magic string and without referencing Catalog's feature folders.
/// </summary>
public static class LiveRecordingAttachReasons
{
    /// <summary>The class has not started yet, so nothing can be attached.</summary>
    public const string SessionNotStarted = "live.session_not_started";

    /// <summary>The media asset is not <c>Ready</c> yet (still transcoding, or failed).</summary>
    public const string AssetNotReady = "live.recording_asset_not_ready";

    /// <summary>The media asset is already a lesson of this course that is not this session's recording lesson.</summary>
    public const string AssetInUse = "live.recording_asset_in_use";

    public const string EpisodeHasNoMedia = "live.recording_episode_has_no_media";

    public const string EpisodeInUse = "live.recording_episode_in_use";
}
