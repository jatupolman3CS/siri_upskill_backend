using Microsoft.Extensions.Options;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/>. "Sign in with Google" uses Google
/// Identity Services' ID-token flow: the browser obtains a signed ID token from Google and posts it to
/// this API, which verifies the signature against Google's published keys and the audience against
/// <see cref="ClientId"/>. A public OAuth client id is the only value needed — no client secret is
/// involved, so nothing here is sensitive. An empty <see cref="ClientId"/> disables Google sign-in
/// (the endpoint answers "not available" and the web app hides the button).
/// </summary>
public sealed class GoogleLoginOptions
{
    public const string SectionName = "Identity:ExternalLogin:Google";

    public const string DefaultDiscoveryUrl = "https://accounts.google.com/.well-known/openid-configuration";

    /// <summary>OAuth 2.0 web client id from Google Cloud Console (ends in
    /// <c>.apps.googleusercontent.com</c>). Every accepted ID token must have been issued to this id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OpenID Connect discovery document the issuer and signing keys are read from. Only
    /// overridden to point at a local fake provider in development/tests — never taken from a request.</summary>
    public string DiscoveryUrl { get; set; } = DefaultDiscoveryUrl;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>Fails the host at startup (<c>ValidateOnStart</c>) on a malformed discovery URL instead of
/// at the first sign-in attempt.</summary>
public sealed class GoogleLoginOptionsValidator : IValidateOptions<GoogleLoginOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleLoginOptions options)
    {
        if (!Uri.TryCreate(options.DiscoveryUrl, UriKind.Absolute, out var discoveryUri))
        {
            return ValidateOptionsResult.Fail($"{GoogleLoginOptions.SectionName}:DiscoveryUrl must be an absolute URL.");
        }

        // Key material is fetched over this URL, so plain HTTP is only tolerable towards the local machine.
        if (discoveryUri.Scheme != Uri.UriSchemeHttps && !discoveryUri.IsLoopback)
        {
            return ValidateOptionsResult.Fail($"{GoogleLoginOptions.SectionName}:DiscoveryUrl must use https.");
        }

        return ValidateOptionsResult.Success;
    }
}
