using Microsoft.Extensions.Options;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class InstructorPayoutAccountServiceTests
{
    private static readonly ISensitiveDataProtector DataProtector =
        new SensitiveDataProtector(Options.Create(new DataProtectionOptions { EncryptionKeyBase64 = Convert.ToBase64String(new byte[32]) }));

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakeInstructorPayoutAccountRepository : IInstructorPayoutAccountRepository
    {
        public readonly Dictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT> Accounts = [];

        public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.TryGetValue(id, out var acc) ? acc : null);

        public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Values.FirstOrDefault(a => a.INSTRUCTOR_ID == instructorId));

        public Task<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>> GetVerifiedAccountsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
        {
            var idSet = instructorIds.ToHashSet();
            var dict = Accounts.Values
                .Where(a => idSet.Contains(a.INSTRUCTOR_ID) && a.VERIFIED_AT_UTC != null)
                .ToDictionary(a => a.INSTRUCTOR_ID);
            return Task.FromResult<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>>(dict);
        }

        public IQueryable<INSTRUCTOR_PAYOUT_ACCOUNT> Query() => Accounts.Values.AsQueryable();

        public void Add(INSTRUCTOR_PAYOUT_ACCOUNT account) => Accounts[account.INSTRUCTOR_PAYOUT_ACCOUNT_ID] = account;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static (InstructorPayoutAccountService Service, FakeInstructorPayoutAccountRepository Repo, FakeInstructorProfileReader Profiles) Arrange(DateTime? now = null)
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var profiles = new FakeInstructorProfileReader();
        var service = new InstructorPayoutAccountService(repo, DataProtector, new FakeClock(now ?? DateTime.UtcNow), profiles);
        return (service, repo, profiles);
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_WhenNew_CreatesAccountKeyedByTheInstructorProfileIdNotTheUserId()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123", TaxPayerType.Individual);

        var result = await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Revenue splits and payout-batch items carry the profile id; the account must carry the same key or a batch can never match it to a verified account.
        Assert.Equal(profileId, result.Value.InstructorId);
        Assert.NotEqual(userId, result.Value.InstructorId);
        Assert.Equal(profileId, repo.Accounts.Values.Single().INSTRUCTOR_ID);
        Assert.Equal("KBANK", result.Value.BankCode);
        Assert.Equal("สมชาย สบายดี", result.Value.AccountName);
        Assert.Equal(TaxPayerType.Individual, result.Value.TaxPayerType);
        Assert.Equal("***-***-0123", result.Value.TaxId);
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_UserWithoutAnInstructorProfile_IsForbiddenAndStoresNothing()
    {
        var (service, repo, _) = Arrange();
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123");

        var result = await service.CreateForCurrentUserAsync(Guid.NewGuid(), command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(repo.Accounts);
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_StoresAccountNumberEncrypted_NotPlaintext()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123");

        await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        var stored = repo.Accounts.Values.Single();
        Assert.NotEqual("0123456789", stored.ACCOUNT_NO_ENCRYPTED);
        Assert.Equal("0123456789", DataProtector.Decrypt(stored.ACCOUNT_NO_ENCRYPTED));
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_WhenTheProfileAlreadyHasAnAccount_ReturnsConflict()
    {
        var (service, _, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123");

        await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);
        var result2 = await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        Assert.False(result2.IsSuccess);
        Assert.Equal("conflict", result2.Error.Code);
    }

    [Fact]
    public async Task GetForCurrentUserAsync_WhenExists_ReturnsTheUsersOwnAccountWithMaskedNumber()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());
        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileId, "SCB", DataProtector.Encrypt("9998887776"), "สมชาย สบายดี", null);
        repo.Add(account);

        var result = await service.GetForCurrentUserAsync(userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.INSTRUCTOR_PAYOUT_ACCOUNT_ID, result.Value.Id);
        Assert.Equal("SCB", result.Value.BankCode);
        Assert.Equal("***-***-7776", result.Value.MaskedAccountNo);
    }

    [Fact]
    public async Task GetForCurrentUserAsync_NeverReturnsAnotherInstructorsAccount()
    {
        var (service, repo, profiles) = Arrange();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var profileA = profiles.Map(userA, Guid.NewGuid());
        var profileB = profiles.Map(userB, Guid.NewGuid());
        var accountA = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileA, "SCB", DataProtector.Encrypt("1111111111"), "A", null);
        var accountB = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileB, "KBANK", DataProtector.Encrypt("2222222222"), "B", null);
        repo.Add(accountA);
        repo.Add(accountB);

        var asB = await service.GetForCurrentUserAsync(userB, CancellationToken.None);

        Assert.True(asB.IsSuccess);
        Assert.Equal(accountB.INSTRUCTOR_PAYOUT_ACCOUNT_ID, asB.Value.Id);
        Assert.Equal("B", asB.Value.AccountName);
    }

    [Fact]
    public async Task GetForCurrentUserAsync_UserWithoutProfile_IsNotFound_EvenWhenAnAccountExistsForSomeoneElse()
    {
        var (service, repo, profiles) = Arrange();
        profiles.Map(Guid.NewGuid(), Guid.NewGuid());
        repo.Add(INSTRUCTOR_PAYOUT_ACCOUNT.Create(Guid.NewGuid(), "SCB", DataProtector.Encrypt("1111111111"), "Other", null));

        var result = await service.GetForCurrentUserAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task VerifyAsync_ByInstructorProfileId_SetsVerifiedAtUtc()
    {
        var verifiedTime = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var (service, repo, _) = Arrange(verifiedTime);

        // The admin sees the profile id on revenue splits and batch items and verifies the account with that same id.
        var profileId = Guid.NewGuid();
        repo.Add(INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileId, "SCB", DataProtector.Encrypt("9998887776"), "สมชาย สบายดี", null));

        var result = await service.VerifyAsync(profileId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(verifiedTime, result.Value.VerifiedAtUtc);
    }
}
