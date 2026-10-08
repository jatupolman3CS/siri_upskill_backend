using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>The meeting-link rules are the platform's defence against XSS (<c>javascript:</c>), open redirects and host spoofing for a URL that learners
/// are sent to — every vector the P11-03 contract (section 6.4) names has a test here.</summary>
public class MeetingLinkValidatorTests
{
    private static MeetingLinkValidator Validator(Action<LiveOptions>? configure = null) => new(LiveTestData.OptionsOf(configure));

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://zoom.us/j/123456789?pwd=abc", "https://zoom.us/j/123456789?pwd=abc")]
    [InlineData("https://us02web.zoom.us/j/123", "https://us02web.zoom.us/j/123")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/xyz", "https://teams.microsoft.com/l/meetup-join/xyz")]
    [InlineData("https://teams.live.com/meet/9876", "https://teams.live.com/meet/9876")]
    public void Validate_AllowedHostOverHttps_ReturnsNormalizedUrl(string input, string expected)
    {
        var result = Validator().Validate(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Validate_MixedCaseSchemeAndHost_IsNormalizedToLowerCase()
    {
        var result = Validator().Validate("HTTPS://MEET.GOOGLE.COM/abc");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://meet.google.com/abc", result.Value);
    }

    [Fact]
    public void Validate_SurroundingWhitespace_IsTrimmed()
    {
        var result = Validator().Validate("  https://meet.google.com/abc  \n");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://meet.google.com/abc", result.Value);
    }

    [Fact]
    public void Validate_Fragment_IsDropped()
    {
        var result = Validator().Validate("https://meet.google.com/abc#secret-fragment");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://meet.google.com/abc", result.Value);
    }

    [Fact]
    public void Validate_ExplicitDefaultPort_IsAccepted()
    {
        var result = Validator().Validate("https://meet.google.com:443/abc");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://meet.google.com/abc", result.Value);
    }

    [Fact]
    public void Validate_QuotesAndAngleBracketsInPath_AreEscapedNotRejectedAsHtml()
    {
        var result = Validator().Validate("https://meet.google.com/a\"<b>");

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("\"", result.Value);
        Assert.DoesNotContain("<", result.Value);
        Assert.DoesNotContain(">", result.Value);
    }

    // ---- Rejected: scheme ------------------------------------------------------------------------

    [Theory]
    [InlineData("http://meet.google.com/abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://meet.google.com/abc")]
    [InlineData("//meet.google.com/abc")]
    [InlineData("meet.google.com/abc")]
    [InlineData("vbscript:msgbox(1)")]
    public void Validate_NonHttpsOrNonAbsolute_IsInvalid(string input)
    {
        var result = Validator().Validate(input);

        AssertInvalid(result, MeetingLinkValidator.InvalidReason);
    }

    [Fact]
    public void Validate_TabInsideScheme_IsInvalid()
    {
        // The classic filter bypass: browsers strip tabs/newlines before parsing the scheme.
        AssertInvalid(Validator().Validate("java\tscript:alert(1)"), MeetingLinkValidator.InvalidReason);
        AssertInvalid(Validator().Validate("https://meet.google.com/ab\tc"), MeetingLinkValidator.InvalidReason);
        AssertInvalid(Validator().Validate("https://meet.google.com/ab\nc"), MeetingLinkValidator.InvalidReason);
    }

    // ---- Rejected: host spoofing -------------------------------------------------------------------

    [Theory]
    [InlineData("https://meet.google.com.evil.com/x")]
    [InlineData("https://evil.com/?https://meet.google.com")]
    [InlineData("https://evil.com/meet.google.com")]
    [InlineData("https://evilzoom.us/j/1")]
    [InlineData("https://zoom.us.evil.com/j/1")]
    [InlineData("https://notmeet.google.com.attacker.example/x")]
    [InlineData("https://google.com/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://meet.google.com./abc")]
    public void Validate_HostNotOnAllowList_IsRejectedWithHostReason(string input)
    {
        var result = Validator().Validate(input);

        AssertInvalid(result, MeetingLinkValidator.HostNotAllowedReason);
    }

    [Theory]
    [InlineData("https://meet.google.com@evil.com/")]
    [InlineData("https://user:pass@meet.google.com/abc")]
    [InlineData("https://meet.google.com:secret@evil.com/")]
    public void Validate_UserInfoInUrl_IsInvalid(string input)
    {
        var result = Validator().Validate(input);

        Assert.True(result.IsFailure);
        Assert.Equal(MeetingLinkValidator.InvalidReason, result.Error.Reason);
    }

    [Fact]
    public void Validate_BackslashTrick_IsInvalid()
    {
        AssertInvalid(Validator().Validate("https://meet.google.com\\@evil.com/"), MeetingLinkValidator.InvalidReason);
    }

    [Fact]
    public void Validate_NonDefaultPort_IsInvalid()
    {
        AssertInvalid(Validator().Validate("https://meet.google.com:8443/abc"), MeetingLinkValidator.InvalidReason);
    }

    [Fact]
    public void Validate_PunycodeLookalikeHost_IsRejected()
    {
        // "meet.google.com" with a Cyrillic 'e' — its ASCII (punycode) form is not on the allow-list.
        var result = Validator().Validate("https://mеet.google.com/abc");

        Assert.True(result.IsFailure);
        Assert.Equal(MeetingLinkValidator.HostNotAllowedReason, result.Error.Reason);
    }

    // ---- Rejected: shape ---------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyInput_IsInvalid(string? input)
    {
        AssertInvalid(Validator().Validate(input), MeetingLinkValidator.InvalidReason);
    }

    [Fact]
    public void Validate_Exactly500Chars_IsAccepted_AndOver500_IsRejected()
    {
        const string prefix = "https://meet.google.com/";
        var at500 = prefix + new string('a', MeetingLinkValidator.MaxLength - prefix.Length);
        var at501 = at500 + "a";

        Assert.True(Validator().Validate(at500).IsSuccess);
        AssertInvalid(Validator().Validate(at501), MeetingLinkValidator.InvalidReason);
    }

    [Fact]
    public void Validate_SpaceInTheMiddle_IsInvalid()
    {
        AssertInvalid(Validator().Validate("https://meet.google.com/abc def"), MeetingLinkValidator.InvalidReason);
    }

    // ---- Configurable allow-list -------------------------------------------------------------------

    [Fact]
    public void Validate_CustomAllowList_ReplacesTheDefaults()
    {
        var validator = Validator(o => o.AllowedMeetingHosts = ["meet.example.org"]);

        Assert.True(validator.Validate("https://meet.example.org/room").IsSuccess);
        Assert.True(validator.Validate("https://eu.meet.example.org/room").IsSuccess);
        AssertInvalid(validator.Validate("https://meet.google.com/abc"), MeetingLinkValidator.HostNotAllowedReason);
    }

    [Fact]
    public void Validate_AllowListEntries_AreCaseInsensitive()
    {
        var validator = Validator(o => o.AllowedMeetingHosts = ["  Meet.Example.ORG "]);

        Assert.True(validator.Validate("https://meet.example.org/room").IsSuccess);
    }

    [Fact]
    public void Validate_ErrorMessages_NeverEchoTheSubmittedUrl()
    {
        var result = Validator().Validate("https://evil.example.test/secret-room-token-123");

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("secret-room-token-123", result.Error.Message);
        Assert.DoesNotContain("evil.example.test", result.Error.Message);
    }

    private static void AssertInvalid(Siri.SharedKernel.Result<string> result, string expectedReason)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(expectedReason, result.Error.Reason);
    }
}
