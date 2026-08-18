namespace Siri.Modules.Identity.Features.RevokeSession;

/// <summary>
/// Input to <see cref="RevokeSessionHandler.HandleAsync"/>. <see cref="SessionId"/> is the one thing a
/// client legitimately supplies here — it names which of the caller's OWN sessions to revoke, taken
/// from the DELETE /api/identity/sessions/{sessionId} route value. That is ordinary REST (accepting a
/// resource <em>id</em> from the caller is normal and expected — backend.md's IDOR rule is about never
/// trusting who is asking, not about never accepting which resource is named); the rule this feature is
/// graded hardest on is that <see cref="UserId"/> below must never also come from the caller.
/// <see cref="UserId"/> is always <see cref="Siri.SharedKernel.IUserContext.UserId"/>, read server-side
/// by <see cref="RevokeSessionEndpoint"/> — never bound from the request in any form. See
/// <see cref="RevokeSessionHandler"/>'s doc comment for exactly how the two combine into the ownership
/// check that is this whole feature's headline correctness point.
/// <see cref="CurrentSessionId"/> is the "sid" claim off the caller's own access token — used only to
/// report back <see cref="RevokeSessionResponse.WasCurrentSession"/>, never to change which session is
/// looked up.
/// </summary>
public sealed record RevokeSessionCommand(Guid UserId, Guid SessionId, Guid? CurrentSessionId);
