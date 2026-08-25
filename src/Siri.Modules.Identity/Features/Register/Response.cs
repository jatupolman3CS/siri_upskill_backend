namespace Siri.Modules.Identity.Features.Register;

/// <summary>
/// Always the exact same shape/content whether or not the submitted email was already registered —
/// see <c>Handler.cs</c>'s doc comment for why that is the entire point of this response.
/// </summary>
public sealed record RegisterResponse(string Message);
