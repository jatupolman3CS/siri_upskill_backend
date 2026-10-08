using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Stripe;

namespace Siri.UnitTests.Payment;

/// <summary>
/// Real data only: with no (or placeholder) Stripe secret key, <see cref="StripePaymentMethod"/> must
/// refuse every operation with <c>payment.provider_not_configured</c> and never reach Stripe — the old
/// behavior was to call Stripe with a fabricated "sk_test_placeholder_key".
/// </summary>
public class StripePaymentMethodNotConfiguredTests
{
    private static StripePaymentMethod CreateUnconfigured(string secretKey, ThrowingStripeClient client) =>
        new(
            Options.Create(new StripeOptions { SecretKey = secretKey, PublishableKey = "pk_test_x", WebhookSecret = "whsec_x" }),
            NullLogger<StripePaymentMethod>.Instance,
            client);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CHANGE_ME_DEV_ONLY_sk_test_placeholder_key")]
    public async Task CreatePaymentIntentAsync_SecretKeyMissingOrPlaceholder_FailsWithoutCallingStripe(string secretKey)
    {
        var client = new ThrowingStripeClient();
        var provider = CreateUnconfigured(secretKey, client);

        var result = await provider.CreatePaymentIntentAsync(
            new CreatePaymentIntentRequest(Guid.NewGuid(), "ORD-1", 100m, CustomerEmail: "buyer@example.test"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task GetPaymentIntentAsync_NotConfigured_FailsWithoutCallingStripe()
    {
        var client = new ThrowingStripeClient();
        var provider = CreateUnconfigured("", client);

        var result = await provider.GetPaymentIntentAsync("pi_1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CancelPaymentIntentAsync_NotConfigured_FailsWithoutCallingStripe()
    {
        var client = new ThrowingStripeClient();
        var provider = CreateUnconfigured("", client);

        var result = await provider.CancelPaymentIntentAsync("pi_1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CreateRefundAsync_NotConfigured_FailsWithoutCallingStripe()
    {
        var client = new ThrowingStripeClient();
        var provider = CreateUnconfigured("", client);

        var result = await provider.CreateRefundAsync(
            new CreateRefundRequest("pi_1", 50m, "requested_by_customer"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task GetChargeFeeAsync_NotConfigured_ReturnsSuccessNullPerContract()
    {
        // IPaymentMethod.GetChargeFeeAsync's documented contract: never Failure (fee lookup is best-effort).
        var client = new ThrowingStripeClient();
        var provider = CreateUnconfigured("", client);

        var result = await provider.GetChargeFeeAsync("pi_1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public void Constructor_NoSecretKey_DoesNotThrow_SoTheHostStillBoots()
    {
        // new StripeClient("") would throw; the adapter must defer that to call time.
        var provider = new StripePaymentMethod(
            Options.Create(new StripeOptions()),
            NullLogger<StripePaymentMethod>.Instance);

        Assert.NotNull(provider);
    }

    private sealed class ThrowingStripeClient : IStripeClient
    {
        public int Calls { get; private set; }

        public string ApiBase => "https://api.stripe.com";
        public string ApiKey => "sk_test_should_never_be_used";
        public string ClientId => string.Empty;
        public string ConnectBase => "https://connect.stripe.com";
        public string FilesBase => "https://files.stripe.com";
        public string MeterEventsBase => "https://meter-events.stripe.com";

        public Task<T> RequestAsync<T>(HttpMethod method, string path, BaseOptions options, RequestOptions requestOptions, CancellationToken cancellationToken = default)
            where T : IStripeEntity
        {
            Calls++;
            throw new InvalidOperationException("Stripe must not be called when the provider is not configured.");
        }

        public Task<Stream> RequestStreamingAsync(HttpMethod method, string path, BaseOptions options, RequestOptions requestOptions, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Stripe must not be called when the provider is not configured.");
        }
    }
}
