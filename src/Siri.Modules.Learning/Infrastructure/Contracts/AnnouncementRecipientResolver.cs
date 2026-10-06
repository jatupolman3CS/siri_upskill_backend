using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

/// <summary>Implementation of <see cref="IAnnouncementRecipientResolver"/> — see that interface's own doc
/// comment for why it is declared in Notification.Contracts but implemented here. Learning already has
/// both enrollment data (<see cref="ILearningAccessContract"/>) and a reference to
/// <see cref="IUserContactReader"/> (see <c>CertificateService</c> for the existing precedent of that
/// combination).</summary>
public sealed class AnnouncementRecipientResolver(
    ILearningAccessContract learningAccessContract,
    IUserContactReader userContactReader) : IAnnouncementRecipientResolver
{
    public async Task<IReadOnlyList<AnnouncementRecipient>> GetRecipientsAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var userIds = await learningAccessContract
            .GetActiveEnrolledUserIdsAsync(courseId, cancellationToken)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
        {
            return Array.Empty<AnnouncementRecipient>();
        }

        var contactInfo = await userContactReader
            .GetUsersContactInfoAsync(userIds, cancellationToken)
            .ConfigureAwait(false);

        var recipients = new List<AnnouncementRecipient>(userIds.Count);
        foreach (var userId in userIds)
        {
            if (contactInfo.TryGetValue(userId, out var contact) && !string.IsNullOrWhiteSpace(contact.Email))
            {
                recipients.Add(new AnnouncementRecipient(userId, contact.Email, contact.DisplayName));
            }
        }

        return recipients;
    }
}
