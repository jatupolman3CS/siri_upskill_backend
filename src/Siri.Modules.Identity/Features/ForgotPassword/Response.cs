namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>
/// Always the exact same shape/content whether or not the submitted email is a genuinely existing,
/// active account — see <c>Handler.cs</c>'s doc comment for why that is the entire point of this
/// response (same anti-enumeration contract <c>Register.RegisterResponse</c>'s own doc comment
/// documents for that feature).
/// </summary>
public sealed record ForgotPasswordResponse(string Message);
