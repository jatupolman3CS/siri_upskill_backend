namespace Siri.Modules.Identity.Features.RevokeAllSessions;

/// <summary>
/// Input to <see cref="RevokeAllSessionsHandler.HandleAsync"/> — no client-suppliable data at all, same
/// reasoning as <c>ListSessions.ListSessionsCommand</c>'s own doc comment. <see cref="UserId"/> is
/// always <see cref="Siri.SharedKernel.IUserContext.UserId"/>. Unlike
/// <c>RevokeOtherSessions.RevokeOtherSessionsCommand</c>, there is no <c>CurrentSessionId</c> here — this
/// handler revokes literally every active session, including the current one, so it never needs to know
/// which one that is (see <see cref="RevokeAllSessionsHandler"/>'s doc comment).
/// </summary>
public sealed record RevokeAllSessionsCommand(Guid UserId);
