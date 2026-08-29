namespace Siri.Modules.Identity.Features.AnonymizeAccount;

/// <summary>
/// Request payload sent by client to request account deletion & anonymization (P7-04).
/// </summary>
public sealed record AnonymizeAccountRequest(
    string? Password,
    string Confirmation);

public sealed record AnonymizeAccountCommand(
    Guid UserId,
    string? Password,
    string Confirmation);

public sealed record AnonymizeAccountResponse(
    string Message);
