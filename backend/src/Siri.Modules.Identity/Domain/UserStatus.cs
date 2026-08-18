namespace Siri.Modules.Identity.Domain;

/// <summary>
/// Lifecycle state of a <see cref="User"/> account. Stored as a string in the database (see
/// <c>Infrastructure/UserConfiguration.cs</c>) per database.md's enum convention.
/// </summary>
public enum UserStatus
{
    /// <summary>Registered but the email address has not been confirmed yet. Cannot sign in.</summary>
    PendingEmailConfirmation,

    /// <summary>Normal, usable account.</summary>
    Active,

    /// <summary>Temporarily blocked by an admin/security action. Can be reactivated.</summary>
    Suspended,

    /// <summary>
    /// The account holder asked to be deleted. Per SECURITY.md's PDPA rules this is an
    /// anonymize-in-place transition, not a row deletion — the row (and any Orders/Enrollments
    /// referencing it) must survive for legal/accounting retention.
    /// </summary>
    Deleted,
}
