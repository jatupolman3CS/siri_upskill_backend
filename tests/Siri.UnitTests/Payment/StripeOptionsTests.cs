using System.ComponentModel.DataAnnotations;
using Siri.Integrations.Payment.Stripe;

namespace Siri.UnitTests.Payment;

public class StripeOptionsTests
{
    private static List<ValidationResult> Validate(StripeOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_FailValidation_RequiredKeysMissing()
    {
        var options = new StripeOptions();
        var errors = Validate(options);

        Assert.True(errors.Count >= 2, $"Expected at least 2 validation errors but got {errors.Count}");
    }

    [Fact]
    public void ValidKeys_PassesValidation()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_12345",
            PublishableKey = "pk_test_12345",
            WebhookSecret = "whsec_12345",
        };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void WebhookSecret_IsOptional_AtStartup()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_12345",
            PublishableKey = "pk_test_12345",
        };

        Assert.Empty(Validate(options));
        Assert.Equal(string.Empty, options.WebhookSecret);
    }

    [Fact]
    public void SectionName_IsPaymentStripe()
    {
        Assert.Equal("Payment:Stripe", StripeOptions.SectionName);
    }
}
