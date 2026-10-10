namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// Answers "has an admin approved this user's instructor application?" for the <c>InstructorOnly</c> authorization policy.
/// The <c>Instructor</c> role in a user's token alone is not proof: it can also be present without an approved application (an admin
/// assigning the role by hand, an invite, a seed), and until an admin has approved the application the only instructor-facing thing
/// that user may use is the application itself (<c>/api/catalog/instructors/apply</c>, <c>/me</c>). Identity owns roles and Catalog
/// owns applications, so the policy reaches the approval through this contract instead of reading Catalog tables itself.
/// </summary>
public interface IInstructorApprovalReader
{
    /// <summary>
    /// <c>true</c> only when <paramref name="userId"/> owns an instructor profile whose status is <c>Approved</c>. Pending, rejected,
    /// no application at all, and <see cref="Guid.Empty"/> are all <c>false</c>. Read-only; never throws for an unknown user.
    /// </summary>
    Task<bool> IsApprovedAsync(Guid userId, CancellationToken cancellationToken);
}
