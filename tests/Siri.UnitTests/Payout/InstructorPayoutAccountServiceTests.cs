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

    [Fact]
    public async Task CreateForCurrentUserAsync_WhenNew_CreatesAccount()
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new InstructorPayoutAccountService(repo, DataProtector, clock);

        var userId = Guid.NewGuid();
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123", TaxPayerType.Individual);

        var result = await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.InstructorId);
        Assert.Equal("KBANK", result.Value.BankCode);
        Assert.Equal("สมชาย สบายดี", result.Value.AccountName);
        Assert.Equal(TaxPayerType.Individual, result.Value.TaxPayerType);
        Assert.Equal("***-***-0123", result.Value.TaxId);
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_StoresAccountNumberEncrypted_NotPlaintext()
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new InstructorPayoutAccountService(repo, DataProtector, clock);

        var userId = Guid.NewGuid();
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123");

        await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        var stored = repo.Accounts.Values.Single();
        Assert.NotEqual("0123456789", stored.ACCOUNT_NO_ENCRYPTED);
        Assert.Equal("0123456789", DataProtector.Decrypt(stored.ACCOUNT_NO_ENCRYPTED));
    }

    [Fact]
    public async Task CreateForCurrentUserAsync_WhenAlreadyExists_ReturnsConflict()
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new InstructorPayoutAccountService(repo, DataProtector, clock);

        var userId = Guid.NewGuid();
        var command = new CreateInstructorPayoutAccountCommand("KBANK", "0123456789", "สมชาย สบายดี", "1234567890123");

        await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);
        var result2 = await service.CreateForCurrentUserAsync(userId, command, CancellationToken.None);

        Assert.False(result2.IsSuccess);
        Assert.Equal("conflict", result2.Error.Code);
    }

    [Fact]
    public async Task GetForCurrentUserAsync_WhenExists_ReturnsAccountWithMaskedNumber()
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new InstructorPayoutAccountService(repo, DataProtector, clock);

        var userId = Guid.NewGuid();
        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(userId, "SCB", DataProtector.Encrypt("9998887776"), "สมชาย สบายดี", null);
        repo.Add(account);

        var result = await service.GetForCurrentUserAsync(userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.INSTRUCTOR_PAYOUT_ACCOUNT_ID, result.Value.Id);
        Assert.Equal("SCB", result.Value.BankCode);
        Assert.Equal("***-***-7776", result.Value.MaskedAccountNo);
    }

    [Fact]
    public async Task VerifyAsync_WhenAccountExists_SetsVerifiedAtUtc()
    {
        var repo = new FakeInstructorPayoutAccountRepository();
        var verifiedTime = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(verifiedTime);
        var service = new InstructorPayoutAccountService(repo, DataProtector, clock);

        var userId = Guid.NewGuid();
        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(userId, "SCB", DataProtector.Encrypt("9998887776"), "สมชาย สบายดี", null);
        repo.Add(account);

        var result = await service.VerifyAsync(userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(verifiedTime, result.Value.VerifiedAtUtc);
    }
}
