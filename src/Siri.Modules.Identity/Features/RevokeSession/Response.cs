namespace Siri.Modules.Identity.Features.RevokeSession;

/// <param name="Message">Human-readable confirmation (Thai, matches every other Identity response's
/// shape).</param>
/// <param name="WasCurrentSession">True when the revoked session was the very session that made this
/// request (see <see cref="RevokeSessionHandler"/>'s doc comment for the "can I revoke my own current
/// session" design decision). Lets the frontend know it must drop its own in-memory access token and
/// treat itself as logged out immediately, rather than keep using a token whose session row is now
/// revoked server-side — the token would otherwise keep validating against the stateless JWT for its
/// remaining ~15-minute lifetime regardless (see the same handler's doc comment for why that residual
/// window is an accepted, pre-existing tradeoff, not new to this task).</param>
public sealed record RevokeSessionResponse(string Message, bool WasCurrentSession);
