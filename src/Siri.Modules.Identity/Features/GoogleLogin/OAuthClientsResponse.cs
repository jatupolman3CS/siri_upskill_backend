namespace Siri.Modules.Identity.Features.GoogleLogin;

/// <summary>Public OAuth client ids the web app needs to render third-party sign-in buttons. A
/// provider that is not configured is <c>null</c> so the app simply does not show its button. Client
/// ids are public by design (they sit in every page that renders the button) — no secret is exposed.</summary>
public sealed record OAuthClientsResponse(OAuthClient? Google);

public sealed record OAuthClient(string ClientId);
