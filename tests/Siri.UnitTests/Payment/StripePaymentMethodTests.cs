using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;

namespace Siri.UnitTests.Payment;

public class StripePaymentMethodTests
{
    private static StripeOptions CreateOptions() => new()
    {
        SecretKey = "sk_test_fake123",
        PublishableKey = "pk_test_fake123",
        WebhookSecret = "whsec_fake123",
    };

    [Fact]
    public async Task CreatePaymentIntentAsync_NegativeOrZeroAmount_ReturnsValidationError()
    {
        var provider = new StripePaymentMethod(
            Options.Create(CreateOptions()),
            NullLogger<StripePaymentMethod>.Instance);

        var request = new CreatePaymentIntentRequest(
            Guid.NewGuid(),
            "ORD-2026-0001",
            0m);

        var result = await provider.CreatePaymentIntentAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task GetPaymentIntentAsync_EmptyId_ThrowsArgumentException()
    {
        var provider = new StripePaymentMethod(
            Options.Create(CreateOptions()),
            NullLogger<StripePaymentMethod>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetPaymentIntentAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task CancelPaymentIntentAsync_EmptyId_ThrowsArgumentException()
    {
        var provider = new StripePaymentMethod(
            Options.Create(CreateOptions()),
            NullLogger<StripePaymentMethod>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.CancelPaymentIntentAsync("", CancellationToken.None));
    }
}
