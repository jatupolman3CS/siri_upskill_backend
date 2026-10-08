using Siri.Integrations.Payment.Stripe;

namespace Siri.UnitTests.Payment;

public class StripeOptionsTests
{
    [Fact]
    public void Defaults_AreEmpty_AndNothingIsConfigured()
    {
        // Real data only: no fake default keys. An unconfigured host still boots (nothing is
        // [Required]) but every Stripe feature reports "not configured".
        var options = new StripeOptions();

        Assert.Equal(string.Empty, options.SecretKey);
        Assert.Equal(string.Empty, options.PublishableKey);
        Assert.Equal(string.Empty, options.WebhookSecret);
        Assert.False(options.HasSecretKey);
        Assert.False(options.HasPublishableKey);
        Assert.False(options.HasWebhookSecret);
    }

    [Fact]
    public void ValidKeys_AreAllConfigured()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_12345",
            PublishableKey = "pk_test_12345",
            WebhookSecret = "whsec_12345",
        };

        Assert.True(options.HasSecretKey);
        Assert.True(options.HasPublishableKey);
        Assert.True(options.HasWebhookSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("CHANGE_ME_DEV_ONLY_sk_test_placeholder_key")]
    [InlineData("change_me_whsec")]
    public void IsConfigured_EmptyOrChangeMeMarker_IsFalse(string? value)
    {
        Assert.False(StripeOptions.IsConfigured(value));
    }

    [Theory]
    [InlineData("sk_test_12345")]
    [InlineData("sk_live_abc")]
    [InlineData("whsec_abc")]
    // Test-fixture style values used by integration tests must stay "configured" — only the committed
    // CHANGE_ME markers are rejected, not any value that merely mentions "placeholder".
    [InlineData("sk_test_placeholder_key_for_testing_purposes_only")]
    public void IsConfigured_RealLookingValue_IsTrue(string value)
    {
        Assert.True(StripeOptions.IsConfigured(value));
    }

    [Fact]
    public void WebhookSecret_IsOptional_AtStartup()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_12345",
            PublishableKey = "pk_test_12345",
        };

        Assert.True(options.HasSecretKey);
        Assert.False(options.HasWebhookSecret);
        Assert.Equal(string.Empty, options.WebhookSecret);
    }

    [Fact]
    public void SectionName_IsPaymentStripe()
    {
        Assert.Equal("Payment:Stripe", StripeOptions.SectionName);
    }
}
