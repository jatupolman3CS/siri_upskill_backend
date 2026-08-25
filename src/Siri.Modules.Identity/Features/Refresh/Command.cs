namespace Siri.Modules.Identity.Features.Refresh;

/// <summary>
/// Input to <see cref="RefreshHandler.HandleAsync"/> — deliberately <b>not</b> bound from the JSON
/// request body by minimal API. <see cref="RefreshEndpoint"/> builds this by hand from
/// <c>HttpContext.Request.Cookies</c> (the raw refresh token) and the server's own view of the request
/// (User-Agent/IP, for the reuse-detection audit entry, same "never trust the body" reasoning
/// <see cref="Login.LoginCommand"/>'s doc comment lays out for those two fields) — a client can never
/// supply <see cref="RawRefreshToken"/> itself through this endpoint (task instruction: "reads the
/// refresh token from the httpOnly cookie ... not accept it as a bindable command property from
/// client-supplied JSON").
/// </summary>
public sealed record RefreshCommand(string RawRefreshToken, string? UserAgent, string? IpAddress);
