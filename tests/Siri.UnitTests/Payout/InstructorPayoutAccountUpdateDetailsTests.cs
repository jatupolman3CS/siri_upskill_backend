using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Payout;

/// <summary>
/// <see cref="INSTRUCTOR_PAYOUT_ACCOUNT.UpdateDetails"/>: the owner editing their own payout account, and the rule that any change to what an admin verified takes
/// the verification away (payout batches and the transfer export only see verified accounts).
/// </summary>
public sealed class InstructorPayoutAccountUpdateDetailsTests
{
    private static readonly DateTime VerifiedAt = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static INSTRUCTOR_PAYOUT_ACCOUNT NewAccount(bool verified)
    {
        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            Guid.NewGuid(), "KBANK", "enc-account-no", "สมชาย สบายดี", "enc-tax-id", TaxPayerType.Individual);
        if (verified)
        {
            account.Verify(new FixedClock(VerifiedAt));
        }

        return account;
    }

    /// <summary>Re-submits the account exactly as stored, optionally overriding single arguments.</summary>
    private static PayoutAccountUpdate Resubmit(
        INSTRUCTOR_PAYOUT_ACCOUNT account,
        string? bankCode = null,
        string? accountNameValue = null,
        TaxPayerType? taxPayerType = null,
        bool accountNoChanged = false,
        string newAccountNo = "enc-new-account-no",
        bool taxIdChanged = false,
        string? newTaxId = null) =>
        account.UpdateDetails(
            bankCode ?? account.BANK_CODE,
            newAccountNo,
            accountNoChanged,
            accountNameValue ?? account.ACCOUNT_NAME,
            newTaxId,
            taxIdChanged,
            taxPayerType ?? account.TAX_PAYER_TYPE);

    [Fact]
    public void UpdateDetails_IdenticalDetailsOnAVerifiedAccount_ChangesNothingAndKeepsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account);

        Assert.Equal(PayoutAccountUpdate.NoChange, outcome);
        Assert.False(outcome.Changed);
        Assert.False(outcome.VerificationReset);
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
        Assert.Equal("enc-account-no", account.ACCOUNT_NO_ENCRYPTED);
        Assert.Equal("enc-tax-id", account.TAX_ID);
    }

    [Fact]
    public void UpdateDetails_WhenNothingChanged_IgnoresTheCiphertextArgumentsAndKeepsTheStoredOnes()
    {
        var account = NewAccount(verified: true);

        // A fresh encryption of the same plaintext differs byte for byte (random nonce); the caller says "unchanged" and the stored value must survive.
        var outcome = account.UpdateDetails(account.BANK_CODE, "fresh-ciphertext-of-same-number", false, account.ACCOUNT_NAME, "fresh-ciphertext-of-same-tax-id", false, account.TAX_PAYER_TYPE);

        Assert.False(outcome.Changed);
        Assert.Equal("enc-account-no", account.ACCOUNT_NO_ENCRYPTED);
        Assert.Equal("enc-tax-id", account.TAX_ID);
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_BankCodeChange_ReplacesItAndResetsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, bankCode: "SCB");

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: true), outcome);
        Assert.Equal("SCB", account.BANK_CODE);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_AccountNumberChange_ReplacesTheCiphertextAndResetsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, accountNoChanged: true, newAccountNo: " enc-other-number ");

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: true), outcome);
        Assert.Equal("enc-other-number", account.ACCOUNT_NO_ENCRYPTED);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_AccountHolderNameChange_ReplacesItAndResetsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, accountNameValue: "  สมหญิง ใจดี  ");

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: true), outcome);
        Assert.Equal("สมหญิง ใจดี", account.ACCOUNT_NAME);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_TaxIdChange_ReplacesItAndResetsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, taxIdChanged: true, newTaxId: "enc-other-tax-id");

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: true), outcome);
        Assert.Equal("enc-other-tax-id", account.TAX_ID);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_RemovingTheTaxId_StoresNoneAndResetsTheVerification(string? newTaxId)
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, taxIdChanged: true, newTaxId: newTaxId);

        Assert.True(outcome.VerificationReset);
        Assert.Null(account.TAX_ID);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_TaxPayerTypeChange_ReplacesItAndResetsTheVerification()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, taxPayerType: TaxPayerType.Corporate);

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: true), outcome);
        Assert.Equal(TaxPayerType.Corporate, account.TAX_PAYER_TYPE);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_ChangeOnAnAccountThatWasNeverVerified_IsChangedButHadNothingToReset()
    {
        var account = NewAccount(verified: false);

        var outcome = Resubmit(account, bankCode: "SCB");

        Assert.Equal(new PayoutAccountUpdate(Changed: true, VerificationReset: false), outcome);
        Assert.Equal("SCB", account.BANK_CODE);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_UnchangedResubmissionOfAnUnverifiedAccount_StaysUnverified()
    {
        var account = NewAccount(verified: false);

        var outcome = Resubmit(account);

        Assert.False(outcome.Changed);
        Assert.Null(account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_SurroundingWhitespaceOnTextDetails_IsNotAChange()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, bankCode: "  KBANK ", accountNameValue: " สมชาย สบายดี  ");

        Assert.False(outcome.Changed);
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_BankCodeCaseChange_IsAChangeBecauseTheComparisonIsOrdinal()
    {
        var account = NewAccount(verified: true);

        var outcome = Resubmit(account, bankCode: "kbank");

        Assert.True(outcome.VerificationReset);
        Assert.Equal("kbank", account.BANK_CODE);
    }

    [Fact]
    public void UpdateDetails_ChangeThenVerify_IsVerifiedAgainOnlyThroughVerify()
    {
        var account = NewAccount(verified: true);
        Resubmit(account, bankCode: "SCB");
        Assert.Null(account.VERIFIED_AT_UTC);

        account.Verify(new FixedClock(VerifiedAt.AddHours(1)));

        Assert.Equal(VerifiedAt.AddHours(1), account.VERIFIED_AT_UTC);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UpdateDetails_BlankBankCode_Throws(string? bankCode)
    {
        var account = NewAccount(verified: true);

        Assert.ThrowsAny<ArgumentException>(() => account.UpdateDetails(bankCode!, "x", false, "name", null, false, TaxPayerType.Individual));
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UpdateDetails_BlankAccountName_Throws(string? accountName)
    {
        var account = NewAccount(verified: true);

        Assert.ThrowsAny<ArgumentException>(() => account.UpdateDetails("KBANK", "x", false, accountName!, null, false, TaxPayerType.Individual));
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_ChangedAccountNumberWithBlankCiphertext_Throws_AndLeavesTheAccountUntouched()
    {
        var account = NewAccount(verified: true);

        Assert.ThrowsAny<ArgumentException>(() => account.UpdateDetails(account.BANK_CODE, " ", true, account.ACCOUNT_NAME, null, false, account.TAX_PAYER_TYPE));
        Assert.Equal("enc-account-no", account.ACCOUNT_NO_ENCRYPTED);
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }

    [Fact]
    public void UpdateDetails_UnknownTaxPayerType_Throws()
    {
        var account = NewAccount(verified: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => account.UpdateDetails(account.BANK_CODE, "x", false, account.ACCOUNT_NAME, null, false, (TaxPayerType)99));
        Assert.Equal(VerifiedAt, account.VERIFIED_AT_UTC);
    }
}
