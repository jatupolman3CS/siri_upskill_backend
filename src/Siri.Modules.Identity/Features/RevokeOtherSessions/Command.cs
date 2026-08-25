namespace Siri.Modules.Identity.Features.RevokeOtherSessions;

/// <summary>
/// Input to <see cref="RevokeOtherSessionsHandler.HandleAsync"/> — no client-suppliable data at all,
/// same reasoning as <c>ListSessions.ListSessionsCommand</c>'s own doc comment. <see cref="UserId"/> is
/// always <see cref="Siri.SharedKernel.IUserContext.UserId"/>; <see cref="CurrentSessionId"/> is the
/// "sid" claim off the caller's own access token — the one session this endpoint's whole purpose is to
/// exclude from revocation (see <see cref="RevokeOtherSessionsHandler"/>'s doc comment for why "every
/// session except the current one" is this endpoint's behavior, and
/// <c>RevokeAllSessions.RevokeAllSessionsHandler</c> for the literal-everything variant).
/// </summary>
public sealed record RevokeOtherSessionsCommand(Guid UserId, Guid? CurrentSessionId);
