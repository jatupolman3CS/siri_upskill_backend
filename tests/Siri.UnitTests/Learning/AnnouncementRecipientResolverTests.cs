using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

/// <summary>
/// Covers <see cref="AnnouncementRecipientResolver"/> (X-31) — bundles
/// <see cref="ILearningAccessContract.GetActiveEnrolledUserIdsAsync"/> with
/// <see cref="IUserContactReader.GetUsersContactInfoAsync"/> behind
/// <see cref="IAnnouncementRecipientResolver"/>. Uses fakes for both dependencies (neither needs a real
/// database for this class's own logic — the DB-backed pieces are covered separately by
/// <c>LearningAccessContractTests</c>/integration tests).
/// </summary>
public sealed class AnnouncementRecipientResolverTests
{
    [Fact]
    public async Task GetRecipientsAsync_ActiveEnrolledUsersWithContactInfo_ReturnsBundledRecipients()
    {
        var courseId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        var accessContract = new FakeLearningAccessContract
        {
            ActiveEnrolledUserIds = { [courseId] = new HashSet<Guid> { user1, user2 } },
        };
        var contactReader = new FakeUserContactReader
        {
            Contacts =
            {
                [user1] = ("user1@example.test", "User One"),
                [user2] = ("user2@example.test", "User Two"),
            },
        };

        var resolver = new AnnouncementRecipientResolver(accessContract, contactReader);

        var recipients = await resolver.GetRecipientsAsync(courseId, CancellationToken.None);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.UserId == user1 && r.Email == "user1@example.test" && r.DisplayName == "User One");
        Assert.Contains(recipients, r => r.UserId == user2 && r.Email == "user2@example.test" && r.DisplayName == "User Two");
    }

    [Fact]
    public async Task GetRecipientsAsync_UserWithUnresolvableContactInfo_IsSkippedNotThrown()
    {
        var courseId = Guid.NewGuid();
        var resolvableUser = Guid.NewGuid();
        var unresolvableUser = Guid.NewGuid();

        var accessContract = new FakeLearningAccessContract
        {
            ActiveEnrolledUserIds = { [courseId] = new HashSet<Guid> { resolvableUser, unresolvableUser } },
        };
        var contactReader = new FakeUserContactReader
        {
            // unresolvableUser deliberately absent from Contacts — simulates GetUsersContactInfoAsync not
            // finding a matching row.
            Contacts = { [resolvableUser] = ("resolvable@example.test", "Resolvable User") },
        };

        var resolver = new AnnouncementRecipientResolver(accessContract, contactReader);

        var recipients = await resolver.GetRecipientsAsync(courseId, CancellationToken.None);

        Assert.Single(recipients);
        Assert.Equal(resolvableUser, recipients[0].UserId);
    }

    [Fact]
    public async Task GetRecipientsAsync_NoActiveEnrolledUsers_ReturnsEmptyWithoutCallingContactReader()
    {
        var courseId = Guid.NewGuid();
        var accessContract = new FakeLearningAccessContract();
        var contactReader = new FakeUserContactReader();

        var resolver = new AnnouncementRecipientResolver(accessContract, contactReader);

        var recipients = await resolver.GetRecipientsAsync(courseId, CancellationToken.None);

        Assert.Empty(recipients);
        Assert.Equal(0, contactReader.GetUsersContactInfoCallCount);
    }

    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public Dictionary<Guid, HashSet<Guid>> ActiveEnrolledUserIds { get; } = [];

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(
                ActiveEnrolledUserIds.TryGetValue(courseId, out var ids) ? ids : new HashSet<Guid>());
    }

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public Dictionary<Guid, (string Email, string DisplayName)> Contacts { get; } = [];

        public int GetUsersContactInfoCallCount { get; private set; }

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Contacts.TryGetValue(userId, out var contact) ? contact.Email : null);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Contacts.TryGetValue(userId, out var contact) ? ((string?)contact.Email, (string?)contact.DisplayName) : (null, null));

        public Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
        {
            GetUsersContactInfoCallCount++;
            var idSet = userIds.ToHashSet();
            var result = Contacts
                .Where(kvp => idSet.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            return Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(result);
        }
    }
}
