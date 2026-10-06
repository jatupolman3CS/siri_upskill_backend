namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// One recipient of an announcement — a user with an active enrollment in the announcement's course,
/// plus the contact info actually needed to deliver it.
/// </summary>
public sealed record AnnouncementRecipient(Guid UserId, string Email, string DisplayName);

/// <summary>
/// Bundles "who is entitled to receive this course's announcements"
/// (<c>Siri.Modules.Learning.Contracts.ILearningAccessContract.GetActiveEnrolledUserIdsAsync</c>) with
/// "how to actually contact them" (<c>Siri.Modules.Identity.Contracts.IUserContactReader
/// .GetUsersContactInfoAsync</c>) behind a single interface, declared here (Notification.Contracts)
/// instead of Notification calling Learning/Identity directly — Notification cannot reference either
/// module's project without creating a circular project reference (full reasoning in
/// docs/contracts/X-31-announcement-delivery.md §3.1). Same pattern already established by
/// <see cref="ICourseOwnershipVerifier"/> (declared here, implemented by Catalog) — this is the second
/// instance of that pattern in the codebase, implemented by
/// <c>Siri.Modules.Learning.Infrastructure.Contracts.AnnouncementRecipientResolver</c>, registered from
/// <c>LearningModule.AddLearningModule</c>.
/// </summary>
public interface IAnnouncementRecipientResolver
{
    /// <summary>
    /// Every recipient with an active enrollment in <paramref name="courseId"/> right now, with contact
    /// info. A user who is active-enrolled but whose contact info cannot be resolved (should not happen in
    /// practice since <c>identity.Users.Email</c> is NOT NULL, but guarded anyway) is silently skipped —
    /// not counted, not sent to.
    /// </summary>
    Task<IReadOnlyList<AnnouncementRecipient>> GetRecipientsAsync(
        Guid courseId,
        CancellationToken cancellationToken);
}
