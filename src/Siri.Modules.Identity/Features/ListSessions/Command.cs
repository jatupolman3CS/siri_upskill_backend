namespace Siri.Modules.Identity.Features.ListSessions;

/// <summary>
/// Input to <see cref="ListSessionsHandler.HandleAsync"/> — deliberately carries no client-suppliable
/// data at all, not even as an optional field. The only two things this query needs are the caller's
/// own id and which of their sessions (if any) the current request itself came from, and both are read
/// exclusively from server-trusted sources by <see cref="ListSessionsEndpoint"/> — never bound from the
/// request body, a query string, or a route value. Accepting a client-supplied <c>userId</c> here
/// (query param, route param, or body) would be a direct IDOR against docs/SECURITY.md's explicit IDOR
/// rule (task instruction: "this must be impossible to query"), so this record has no bindable property
/// that could ever carry one — the only way to influence which sessions come back is to authenticate as
/// a different user.
/// <list type="bullet">
/// <item><see cref="UserId"/> — <see cref="Siri.SharedKernel.IUserContext.UserId"/>, the authenticated
/// caller's own id.</item>
/// <item><see cref="CurrentSessionId"/> — the "sid" claim off the caller's own access token
/// (<see cref="Infrastructure.Endpoints.CurrentSessionClaim.Read"/>), so the handler can flag which
/// returned session (if any) is the one making this very request. <c>null</c> for a token that carries
/// no such claim — see <see cref="Infrastructure.Endpoints.CurrentSessionClaim"/>'s own doc comment for
/// why that is never treated as an error.</item>
/// </list>
/// </summary>
public sealed record ListSessionsCommand(Guid UserId, Guid? CurrentSessionId);
