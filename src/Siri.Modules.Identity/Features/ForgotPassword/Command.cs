namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>Request payload for POST /api/identity/forgot-password. Binds from the JSON request body
/// (default minimal-API inference for a non-primitive parameter type) — deliberately just the email;
/// see <c>Handler.cs</c>'s doc comment for why nothing else about the caller is ever accepted here.</summary>
public sealed record ForgotPasswordCommand(string Email);
