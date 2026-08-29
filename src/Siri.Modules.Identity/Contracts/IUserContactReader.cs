namespace Siri.Modules.Identity.Contracts;

/// <summary>
/// Read-only access to a user's contact details for other modules that need to send them something
/// (e.g. Commerce queuing a receipt email after a paid order) but have no reason to see anything else
/// about the account.
/// </summary>
public interface IUserContactReader
{
    /// <summary><c>null</c> if <paramref name="userId"/> does not exist.</summary>
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Returns email and display name for a user.</summary>
    Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Batch returns email and display name for multiple users.</summary>
    Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(new Dictionary<Guid, (string Email, string DisplayName)>());
}
