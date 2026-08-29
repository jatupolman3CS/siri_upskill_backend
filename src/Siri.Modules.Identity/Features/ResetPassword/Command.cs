namespace Siri.Modules.Identity.Features.ResetPassword;

/// <summary>
/// Request payload for POST /api/identity/reset-password. Binds from the JSON request body (default
/// minimal-API inference for a non-primitive parameter type). <see cref="Token"/> is the raw (unhashed)
/// value from a reset link's <c>?token=</c> query parameter — same "submitted via POST so an email
/// client's link-prefetch can never silently consume a one-time token" reasoning
/// <see cref="ConfirmEmail.ConfirmEmailCommand"/>'s own doc comment lays out; the frontend reset page
/// (out of scope here) reads it off the URL on a plain GET, then POSTs it here itself.
/// <para>
/// IP address is deliberately <b>not</b> here — same "server's own view of the request, never trusted
/// from client-supplied JSON" reasoning <see cref="Login.LoginCommand"/>'s doc comment gives for
/// USER-Agent/IP; <see cref="ResetPasswordEndpoint"/> reads it separately and passes it into
/// <see cref="ResetPasswordHandler.HandleAsync"/> as its own parameter, purely for the
/// <c>SECURITY_AUDIT</c> entry written on success.
/// </para>
/// </summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword);
