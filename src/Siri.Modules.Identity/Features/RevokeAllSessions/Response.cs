namespace Siri.Modules.Identity.Features.RevokeAllSessions;

/// <param name="Message">Human-readable confirmation (Thai) — explicitly says this included the
/// caller's own current device, unlike <c>RevokeOtherSessions.RevokeOtherSessionsResponse</c>'s
/// message.</param>
/// <param name="RevokedCount">How many sessions were revoked, including the current one if it was
/// active. <c>0</c> only happens if the caller somehow had no active sessions at all at the moment of
/// the call — not a realistic case for an authenticated caller (their own current session is normally
/// always active), but not treated as an error either way.</param>
public sealed record RevokeAllSessionsResponse(string Message, int RevokedCount);
