using System.ComponentModel.DataAnnotations;
using Siri.Integrations.Video.Bunny;

namespace Siri.UnitTests.Video;

public class VideoProviderOptionsTests
{
    /// <summary>Runs the exact validation Options-pattern <c>ValidateDataAnnotations()</c> runs at
    /// startup: attributes on all properties.</summary>
    private static List<ValidationResult> Validate(VideoProviderOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_FailValidation_AllRequiredFieldsMissing()
    {
        var options = new VideoProviderOptions();
        var errors = Validate(options);

        // LibraryId, ApiKey, ReadOnlyApiKey, PullZone are all [Required].
        Assert.True(errors.Count >= 4, $"Expected >= 4 validation errors but got {errors.Count}");
    }

    [Fact]
    public void AllRequiredFieldsSet_PassesValidation()
    {
        var options = new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
        };

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void CdnHostname_IsOptional_NotRequired()
    {
        var options = new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
            // CdnHostname intentionally not set — should still pass validation.
        };

        Assert.Empty(Validate(options));
        Assert.Equal(string.Empty, options.CdnHostname);
    }

    [Fact]
    public void TokenAuthenticationKey_IsOptional_NotRequired()
    {
        var options = new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
        };

        Assert.Empty(Validate(options));
        Assert.Equal(string.Empty, options.TokenAuthenticationKey);
    }

    [Fact]
    public void SectionName_IsVideoProvider()
    {
        Assert.Equal("VideoProvider", VideoProviderOptions.SectionName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void LibraryId_EmptyOrWhitespace_FailsValidation(string libraryId)
    {
        var options = new VideoProviderOptions
        {
            LibraryId = libraryId,
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
        };

        var errors = Validate(options);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ApiKey_EmptyOrWhitespace_FailsValidation(string apiKey)
    {
        var options = new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = apiKey,
            ReadOnlyApiKey = "test-readonly-key",
            PullZone = "my-pull-zone",
        };

        var errors = Validate(options);
        Assert.NotEmpty(errors);
    }
}
