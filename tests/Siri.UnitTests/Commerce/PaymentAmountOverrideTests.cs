using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PaymentAmountOverrideTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private static readonly FakeClock Clock = new(new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));

    private static PAYMENT_AMOUNT_OVERRIDE Entry(bool enabled, decimal amount) =>
        PAYMENT_AMOUNT_OVERRIDE.Create(enabled, amount, "smoke test", Guid.NewGuid(), Clock);

    // ---- Resolve: what Stripe is asked to charge ----

    [Fact]
    public void Resolve_NoOverrideEverConfigured_ReturnsOrderTotal() =>
        Assert.Equal(1890m, PAYMENT_AMOUNT_OVERRIDE.Resolve(null, 1890m));

    [Fact]
    public void Resolve_Disabled_ReturnsOrderTotal() =>
        Assert.Equal(1890m, PAYMENT_AMOUNT_OVERRIDE.Resolve(Entry(false, 20m), 1890m));

    [Fact]
    public void Resolve_EnabledBelowOrderTotal_ReturnsOverrideAmount() =>
        Assert.Equal(20m, PAYMENT_AMOUNT_OVERRIDE.Resolve(Entry(true, 20m), 1890m));

    [Fact]
    public void Resolve_EnabledAboveOrderTotal_NeverRaisesTheCharge() =>
        Assert.Equal(500m, PAYMENT_AMOUNT_OVERRIDE.Resolve(Entry(true, 1000m), 500m));

    [Fact]
    public void Resolve_EnabledEqualToOrderTotal_ReturnsOrderTotal() =>
        Assert.Equal(500m, PAYMENT_AMOUNT_OVERRIDE.Resolve(Entry(true, 500m), 500m));

    [Fact]
    public void Resolve_FreeOrder_StaysFree() =>
        Assert.Equal(0m, PAYMENT_AMOUNT_OVERRIDE.Resolve(Entry(true, 20m), 0m));

    // ---- Create: invariants ----

    [Fact]
    public void Create_ValidInput_RecordsEverythingAndTrimsReason()
    {
        var admin = Guid.NewGuid();
        var entry = PAYMENT_AMOUNT_OVERRIDE.Create(true, 20.50m, "  live smoke test  ", admin, Clock);

        Assert.NotEqual(Guid.Empty, entry.PAYMENT_AMOUNT_OVERRIDE_ID);
        Assert.True(entry.IS_ENABLED);
        Assert.Equal(20.50m, entry.OVERRIDE_AMOUNT);
        Assert.Equal("live smoke test", entry.REASON);
        Assert.Equal(admin, entry.CHANGED_BY_USER_ID);
        Assert.Equal(Clock.UtcNow, entry.CHANGED_AT_UTC);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2_000_000.01)]
    [InlineData(20.123)]
    public void Create_InvalidAmount_Throws(double amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PAYMENT_AMOUNT_OVERRIDE.Create(true, (decimal)amount, "reason", Guid.NewGuid(), Clock));

    [Fact]
    public void Create_MaxAmount_IsAccepted() =>
        Assert.Equal(PAYMENT_AMOUNT_OVERRIDE.MaxOverrideAmount, Entry(true, PAYMENT_AMOUNT_OVERRIDE.MaxOverrideAmount).OVERRIDE_AMOUNT);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankReason_Throws(string reason) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            PAYMENT_AMOUNT_OVERRIDE.Create(true, 20m, reason, Guid.NewGuid(), Clock));

    [Fact]
    public void Create_ReasonTooLong_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PAYMENT_AMOUNT_OVERRIDE.Create(true, 20m, new string('x', PAYMENT_AMOUNT_OVERRIDE.MaxReasonLength + 1), Guid.NewGuid(), Clock));

    [Fact]
    public void Create_EmptyAdminId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PAYMENT_AMOUNT_OVERRIDE.Create(true, 20m, "reason", Guid.Empty, Clock));

    // ---- PAYMENT.ORIGINAL_AMOUNT ----

    [Fact]
    public void PaymentCreate_WithOriginalAmountAboveAmount_MarksOverridden()
    {
        var payment = PAYMENT.Create(Guid.NewGuid(), PaymentMethod.PromptPay, "pi_ovr", 20m, Clock, originalAmount: 1890m);

        Assert.Equal(20m, payment.AMOUNT);
        Assert.Equal(1890m, payment.ORIGINAL_AMOUNT);
        Assert.True(payment.IsAmountOverridden);
    }

    [Fact]
    public void PaymentCreate_WithoutOriginalAmount_IsNotOverridden()
    {
        var payment = PAYMENT.Create(Guid.NewGuid(), PaymentMethod.PromptPay, "pi_plain", 1890m, Clock);

        Assert.Null(payment.ORIGINAL_AMOUNT);
        Assert.False(payment.IsAmountOverridden);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(10)]
    public void PaymentCreate_OriginalAmountNotAboveAmount_Throws(double original) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PAYMENT.Create(Guid.NewGuid(), PaymentMethod.PromptPay, "pi_bad", 20m, Clock, originalAmount: (decimal)original));

    // ---- Validator ----

    private static readonly SetPaymentAmountOverrideValidator Validator = new();

    [Fact]
    public void Validator_ValidCommand_Passes() =>
        Assert.True(Validator.Validate(new SetPaymentAmountOverrideCommand(true, 20m, "smoke test")).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2_000_001)]
    [InlineData(20.123)]
    public void Validator_BadAmount_FailsOnOverrideAmount(double amount)
    {
        var result = Validator.Validate(new SetPaymentAmountOverrideCommand(true, (decimal)amount, "smoke test"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetPaymentAmountOverrideCommand.OverrideAmount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Validator_ShortOrMissingReason_FailsOnReason(string reason)
    {
        var result = Validator.Validate(new SetPaymentAmountOverrideCommand(true, 20m, reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetPaymentAmountOverrideCommand.Reason));
    }

    [Fact]
    public void Validator_ReasonTooLong_FailsOnReason()
    {
        var result = Validator.Validate(new SetPaymentAmountOverrideCommand(true, 20m, new string('x', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetPaymentAmountOverrideCommand.Reason));
    }
}
