using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>What the instructor is expected to do next about a session's online room (docs/contracts/P11-FE-live-dto-appendix.md A.5).</summary>
public enum MeetingNeedsAction
{
    None,
    Waiting,
    PasteLink,
    ReconnectGoogle,
    Retry,
}

/// <summary>
/// One session's room as shown to its instructor. <b>Never carries the URL</b> (<see cref="HasMeetingLink"/> only): the
/// link is a capability that grants entry, and is revealed only by the join gate and the owning instructor's session detail.
/// </summary>
/// <param name="ErrorCode">Short machine code (e.g. <c>conference_pending</c>) the frontend maps to text — never raw provider text.</param>
public sealed record InstructorMeetingSummary(
    Guid SessionId,
    MeetingProvider? Provider,
    MeetingSyncStatus SyncStatus,
    bool HasMeetingLink,
    bool IsUsable,
    DateTime? LastSyncAtUtc,
    MeetingNeedsAction NeedsAction,
    string? ErrorCode);

public sealed record CourseMeetingsResponse(IReadOnlyList<InstructorMeetingSummary> Items);

public sealed record SetMeetingLinkCommand(string? MeetUrl);

public sealed record GoogleConnectCommand(string? ReturnPath);

public sealed record GoogleConnectResponse(string AuthorizationUrl);

/// <param name="AccountKind"><c>null</c> when not connected; otherwise <c>Personal</c>/<c>Workspace</c>/<c>Unknown</c> (P11-13).</param>
/// <param name="HostedDomain">The Workspace domain, when the account is a Workspace one.</param>
/// <param name="Recording">What the platform can do about recordings for this instructor.</param>
public sealed record GoogleConnectionStatusResponse(
    bool Configured,
    bool Connected,
    bool NeedsReconnect,
    string? GoogleEmail,
    DateTime? ConnectedAtUtc,
    DateTime? LastValidatedAtUtc,
    string? RevokedReason,
    int AffectedSessionCount,
    GoogleAccountKind? AccountKind,
    string? HostedDomain,
    RecordingCapabilityInfo Recording);

/// <summary>Where the browser is sent after the OAuth callback. <see cref="ErrorReason"/> is <c>null</c> on success.</summary>
public sealed record GoogleConnectOutcome(string RedirectUrl, string? ErrorReason);

/// <summary>Stable <c>reason</c> values of the OAuth callback redirect (<c>?google=error&amp;reason=...</c>).</summary>
public static class GoogleConnectErrorReasons
{
    public const string AccessDenied = "access_denied";
    public const string StateInvalid = "state_invalid";
    public const string ScopeMissing = "scope_missing";
    public const string NoRefreshToken = "no_refresh_token";
    public const string ExchangeFailed = "exchange_failed";

    /// <summary>The recording-access consent (P11-13) finished without both recording scopes (the instructor unticked one); nothing was stored.</summary>
    public const string RecordingScopeMissing = "recording_scope_missing";
}

/// <summary>Short codes stored in <c>SESSION_MEETINGS.ERROR</c> / exposed as <c>errorCode</c>. Never contain a token, URL or e-mail.</summary>
public static class MeetingErrorCodes
{
    public const string ConferencePending = "conference_pending";
    public const string ConferenceFailed = "conference_failed";
    public const string GoogleAccountUnavailable = "google_account_unavailable";
    public const string InvalidGrant = "invalid_grant";
    public const string InsufficientScope = "insufficient_scope";
    public const string OrphanEvent = "orphan_event";
    public const string MeetUrlRejected = "meet_url_rejected";
    public const string GoogleRateLimited = "google_rate_limited";
    public const string GoogleTransient = "google_transient";
    public const string GoogleBadRequest = "google_bad_request";
    public const string GoogleClientMisconfigured = "google_client_misconfigured";
    public const string GoogleNotConfigured = "google_not_configured";
}
