namespace Siri.Modules.Identity.Features.RevokeOtherSessions;

/// <param name="Message">Human-readable confirmation (Thai).</param>
/// <param name="RevokedCount">How many of the caller's other sessions were revoked — <c>0</c> is a
/// legitimate, successful outcome (the caller had no other active sessions to begin with), not an
/// error.</param>
public sealed record RevokeOtherSessionsResponse(string Message, int RevokedCount);
