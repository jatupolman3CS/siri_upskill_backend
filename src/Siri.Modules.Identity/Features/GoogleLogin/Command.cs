namespace Siri.Modules.Identity.Features.GoogleLogin;

/// <summary>
/// Request payload for POST /api/identity/external-login/google. <see cref="IdToken"/> is the
/// <c>credential</c> Google Identity Services hands the browser after the user picks a Google account.
/// It is never trusted as-is: the handler verifies its signature, issuer, audience and lifetime
/// against Google before reading a single claim out of it. Device fields are self-reported, exactly
/// like <c>LoginCommand</c>'s.
/// </summary>
public sealed record GoogleLoginCommand(string IdToken, string? DeviceId, string? DeviceName);
