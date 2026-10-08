namespace Siri.Modules.Identity.Features.GetMe;

/// <summary>
/// Input to <see cref="GetMeHandler.HandleAsync"/> — deliberately carries nothing a client could
/// supply: <see cref="UserId"/> is <see cref="Siri.SharedKernel.IUserContext.UserId"/> (read from the
/// verified access token by the controller), never a route value, query string or body field. There is
/// therefore no way to ask for anybody's profile but your own — the only way to see a different
/// account's data is to authenticate as that account (docs/SECURITY.md IDOR rule).
/// </summary>
public sealed record GetMeQuery(Guid UserId);
