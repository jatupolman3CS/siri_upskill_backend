using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Stripe;

namespace Siri.UnitTests.Payment;

/// <summary>
/// P11-09: Card checkout branch of <see cref="StripePaymentMethod.CreatePaymentIntentAsync"/> +
/// <see cref="StripePaymentMethod.GetChargeFeeAsync"/>. Kept in a separate file from
/// <see cref="StripePaymentMethodTests"/> deliberately — that file must compile and pass with ZERO
/// changes (the contract's regression guarantee for existing 3-arg <c>CreatePaymentIntentRequest</c>
/// call sites still compiling against the new trailing <c>Method</c> param).
/// </summary>
public class StripePaymentMethodCardTests
{
    private static StripeOptions CreateOptions() => new()
    {
        SecretKey = "sk_test_fake123",
        PublishableKey = "pk_test_fake123",
        WebhookSecret = "whsec_fake123",
    };

    [Fact]
    public async Task CreatePaymentIntentAsync_PromptPay_BuildsOptionsUnchangedFromPreP11_09Shape()
    {
        var client = new CapturingStripeClient();
        var provider = new StripePaymentMethod(Options.Create(CreateOptions()), NullLogger<StripePaymentMethod>.Instance, client);

        var request = new CreatePaymentIntentRequest(
            Guid.NewGuid(), "ORD-2026-0001", 1500m, "thb", CustomerEmail: "buyer@example.test");
        // Method omitted — must default to PromptPay, exactly as it did before this request record grew a Method field.

        var result = await provider.CreatePaymentIntentAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var options = Assert.IsType<PaymentIntentCreateOptions>(client.LastCreateOptions);
        Assert.True(options.Confirm);
        Assert.Equal(["promptpay"], options.PaymentMethodTypes);
        Assert.NotNull(options.PaymentMethodData);
        Assert.Equal("promptpay", options.PaymentMethodData.Type);
        Assert.Equal("buyer@example.test", options.PaymentMethodData.BillingDetails.Email);
        Assert.Equal("buyer@example.test", options.ReceiptEmail);
        Assert.Equal(150000, options.Amount);
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_Card_ConfirmFalseAndNoPaymentMethodData()
    {
        var client = new CapturingStripeClient();
        var provider = new StripePaymentMethod(Options.Create(CreateOptions()), NullLogger<StripePaymentMethod>.Instance, client);

        var request = new CreatePaymentIntentRequest(
            Guid.NewGuid(), "ORD-2026-0002", 1500m, "thb", CustomerEmail: "buyer@example.test", Method: PaymentMethodType.Card);

        var result = await provider.CreatePaymentIntentAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var options = Assert.IsType<PaymentIntentCreateOptions>(client.LastCreateOptions);
        Assert.False(options.Confirm);
        Assert.Equal(["card"], options.PaymentMethodTypes);
        Assert.Null(options.PaymentMethodData);
        Assert.Equal("buyer@example.test", options.ReceiptEmail);
    }

    [Fact]
    public async Task GetChargeFeeAsync_FeeAvailable_ReturnsFeeConvertedFromSatang()
    {
        var client = new CapturingStripeClient { FeeInSatangToReturn = 3000 };
        var provider = new StripePaymentMethod(Options.Create(CreateOptions()), NullLogger<StripePaymentMethod>.Instance, client);

        var result = await provider.GetChargeFeeAsync("pi_test_123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30.00m, result.Value);
        var options = Assert.IsType<PaymentIntentGetOptions>(client.LastGetOptions);
        Assert.Equal(["latest_charge.balance_transaction"], options.Expand);
    }

    [Fact]
    public async Task GetChargeFeeAsync_FeeNotYetAvailable_ReturnsNullNotFailure()
    {
        var client = new CapturingStripeClient { FeeInSatangToReturn = null };
        var provider = new StripePaymentMethod(Options.Create(CreateOptions()), NullLogger<StripePaymentMethod>.Instance, client);

        var result = await provider.GetChargeFeeAsync("pi_test_123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetChargeFeeAsync_StripeApiThrows_ReturnsSuccessWithNullNeverFailure()
    {
        var client = new CapturingStripeClient { ThrowOnGet = true };
        var provider = new StripePaymentMethod(Options.Create(CreateOptions()), NullLogger<StripePaymentMethod>.Instance, client);

        var result = await provider.GetChargeFeeAsync("pi_test_123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /// <summary>Fake <see cref="IStripeClient"/> that captures the <see cref="BaseOptions"/> sent for
    /// PaymentIntent create/get calls instead of making a real HTTP request — Stripe.net's services
    /// (<see cref="PaymentIntentService"/>) are built around this seam.</summary>
    private sealed class CapturingStripeClient : IStripeClient
    {
        public BaseOptions? LastCreateOptions { get; private set; }
        public BaseOptions? LastGetOptions { get; private set; }
        public long? FeeInSatangToReturn { get; set; }
        public bool ThrowOnGet { get; set; }

        public string ApiBase => "https://api.stripe.com";
        public string ApiKey => "sk_test_fake";
        public string ClientId => string.Empty;
        public string ConnectBase => "https://connect.stripe.com";
        public string FilesBase => "https://files.stripe.com";
        public string MeterEventsBase => "https://meter-events.stripe.com";

        public Task<T> RequestAsync<T>(HttpMethod method, string path, BaseOptions options, RequestOptions requestOptions, CancellationToken cancellationToken = default)
            where T : IStripeEntity
        {
            if (options is PaymentIntentGetOptions)
            {
                LastGetOptions = options;
                if (ThrowOnGet)
                {
                    throw new StripeException("simulated transient Stripe API failure");
                }

                var intent = new PaymentIntent
                {
                    Id = "pi_test_123",
                    ClientSecret = "pi_test_123_secret",
                    Status = "succeeded",
                    Amount = 150000,
                    Currency = "thb",
                    LatestCharge = FeeInSatangToReturn is { } fee
                        ? new Charge { BalanceTransaction = new BalanceTransaction { Fee = fee } }
                        : null,
                };
                return Task.FromResult((T)(object)intent);
            }

            LastCreateOptions = options;
            var created = new PaymentIntent
            {
                Id = "pi_test_123",
                ClientSecret = "pi_test_123_secret",
                Status = "requires_confirmation",
                Amount = ((PaymentIntentCreateOptions)options).Amount ?? 0,
                Currency = "thb",
            };
            return Task.FromResult((T)(object)created);
        }

        public Task<Stream> RequestStreamingAsync(HttpMethod method, string path, BaseOptions options, RequestOptions requestOptions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
