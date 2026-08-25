namespace Siri.Modules.Identity.Contracts;

/// <summary>
/// Read-only access to a user's contact details for other modules that need to send them something
/// (e.g. Commerce queuing a receipt email after a paid order) but have no reason to see anything else
/// about the account. Added because Commerce previously had no way to resolve a real email address and
/// fell back to a fabricated <c>@example.test</c> address whenever Stripe's own
/// <c>PaymentIntent.ReceiptEmail</c> was empty — which, as of this fix, is every payment, since nothing
/// upstream ever populates it. <c>identity.Users.Email</c> is the actual source of truth.
/// </summary>
public interface IUserContactReader
{
    /// <summary><c>null</c> if <paramref name="userId"/> does not exist.</summary>
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Returns email and display name for a user.</summary>
    Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken);
}
