using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Siri.Api.Configuration;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Catalog;
using Siri.Modules.Commerce;
using Siri.Modules.Learning;
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
    public void IsRunningInContainer_WhenFlagSet_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            })
            .Build();

        Assert.True(ProductionConfigurationGuard.IsRunningInContainer(config));
    }

    [Fact]
    public void IsRunningInContainer_WhenFlagAbsent_ReturnsFalse()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.False(ProductionConfigurationGuard.IsRunningInContainer(config));
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
        // Real-data-only checks: nothing Email/Bunny/webhook-related was configured at all.
        Assert.Contains("Email:Provider", ex.Message);
        Assert.Contains("VideoProvider:LibraryId", ex.Message);
        Assert.Contains($"{StripeOptions.SectionName}:WebhookSecret", ex.Message);
        Assert.Contains($"{StripeOptions.SectionName}:PublishableKey", ex.Message);
    }

    /// <summary>A fully, genuinely configured Production host (real-looking values only). Tests below
    /// start from this and break exactly one thing, so each failure is attributable to that one check.</summary>
    private static Dictionary<string, string?> ValidProductionSettings() => new()
    {
        // 32 bytes base64 encoded
        ["DataProtection:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
        ["Identity:Jwt:SigningKey"] = "ProductionSecureJwtSigningKey_2026_SiriUpSkill_Secret_Value!",
        [$"{StripeOptions.SectionName}:SecretKey"] = "sk_live_REDACTED",
        [$"{StripeOptions.SectionName}:PublishableKey"] = "pk_live_REDACTED",
        [$"{StripeOptions.SectionName}:WebhookSecret"] = "whsec_xxxxxxxxxxxxxxxxxxxxxx",
        ["Cors:AllowedOrigins:0"] = "https://siriupskill.com",
        ["Cors:AllowedOrigins:1"] = "https://admin.siriupskill.com",
        ["Seo:PublicBaseUrl"] = "https://siriupskill.com",
        [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "SIRI UpSkill Co., Ltd.",
        [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105500000000",
        [$"{PayoutOptions.SectionName}:PayerAddress"] = "123 Siri Tower, Sukhumvit Rd, Bangkok",
        ["Email:Provider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.siriupskill.com",
        ["Email:Smtp:FromAddress"] = "no-reply@siriupskill.com",
        ["VideoProvider:LibraryId"] = "123456",
        ["VideoProvider:ApiKey"] = "a1b2c3d4-real-api-key",
        ["VideoProvider:ReadOnlyApiKey"] = "e5f6a7b8-real-readonly-key",
        ["VideoProvider:PullZone"] = "siriupskill",
        ["VideoProvider:CdnHostname"] = "vz-abc123.b-cdn.net",
        ["VideoProvider:TokenAuthenticationKey"] = "c9d8e7f6-real-token-key",
    };

    private static InvalidOperationException ValidateProductionThrows(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production")));
    }

    [Fact]
    public void ValidateProductionConfiguration_WithValidProductionSettings_Passes()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(ValidProductionSettings()).Build();

        // Should not throw
        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production"));
    }

    [Theory]
    [InlineData("Log")]
    [InlineData("log")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SendGrid")]
    public void ValidateProductionConfiguration_EmailProviderNotSmtp_Throws(string? provider)
    {
        var settings = ValidProductionSettings();
        settings["Email:Provider"] = provider;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Email:Provider must be 'Smtp'", ex.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_SmtpWithoutHost_Throws()
    {
        var settings = ValidProductionSettings();
        settings["Email:Smtp:Host"] = "";

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Email:Smtp:Host", ex.Message);
    }

    [Theory]
    [InlineData("no-reply@example.com")]
    [InlineData("no-reply@siriupskill.test")]
    [InlineData("SIRI UpSkill <no-reply@example.org>")]
    [InlineData("")]
    public void ValidateProductionConfiguration_SmtpFromAddressFakeOrMissing_Throws(string from)
    {
        var settings = ValidProductionSettings();
        settings["Email:Smtp:FromAddress"] = from;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Email:Smtp:FromAddress", ex.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_SmtpAllowInsecure_Throws()
    {
        var settings = ValidProductionSettings();
        settings["Email:Smtp:AllowInsecure"] = "true";

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Email:Smtp:AllowInsecure", ex.Message);
    }

    [Theory]
    [InlineData("VideoProvider:LibraryId", "")]
    [InlineData("VideoProvider:LibraryId", "000000")]
    [InlineData("VideoProvider:ApiKey", "")]
    [InlineData("VideoProvider:ApiKey", "CHANGE_ME_DEV_ONLY_bunny_api_key")]
    [InlineData("VideoProvider:ReadOnlyApiKey", "CHANGE_ME_DEV_ONLY_bunny_readonly_api_key")]
    [InlineData("VideoProvider:CdnHostname", "")]
    [InlineData("VideoProvider:CdnHostname", "CHANGE_ME_DEV_ONLY_cdn")]
    [InlineData("VideoProvider:TokenAuthenticationKey", "")]
    [InlineData("VideoProvider:TokenAuthenticationKey", "token-placeholder")]
    public void ValidateProductionConfiguration_VideoSettingMissingOrPlaceholder_ThrowsNamingTheSetting(string key, string value)
    {
        var settings = ValidProductionSettings();
        settings[key] = value;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Bunny Stream settings", ex.Message);
        Assert.Contains(key, ex.Message);
        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, ex.Message); // names only — never echo configured values
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHANGE_ME_DEV_ONLY_whsec_placeholder_key")]
    public void ValidateProductionConfiguration_StripeWebhookSecretMissingOrPlaceholder_Throws(string secret)
    {
        var settings = ValidProductionSettings();
        settings[$"{StripeOptions.SectionName}:WebhookSecret"] = secret;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains($"{StripeOptions.SectionName}:WebhookSecret", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pk_test_REDACTED")]
    [InlineData("CHANGE_ME_DEV_ONLY_pk_test_placeholder_key")]
    public void ValidateProductionConfiguration_StripePublishableKeyNotLive_Throws(string key)
    {
        var settings = ValidProductionSettings();
        settings[$"{StripeOptions.SectionName}:PublishableKey"] = key;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains($"{StripeOptions.SectionName}:PublishableKey", ex.Message);
    }

    [Theory]
    [InlineData("CompanyName", "")]
    [InlineData("CompanyName", "CHANGE_ME_DEV_ONLY")]
    [InlineData("TaxId", "")]
    [InlineData("TaxId", "12345")]
    [InlineData("TaxId", "01055000000AB")]
    [InlineData("Address", "")]
    [InlineData("Address", "CHANGE_ME")]
    public void ValidateProductionConfiguration_ReceiptSellerMissingOrPlaceholder_ThrowsNamingTheSetting(string field, string value)
    {
        // An explicit Commerce:Seller value that is bad AND no usable payout payer value to fall back to.
        var settings = ValidProductionSettings();
        settings[$"{ReceiptSellerOptions.SectionName}:{field}"] = value;
        settings[$"{PayoutOptions.SectionName}:Payer{field}"] = value;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains("Receipt seller identity", ex.Message);
        Assert.Contains($"{ReceiptSellerOptions.SectionName}:{field}", ex.Message);
        if (value.Length > 4 && !value.Contains("CHANGE_ME", StringComparison.Ordinal)) // the CHANGE_ME marker itself is a public constant, not a configured value
        {
            Assert.DoesNotContain(value, ex.Message); // names only — never echo configured values
        }
    }

    [Fact]
    public void ValidateProductionConfiguration_SellerUnsetFallsBackToThePayoutPayerIdentity_Passes()
    {
        // ValidProductionSettings() sets only the Payout payer identity (the same legal entity).
        var settings = ValidProductionSettings();
        Assert.False(settings.ContainsKey($"{ReceiptSellerOptions.SectionName}:CompanyName"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production"));
    }

    [Fact]
    public void ReceiptSellerOptions_ExplicitSellerValuesWinOverThePayoutPayerFallback()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ReceiptSellerOptions.SectionName}:CompanyName"] = "Seller Co., Ltd.",
            [$"{ReceiptSellerOptions.SectionName}:TaxId"] = "0105511111111",
            [$"{ReceiptSellerOptions.SectionName}:Address"] = "1 Seller Road",
            [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "Payer Co., Ltd.",
            [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105522222222",
            [$"{PayoutOptions.SectionName}:PayerAddress"] = "2 Payer Road",
        }).Build();

        var seller = ReceiptSellerOptions.Resolve(config);

        Assert.Equal("Seller Co., Ltd.", seller.CompanyName);
        Assert.Equal("0105511111111", seller.TaxId);
        Assert.Equal("1 Seller Road", seller.Address);
        Assert.Empty(seller.GetMissingSettings());
    }

    [Fact]
    public void ReceiptSellerOptions_UnsetSellerValuesFallBackToThePayoutPayerIdentity()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "Payer Co., Ltd.",
            [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105522222222",
            [$"{PayoutOptions.SectionName}:PayerAddress"] = "2 Payer Road",
        }).Build();

        var seller = ReceiptSellerOptions.Resolve(config);

        Assert.Equal("Payer Co., Ltd.", seller.CompanyName);
        Assert.Equal("0105522222222", seller.TaxId);
        Assert.Equal("2 Payer Road", seller.Address);
        Assert.Empty(seller.GetMissingSettings());
    }

    [Fact]
    public void ReceiptSellerOptions_PlaceholderPayerIdentity_IsNeverUsedAsTheSeller()
    {
        // appsettings.json ships the payout payer as CHANGE_ME_DEV_ONLY / 0000000000000 — those are not a company.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{PayoutOptions.SectionName}:PayerCompanyName"] = "CHANGE_ME_DEV_ONLY",
            [$"{PayoutOptions.SectionName}:PayerTaxId"] = "0000000000000",
            [$"{PayoutOptions.SectionName}:PayerAddress"] = "CHANGE_ME",
        }).Build();

        var seller = ReceiptSellerOptions.Resolve(config);

        Assert.Equal(3, seller.GetMissingSettings().Count);
        Assert.DoesNotContain("CHANGE_ME", seller.CompanyName + seller.Address);
    }

    [Theory]
    [InlineData("http://siriupskill.com")]
    [InlineData("siriupskill.com")]
    [InlineData("ftp://siriupskill.com")]
    public void ValidateProductionConfiguration_ExplicitCertificatePublicBaseUrlNotHttps_Throws(string url)
    {
        var settings = ValidProductionSettings();
        settings[$"{CertificateOptions.SectionName}:PublicBaseUrl"] = url;

        var ex = ValidateProductionThrows(settings);

        Assert.Contains($"{CertificateOptions.SectionName}:PublicBaseUrl", ex.Message);
    }

    [Fact]
    public void ValidateProductionConfiguration_CertificateUrlUnsetFallsBackToSiteWideSeoOrigin_Passes()
    {
        // ValidProductionSettings() sets only Seo:PublicBaseUrl (https) — certificates reuse it.
        var settings = ValidProductionSettings();
        Assert.False(settings.ContainsKey($"{CertificateOptions.SectionName}:PublicBaseUrl"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production"));
    }

    [Fact]
    public void ValidateProductionConfiguration_NoPublicOriginAnywhere_ThrowsNamingTheCertificateSetting()
    {
        var settings = ValidProductionSettings();
        settings["Seo:PublicBaseUrl"] = "";

        var ex = ValidateProductionThrows(settings);

        Assert.Contains($"{CertificateOptions.SectionName}:PublicBaseUrl", ex.Message);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("disabled")]
    [InlineData(" Disabled ")]
    public void ValidateProductionConfiguration_AttachmentVirusScanDisabled_Passes(string mode)
    {
        // Owner decision 2026-10-10 (docs/DECISIONS.md Q9): the scan step is skipped for now, so production may run
        // with the explicit, logged opt-in. Before this date the guard refused to boot with it.
        var settings = ValidProductionSettings();
        settings[$"{AttachmentVirusScanOptions.SectionName}:Mode"] = mode;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Required")]
    public void ValidateProductionConfiguration_AttachmentVirusScanRequiredOrUnset_Passes(string? mode)
    {
        var settings = ValidProductionSettings();
        settings[$"{AttachmentVirusScanOptions.SectionName}:Mode"] = mode;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Production"));
    }

    [Fact]
    public void ValidateProductionConfiguration_OutsideProduction_EmailLogAndEmptyVideoAreAllowed()
    {
        // Development/QA may run without Bunny/Stripe/SMTP (the features then answer 503) — only
        // Production is fail-fast.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "Log" })
            .Build();

        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("Development"));
        ProductionConfigurationGuard.ValidateProductionConfiguration(config, new FakeHostEnvironment("QA"));
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
        var settings = ValidProductionSettings();
        settings["Identity:Jwt:SigningKey"] = "wX8hR9mK2pL5vN8qY1tU4xZ7aB0cD3eF4gH6jI9kL1nO4pQ7sT0uV3wX6yZ9aB2c";
        settings[$"{StripeOptions.SectionName}:WebhookSecret"] = "whsec_suyLpPcKhIGcLh8w2WRsCpFd8x7FnST4";
        settings["Cors:AllowedOrigins:0"] = "https://siriupskill.siristudiophoto.com";
        settings["Cors:AllowedOrigins:1"] = "https://admin.siriupskill.siristudiophoto.com";
        settings["Seo:PublicBaseUrl"] = "https://siriupskill.siristudiophoto.com";
        settings[$"{PayoutOptions.SectionName}:PayerCompanyName"] = "บริษัท สิริ อัพสกิล จำกัด";
        settings[$"{PayoutOptions.SectionName}:PayerTaxId"] = "0105550000000";
        settings[$"{PayoutOptions.SectionName}:PayerAddress"] = "Bangkok, Thailand";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

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
