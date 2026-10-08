using Siri.Modules.Payout;
using Siri.Modules.Payout.Infrastructure;

namespace Siri.UnitTests.Payout;

/// <summary>
/// Pure-logic tests for <see cref="PayoutOptionsGuard.EnsureRealPayerInfoConfigured"/> (task P6-04 / X-9) —
/// proves that tax certificates and production payouts cannot run with default dev placeholders or invalid tax IDs.
/// </summary>
public sealed class PayoutOptionsGuardTests
{
    private static PayoutOptions ValidOptions() => new()
    {
        PayerCompanyName = "SIRI UpSkill Co., Ltd.",
        PayerTaxId = "0105500000000",
        PayerAddress = "123 Sukhumvit Road, Bangkok 10110",
        EstimatedPaymentFeePercent = 3.30m,
        WithholdingTaxPercent = 3.00m,
        MinimumPayoutAmount = 500.00m,
        HoldDays = 14
    };

    [Fact]
    public void EnsureRealPayerInfoConfigured_ValidOptions_DoesNotThrow()
    {
        var exception = Record.Exception(() => PayoutOptionsGuard.EnsureRealPayerInfoConfigured(ValidOptions()));

        Assert.Null(exception);
    }

    [Fact]
    public void GetPayerInfoProblems_ValidOptions_ReturnsNoProblems()
    {
        Assert.Empty(PayoutOptionsGuard.GetPayerInfoProblems(ValidOptions()));
    }

    [Fact]
    public void GetPayerInfoProblems_AllPlaceholders_ReportsEachSettingWithoutThrowing()
    {
        var options = new PayoutOptions
        {
            PayerCompanyName = PayoutOptionsGuard.PlaceholderPayerCompanyName,
            PayerTaxId = PayoutOptionsGuard.PlaceholderPayerTaxId,
            PayerAddress = "CHANGE_ME",
        };

        var problems = PayoutOptionsGuard.GetPayerInfoProblems(options);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("Payout:PayerCompanyName", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Payout:PayerTaxId", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Payout:PayerAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureRealPayerInfoConfigured_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PayoutOptionsGuard.EnsureRealPayerInfoConfigured(null!));
    }

    [Fact]
    public void EnsureRealPayerInfoConfigured_PayerCompanyNameIsPlaceholder_ThrowsInvalidOperationException()
    {
        var options = ValidOptions();
        options.PayerCompanyName = PayoutOptionsGuard.PlaceholderPayerCompanyName;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayoutOptionsGuard.EnsureRealPayerInfoConfigured(options));

        Assert.Contains("Payout:PayerCompanyName", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CHANGE_ME_DEV_ONLY")]
    [InlineData("CHANGE_ME_ANYTHING")]
    public void EnsureRealPayerInfoConfigured_InvalidPayerCompanyName_ThrowsInvalidOperationException(string invalidName)
    {
        var options = ValidOptions();
        options.PayerCompanyName = invalidName;

        Assert.Throws<InvalidOperationException>(() =>
            PayoutOptionsGuard.EnsureRealPayerInfoConfigured(options));
    }

    [Fact]
    public void EnsureRealPayerInfoConfigured_PayerTaxIdIsPlaceholder_ThrowsInvalidOperationException()
    {
        var options = ValidOptions();
        options.PayerTaxId = PayoutOptionsGuard.PlaceholderPayerTaxId;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayoutOptionsGuard.EnsureRealPayerInfoConfigured(options));

        Assert.Contains("Payout:PayerTaxId", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000000000000")]
    [InlineData("12345")] // too short
    [InlineData("01055000000001")] // too long (14 digits)
    [InlineData("010550000000A")] // non-digit
    public void EnsureRealPayerInfoConfigured_InvalidPayerTaxId_ThrowsInvalidOperationException(string invalidTaxId)
    {
        var options = ValidOptions();
        options.PayerTaxId = invalidTaxId;

        Assert.Throws<InvalidOperationException>(() =>
            PayoutOptionsGuard.EnsureRealPayerInfoConfigured(options));
    }
}
