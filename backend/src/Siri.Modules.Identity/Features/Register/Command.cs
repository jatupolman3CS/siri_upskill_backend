namespace Siri.Modules.Identity.Features.Register;

/// <summary>Request payload for POST /api/identity/register. Binds from the JSON request body
/// (default minimal-API inference for a non-primitive parameter type).</summary>
public sealed record RegisterCommand(string Email, string Password, string DisplayName);
