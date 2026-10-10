using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

// DTOs of the automatic recording import (docs/contracts/P11-13-live-recording-auto-import.md section 7). camelCase JSON, enums as strings (global
// JsonStringEnumConverter). None of these ever carries a Drive id, an asset id, a URL, a token or a message text — only codes.

/// <summary>What the platform can do about recordings for one instructor (contract section 6). Pure function of the feature flag, the provider mode and the
/// instructor's Google account — see <see cref="RecordingCapabilityCalculator"/>.</summary>
public enum RecordingCapability
{
    /// <summary>The instructor uploads the recording by hand (feature off, personal account, no usable account, or Google not in use).</summary>
    Manual,

    /// <summary>A Workspace account that has not yet granted the two recording scopes: offer "turn on automatic recording import".</summary>
    AutoNeedsConsent,

    /// <summary>A Workspace account with recording access: classes are imported automatically.</summary>
    Auto,
}

/// <summary>The two ways a session's recording gets onto the platform, as the session list shows them.</summary>
public enum RecordingImportMode
{
    Manual,
    Auto,
}

/// <summary>The <c>recording</c> member of the Google connection status.</summary>
/// <param name="Mode">See <see cref="RecordingCapability"/>.</param>
/// <param name="AutoImportAvailable">The server feature flag <c>Live:Recording:AutoImport:Enabled</c> is on.</param>
/// <param name="ScopesGranted">The connected account has both recording scopes.</param>
public sealed record RecordingCapabilityInfo(RecordingCapability Mode, bool AutoImportAvailable, bool ScopesGranted);

/// <summary>The <c>recordingImport</c> member of an instructor's session list item / detail.</summary>
/// <param name="Mode"><c>Auto</c> when the feature is on and either an import row exists or the instructor's capability is <c>Auto</c>; <c>Manual</c> otherwise - always <c>Manual</c> while <c>Live:Recording:AutoImport:Enabled</c> is off, even for a session with an old import row.</param>
/// <param name="Status"><c>null</c> when there is no import row.</param>
/// <param name="ErrorCode">A stable code (never a message); only while the import is not finished successfully.</param>
/// <param name="NextAttemptAtUtc">When the platform will look again (<c>Waiting</c>/<c>Processing</c> only).</param>
/// <param name="CanRetry">The instructor may ask the platform to try again (<c>POST …/recording-import/retry</c> would succeed).</param>
public sealed record RecordingImportInfo(
    RecordingImportMode Mode,
    RecordingImportStatus? Status,
    string? ErrorCode,
    DateTime? NextAttemptAtUtc,
    bool CanRetry);
