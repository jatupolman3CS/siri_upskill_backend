using Siri.Integrations.Storage;
using Xunit;

namespace Siri.UnitTests.Storage;

public sealed class R2StorageOptionsTests
{
    [Fact]
    public void DefaultOptions_AreNotConfigured_AndNameEveryMissingSettingButNeverAValue()
    {
        var options = new R2StorageOptions();

        Assert.False(options.IsConfigured);
        Assert.Equal(
            ["Storage:R2:AccountId", "Storage:R2:AccessKeyId", "Storage:R2:SecretAccessKey", "Storage:R2:BucketName"],
            options.GetMissingSettings());
    }

    [Fact]
    public void FullyPopulatedOptions_AreConfigured()
    {
        var options = new R2StorageOptions { AccountId = "abc", AccessKeyId = "id", SecretAccessKey = "secret", BucketName = "bucket" };

        Assert.True(options.IsConfigured);
        Assert.Empty(options.GetMissingSettings());
    }

    [Theory]
    [InlineData("CHANGE_ME")]
    [InlineData("change_me_please")]
    [InlineData("a-placeholder-value")]
    [InlineData("  ")]
    public void PlaceholderValues_CountAsMissing(string value)
    {
        var options = new R2StorageOptions { AccountId = "abc", AccessKeyId = "id", SecretAccessKey = value, BucketName = "bucket" };

        Assert.False(options.IsConfigured);
        Assert.Equal(["Storage:R2:SecretAccessKey"], options.GetMissingSettings());
    }

    [Fact]
    public void ServiceUrl_DefaultsToTheAccountsGlobalR2Endpoint()
    {
        var options = new R2StorageOptions { AccountId = " abc123 " };

        Assert.Equal("https://abc123.r2.cloudflarestorage.com", options.ResolveServiceUrl());
    }

    [Fact]
    public void ServiceUrl_UsesTheOverrideWhenSet_AndThenAccountIdIsNotRequired()
    {
        var options = new R2StorageOptions
        {
            Endpoint = "https://abc123.eu.r2.cloudflarestorage.com/",
            AccessKeyId = "id",
            SecretAccessKey = "secret",
            BucketName = "bucket",
        };

        Assert.Equal("https://abc123.eu.r2.cloudflarestorage.com", options.ResolveServiceUrl());
        Assert.True(options.IsConfigured);
    }
}
