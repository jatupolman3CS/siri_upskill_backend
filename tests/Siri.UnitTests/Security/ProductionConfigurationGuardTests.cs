using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Siri.Api.Configuration;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Payout;

namespace Siri.UnitTests.Security;

public sealed class ProductionConfigurationGuardTests
{
    [Theory]
    [InlineData("wb8jlEpV/0t7ptEyiSdtRQOh5MXY4lde5L5WYxNfyIM=")]
    [InlineData(" wb8jlEpV/0t7ptEyiSdtRQOh5MXY4lde5L5WYxNfyIM=\n")]
    public void ValidateProductionConfiguration_WithShippedDevelopmentEncryptionKey_Throws(string encryptionKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = encryptionKey,
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(configuration, new FakeHostEnvironment("Production")));

        Assert.Contains("DataProtection:EncryptionKeyBase64", exception.Message);
        Assert.Contains("shipped development key", exception.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_WhenNotProduction_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = "CHANGE_ME_IN_PRODUCTION_BASE64_32_BYTES_KEY==",
            })
            .Build();

        var env = new FakeHostEnvironment("Development");

        // Should not throw in Development
        ProductionConfigurationGuard.ValidateProductionConfiguration(config, env);
    }

    [Fact]
    public void ValidateDeploymentConfiguration_WhenContainerUsesNativeDevDatabase_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=5433;Database=SIRIUPSKILL;Username=siriupskill_dev;Password=secret",
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateDeploymentConfiguration(config, new FakeHostEnvironment("QA")));

        Assert.Contains("native Windows development database", ex.Message);
        Assert.Contains("ConnectionStrings__Default", ex.Message);
    }

    [Fact]
    public void ValidateDeploymentConfiguration_WhenLocalDevelopmentUsesNativeDevDatabase_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=5433;Database=SIRIUPSKILL;Username=siriupskill_dev;Password=secret",
            })
            .Build();

        ProductionConfigurationGuard.ValidateDeploymentConfiguration(config, new FakeHostEnvironment("Development"));
    }

    [Fact]
    public void ValidateDeploymentConfiguration_WhenQaUsesServiceHost_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
                ["ConnectionStrings:Default"] = "Host=postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=secret",
            })
            .Build();

        ProductionConfigurationGuard.ValidateDeploymentConfiguration(config, new FakeHostEnvironment("QA"));
    }

    [Fact]
    public void ValidateProductionConfiguration_WithDefaultPlaceholder_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = "CHANGE_ME_IN_PRODUCTION_BASE64_32_BYTES_KEY==",
                ["Identity:Jwt:SigningKey"] = "CHANGE_ME_SECRET_KEY_12345678901234567890",
                [$"{StripeOptions.SectionName}:SecretKey"] = "CHANGE_ME_DEV_ONLY_sk_test_REDACTED",
                ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
                ["Seo:PublicBaseUrl"] = "http://dev.siriupskill.com",
                [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "CHANGE_ME_DEV_ONLY",
                [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0000000000000",
            })
            .Build();

        var env = new FakeHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(config, env));

        Assert.Contains("DataProtection:EncryptionKeyBase64", ex.Message);
        Assert.Contains("Identity:Jwt:SigningKey", ex.Message);
        Assert.Contains($"{StripeOptions.SectionName}:SecretKey", ex.Message);
        Assert.Contains("Cors:AllowedOrigins", ex.Message);
        Assert.Contains("Seo:PublicBaseUrl", ex.Message);
        Assert.Contains($"{PayoutOptions.SectionName}:PayerCompanyName", ex.Message);
        Assert.Contains($"{PayoutOptions.SectionName}:PayerTaxId", ex.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_WithValidProductionSettings_Passes()
    {
        // 32 bytes base64 encoded
        var validKeyBase64 = Convert.ToBase64String(new byte[32]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = validKeyBase64,
                ["Identity:Jwt:SigningKey"] = "ProductionSecureJwtSigningKey_2026_SiriUpSkill_Secret_Value!",
                [$"{StripeOptions.SectionName}:SecretKey"] = "sk_test_REDACTED",
                [$"{StripeOptions.SectionName}:WebhookSecret"] = "whsec_xxxxxxxxxxxxxxxxxxxxxx",
                ["Cors:AllowedOrigins:0"] = "https://siriupskill.com",
                ["Cors:AllowedOrigins:1"] = "https://admin.siriupskill.com",
                ["Seo:PublicBaseUrl"] = "https://siriupskill.com",
                [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "SIRI UpSkill Co., Ltd.",
                [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105500000000",
                [$"{PayoutOptions.SectionName}:PayerAddress"] = "123 Siri Tower, Sukhumvit Rd, Bangkok",
            })
            .Build();

        var env = new FakeHostEnvironment("Production");

        // Should not throw
        ProductionConfigurationGuard.ValidateProductionConfiguration(config, env);
    }

    [Fact]
    public void ValidateProductionConfiguration_WithNonLiveStripeSecretKey_Throws()
    {
        var validKeyBase64 = Convert.ToBase64String(new byte[32]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = validKeyBase64,
                ["Identity:Jwt:SigningKey"] = "ProductionSecureJwtSigningKey_2026_SiriUpSkill_Secret_Value!",
                [$"{StripeOptions.SectionName}:SecretKey"] = "sk_test_REDACTED", // test key not allowed in production
                [$"{StripeOptions.SectionName}:WebhookSecret"] = "whsec_xxxxxxxxxxxxxxxxxxxxxx",
                ["Cors:AllowedOrigins:0"] = "https://siriupskill.com",
                ["Seo:PublicBaseUrl"] = "https://siriupskill.com",
                [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "SIRI UpSkill Co., Ltd.",
                [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105500000000",
                [$"{PayoutOptions.SectionName}:PayerAddress"] = "123 Siri Tower, Sukhumvit Rd, Bangkok",
            })
            .Build();

        var env = new FakeHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(config, env));

        Assert.Contains($"{StripeOptions.SectionName}:SecretKey", ex.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_WithThaiCompanyPayerInfo_Passes()
    {
        var validKeyBase64 = Convert.ToBase64String(new byte[32]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:EncryptionKeyBase64"] = validKeyBase64,
                ["Identity:Jwt:SigningKey"] = "wX8hR9mK2pL5vN8qY1tU4xZ7aB0cD3eF4gH6jI9kL1nO4pQ7sT0uV3wX6yZ9aB2c",
                [$"{StripeOptions.SectionName}:SecretKey"] = "sk_test_REDACTED",
                [$"{StripeOptions.SectionName}:WebhookSecret"] = "whsec_suyLpPcKhIGcLh8w2WRsCpFd8x7FnST4",
                ["Cors:AllowedOrigins:0"] = "https://siriupskill.siristudiophoto.com",
                ["Cors:AllowedOrigins:1"] = "https://admin.siriupskill.siristudiophoto.com",
                ["Seo:PublicBaseUrl"] = "https://siriupskill.siristudiophoto.com",
                [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "บริษัท สิริ อัพสกิล จำกัด",
                [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105550000000",
                [$"{PayoutOptions.SectionName}:PayerAddress"] = "Bangkok, Thailand",
            })
            .Build();

        var env = new FakeHostEnvironment("Production");

        // Should not throw
        ProductionConfigurationGuard.ValidateProductionConfiguration(config, env);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Siri.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
