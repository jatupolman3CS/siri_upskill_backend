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

    // ---- UpsertForCurrentUserAsync (PUT /api/payout/instructor/payout-account) --------------------------------------

    private static CreateInstructorPayoutAccountCommand Details(
        string bankCode = "KBANK",
        string accountNo = "0123456789",
        string accountName = "สมชาย สบายดี",
        string? taxId = "1234567890123",
        TaxPayerType taxPayerType = TaxPayerType.Individual) =>
        new(bankCode, accountNo, accountName, taxId, taxPayerType);

    private static async Task<Guid> SaveVerifiedAccountAsync(
        InstructorPayoutAccountService service, FakeInstructorPayoutAccountRepository repo, FakeInstructorProfileReader profiles, Guid userId, CreateInstructorPayoutAccountCommand details)
    {
        var profileId = profiles.Map(userId, Guid.NewGuid());
        var created = await service.UpsertForCurrentUserAsync(userId, details, CancellationToken.None);
        Assert.True(created.IsSuccess);
        await service.VerifyAsync(profileId, CancellationToken.None);
        Assert.NotNull(repo.Accounts.Values.Single().VERIFIED_AT_UTC);
        return profileId;
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_WhenNoAccountYet_CreatesItKeyedByTheProfileIdAndReportsCreated()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());

        var result = await service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal(profileId, result.Value.Account.InstructorId);
        Assert.NotEqual(userId, result.Value.Account.InstructorId);
        Assert.Equal(profileId, repo.Accounts.Values.Single().INSTRUCTOR_ID);
        Assert.Equal("KBANK", result.Value.Account.BankCode);
        Assert.Equal("***-***-6789", result.Value.Account.MaskedAccountNo);
        Assert.Equal("***-***-0123", result.Value.Account.TaxId);
        Assert.Null(result.Value.Account.VerifiedAtUtc);
        Assert.Equal(1, repo.SaveCalls);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_StoresTheAccountNumberAndTaxIdEncrypted_NeverPlaintext()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());

        await service.UpsertForCurrentUserAsync(userId, Details(accountNo: "0123456789", taxId: "1234567890123"), CancellationToken.None);

        var stored = repo.Accounts.Values.Single();
        Assert.DoesNotContain("0123456789", stored.ACCOUNT_NO_ENCRYPTED);
        Assert.DoesNotContain("1234567890123", stored.TAX_ID);
        Assert.Equal("0123456789", DataProtector.Decrypt(stored.ACCOUNT_NO_ENCRYPTED));
        Assert.Equal("1234567890123", DataProtector.Decrypt(stored.TAX_ID!));
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_UserWithoutAnInstructorProfile_IsForbiddenAndStoresNothing()
    {
        var (service, repo, _) = Arrange();

        var result = await service.UpsertForCurrentUserAsync(Guid.NewGuid(), Details(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(repo.Accounts);
        Assert.Equal(0, repo.SaveCalls);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_WhenAnAccountExists_UpdatesTheSameRowInsteadOfConflicting_AndReportsNotCreated()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());
        var first = await service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None);

        var second = await service.UpsertForCurrentUserAsync(
            userId, Details(bankCode: "SCB", accountNo: "9998887776", accountName: "สมหญิง ใจดี", taxId: "9999999999999", taxPayerType: TaxPayerType.Corporate), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Account.Id, second.Value.Account.Id);
        var stored = Assert.Single(repo.Accounts.Values);
        Assert.Equal("SCB", stored.BANK_CODE);
        Assert.Equal("สมหญิง ใจดี", stored.ACCOUNT_NAME);
        Assert.Equal(TaxPayerType.Corporate, stored.TAX_PAYER_TYPE);
        Assert.Equal("9998887776", DataProtector.Decrypt(stored.ACCOUNT_NO_ENCRYPTED));
        Assert.Equal("9999999999999", DataProtector.Decrypt(stored.TAX_ID!));
        Assert.Equal("***-***-7776", second.Value.Account.MaskedAccountNo);
        Assert.Equal(2, repo.SaveCalls);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_IdenticalResubmission_KeepsTheVerificationAndWritesNothing()
    {
        var (service, repo, profiles) = Arrange(new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc));
        var userId = Guid.NewGuid();
        await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details());
        var savesBefore = repo.SaveCalls;
        var ciphertextBefore = repo.Accounts.Values.Single().ACCOUNT_NO_ENCRYPTED;

        var again = await service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Created);
        Assert.Equal(new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc), again.Value.Account.VerifiedAtUtc);
        Assert.NotNull(repo.Accounts.Values.Single().VERIFIED_AT_UTC);
        Assert.Equal(savesBefore, repo.SaveCalls);
        Assert.Equal(ciphertextBefore, repo.Accounts.Values.Single().ACCOUNT_NO_ENCRYPTED);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_IdenticalValuesWithSurroundingWhitespace_AreStillTheSameAccount()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details());

        var padded = await service.UpsertForCurrentUserAsync(
            userId, Details(bankCode: " KBANK ", accountNo: " 0123456789 ", accountName: "  สมชาย สบายดี ", taxId: " 1234567890123 "), CancellationToken.None);

        Assert.True(padded.IsSuccess);
        Assert.NotNull(padded.Value.Account.VerifiedAtUtc);
    }

    private static CreateInstructorPayoutAccountCommand WithOneDetailChanged(string detail) => detail switch
    {
        "bank code" => Details(bankCode: "SCB"),
        "account number" => Details(accountNo: "0123456780"),
        "account holder name" => Details(accountName: "สมชาย สบายดีมาก"),
        "tax id" => Details(taxId: "1234567890124"),
        "tax id removed" => Details(taxId: null),
        "tax payer type" => Details(taxPayerType: TaxPayerType.Corporate),
        _ => throw new ArgumentOutOfRangeException(nameof(detail), detail, null),
    };

    [Theory]
    [InlineData("bank code")]
    [InlineData("account number")]
    [InlineData("account holder name")]
    [InlineData("tax id")]
    [InlineData("tax id removed")]
    [InlineData("tax payer type")]
    public async Task UpsertForCurrentUserAsync_ChangingAnyDetail_MakesTheAccountUnverifiedAgain_SoTheBatchSkipsIt(string changed)
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details());
        Assert.Contains(profileId, (await repo.GetVerifiedAccountsAsync([profileId], CancellationToken.None)).Keys);

        var result = await service.UpsertForCurrentUserAsync(userId, WithOneDetailChanged(changed), CancellationToken.None);

        Assert.True(result.IsSuccess, changed);
        Assert.False(result.Value.Created);
        Assert.Null(result.Value.Account.VerifiedAtUtc);
        Assert.Null(repo.Accounts.Values.Single().VERIFIED_AT_UTC);
        // Payout-batch creation and the transfer-file export both read through this query: an unverified account is invisible to them.
        Assert.Empty(await repo.GetVerifiedAccountsAsync([profileId], CancellationToken.None));
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_AfterAChangeAnAdminCanVerifyTheNewDetailsAgain()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details());
        await service.UpsertForCurrentUserAsync(userId, Details(accountNo: "5555555555"), CancellationToken.None);
        Assert.Empty(await repo.GetVerifiedAccountsAsync([profileId], CancellationToken.None));

        var verified = await service.VerifyAsync(profileId, CancellationToken.None);

        Assert.True(verified.IsSuccess);
        Assert.NotNull(verified.Value.VerifiedAtUtc);
        Assert.Contains(profileId, (await repo.GetVerifiedAccountsAsync([profileId], CancellationToken.None)).Keys);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_ChangingAnAccountThatWasNeverVerified_StaysUnverified()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());
        await service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None);

        var result = await service.UpsertForCurrentUserAsync(userId, Details(accountNo: "1111111111"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Account.VerifiedAtUtc);
        Assert.Equal("1111111111", DataProtector.Decrypt(repo.Accounts.Values.Single().ACCOUNT_NO_ENCRYPTED));
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_BlankTaxId_MeansNoTaxId_AndIsNotAChangeWhenNoneIsStored()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details(taxId: null));
        var savesBefore = repo.SaveCalls;

        var result = await service.UpsertForCurrentUserAsync(userId, Details(taxId: "   "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Account.TaxId);
        Assert.NotNull(result.Value.Account.VerifiedAtUtc);
        Assert.Equal(savesBefore, repo.SaveCalls);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_RemovingTheTaxId_ClearsItAndResetsVerification()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        await SaveVerifiedAccountAsync(service, repo, profiles, userId, Details(taxId: "1234567890123"));

        var result = await service.UpsertForCurrentUserAsync(userId, Details(taxId: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(repo.Accounts.Values.Single().TAX_ID);
        Assert.Null(result.Value.Account.TaxId);
        Assert.Null(result.Value.Account.VerifiedAtUtc);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_NeverTouchesAnotherInstructorsAccount()
    {
        var (service, repo, profiles) = Arrange();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var profileA = await SaveVerifiedAccountAsync(service, repo, profiles, userA, Details(accountNo: "1111111111", accountName: "A"));
        var profileB = profiles.Map(userB, Guid.NewGuid());

        var asB = await service.UpsertForCurrentUserAsync(userB, Details(bankCode: "SCB", accountNo: "2222222222", accountName: "B"), CancellationToken.None);

        Assert.True(asB.IsSuccess);
        Assert.True(asB.Value.Created);
        Assert.Equal(profileB, asB.Value.Account.InstructorId);
        Assert.Equal(2, repo.Accounts.Count);
        var accountA = repo.Accounts.Values.Single(a => a.INSTRUCTOR_ID == profileA);
        Assert.Equal("KBANK", accountA.BANK_CODE);
        Assert.Equal("A", accountA.ACCOUNT_NAME);
        Assert.Equal("1111111111", DataProtector.Decrypt(accountA.ACCOUNT_NO_ENCRYPTED));
        Assert.NotNull(accountA.VERIFIED_AT_UTC);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_WhenAConcurrentFirstSaveWinsTheRace_AppliesThisRequestAsAnUpdateOfTheWinnersRow()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());

        // The other request commits its insert between this request's "no account yet" read and its own insert.
        var winner = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileId, "KBANK", DataProtector.Encrypt("0123456789"), "สมชาย สบายดี", DataProtector.Encrypt("1234567890123"));
        winner.Verify(new FakeClock(new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc)));
        repo.FailNextSaveWith = new Microsoft.EntityFrameworkCore.DbUpdateException("duplicate key value violates unique constraint");
        repo.BeforeFailingSave = () => repo.Accounts[winner.INSTRUCTOR_PAYOUT_ACCOUNT_ID] = winner;

        // Same details as the winner: the loser must end in the same state without un-verifying the winner's row.
        var result = await service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Equal(winner.INSTRUCTOR_PAYOUT_ACCOUNT_ID, result.Value.Account.Id);
        Assert.Equal(1, repo.DiscardCalls);
        Assert.Same(winner, Assert.Single(repo.Accounts.Values));
        Assert.NotNull(winner.VERIFIED_AT_UTC);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_WhenTheRaceLoserHasDifferentDetails_UpdatesTheWinnersRowAndResetsItsVerification()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());
        var winner = INSTRUCTOR_PAYOUT_ACCOUNT.Create(profileId, "KBANK", DataProtector.Encrypt("0123456789"), "สมชาย สบายดี", null);
        winner.Verify(new FakeClock(DateTime.UtcNow));
        repo.FailNextSaveWith = new Microsoft.EntityFrameworkCore.DbUpdateException("duplicate key value violates unique constraint");
        repo.BeforeFailingSave = () => repo.Accounts[winner.INSTRUCTOR_PAYOUT_ACCOUNT_ID] = winner;

        var result = await service.UpsertForCurrentUserAsync(userId, Details(accountNo: "7777777777", taxId: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Equal("7777777777", DataProtector.Decrypt(winner.ACCOUNT_NO_ENCRYPTED));
        Assert.Null(winner.VERIFIED_AT_UTC);
    }

    [Fact]
    public async Task UpsertForCurrentUserAsync_WhenTheInsertFailsButNoRowAppeared_TheFailureIsNotSwallowed()
    {
        var (service, repo, profiles) = Arrange();
        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());
        repo.FailNextSaveWith = new Microsoft.EntityFrameworkCore.DbUpdateException("connection reset");

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => service.UpsertForCurrentUserAsync(userId, Details(), CancellationToken.None));

        Assert.Empty(repo.Accounts);
    }
}
