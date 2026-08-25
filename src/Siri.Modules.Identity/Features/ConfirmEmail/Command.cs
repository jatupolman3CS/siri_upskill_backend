namespace Siri.Modules.Identity.Features.ConfirmEmail;

/// <summary>
/// The raw (unhashed) token from a confirmation link's <c>?token=</c> query parameter. Submitted via
/// POST (not GET) specifically so that email clients/security scanners that prefetch links found
/// inside emails can never silently consume a one-time confirmation token just by generating a link
/// preview — a known real-world failure mode of GET-with-side-effects confirmation endpoints. The
/// frontend confirmation page (out of scope for this backend-only task) reads the token from the URL
/// on a plain GET navigation, then POSTs it here itself.
/// </summary>
public sealed record ConfirmEmailCommand(string Token);
