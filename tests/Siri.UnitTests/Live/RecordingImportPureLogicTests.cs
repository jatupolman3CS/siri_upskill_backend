using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Siri.Integrations.Google;
using Siri.Modules.Live;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>The pure parts of the P11-13 recording import: the capability decision, the backoff schedule, the Meet-code parser, the options (+ production guard) and the
/// OAuth state payload. No I/O, no clock.</summary>
public class RecordingImportPureLogicTests
{
    private static readonly DateTime Now = LiveTestData.Now;
    private static readonly string AllScopes = string.Join(' ', GoogleScopes.OpenId, GoogleScopes.CalendarEventsOwned, GoogleScopes.MeetSpaceReadonly, GoogleScopes.DriveMeetReadonly);

    // ---- Capability ----------------------------------------------------------------------------------

    private static INSTRUCTOR_GOOGLE_ACCOUNT Account(string? hostedDomain, string? scopes = null, bool revoked = false, bool checkedKind = true)
    {
        var clock = new FakeClock(Now);
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            Guid.NewGuid(), "sub", "teacher@example.test", "enc-refresh", scopes ?? AllScopes, clock, hostedDomain);

        if (!checkedKind)
        {
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.ACCOUNT_KIND_CHECKED_AT_UTC))!.GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.HOSTED_DOMAIN))!.GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
        }

        if (revoked)
        {
            account.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, clock);
        }

        return account;
    }

    [Fact]
    public void Capability_FeatureOff_IsAlwaysManual_WhateverTheAccountIs()
    {
        var capability = RecordingCapabilityCalculator.Compute(false, LiveProviderMode.GoogleMeet, Account("school.example.test"));

        Assert.Equal(RecordingCapability.Manual, capability);
    }

    [Fact]
    public void Capability_ManualOnlyRooms_IsManual()
    {
        Assert.Equal(RecordingCapability.Manual, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.ManualOnly, Account("school.example.test")));
    }

    [Fact]
    public void Capability_NoAccountOrARevokedOne_IsManual()
    {
        Assert.Equal(RecordingCapability.Manual, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, account: null));
        Assert.Equal(RecordingCapability.Manual, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account("school.example.test", revoked: true)));
    }

    [Fact]
    public void Capability_PersonalAndUnknownAccounts_AreManual_EvenWithEveryScope()
    {
        Assert.Equal(RecordingCapability.Manual, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account(hostedDomain: null)));
        Assert.Equal(RecordingCapability.Manual, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account(hostedDomain: null, checkedKind: false)));
    }

    [Fact]
    public void Capability_WorkspaceWithoutBothRecordingScopes_NeedsConsent()
    {
        Assert.Equal(
            RecordingCapability.AutoNeedsConsent,
            RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account("school.example.test", scopes: GoogleScopes.CalendarEventsOwned)));

        // One of the two is not enough.
        Assert.Equal(
            RecordingCapability.AutoNeedsConsent,
            RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account("school.example.test", scopes: GoogleScopes.CalendarEventsOwned + " " + GoogleScopes.MeetSpaceReadonly)));
    }

    [Fact]
    public void Capability_WorkspaceWithBothRecordingScopes_IsAuto()
    {
        Assert.Equal(RecordingCapability.Auto, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.GoogleMeet, Account("school.example.test")));
    }

    [Fact]
    public void Capability_TheDevLoggingProvider_StandsInForGoogleMeet()
    {
        Assert.Equal(RecordingCapability.Auto, RecordingCapabilityCalculator.Compute(true, LiveProviderMode.Logging, Account("workspace.example.test")));
    }

    [Fact]
    public void CapabilityInfo_ReportsTheFlag_AndWhetherTheScopesAreGranted()
    {
        var on = RecordingCapabilityCalculator.ToInfo(true, LiveProviderMode.GoogleMeet, Account("school.example.test"));
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Auto, AutoImportAvailable: true, ScopesGranted: true), on);

        var off = RecordingCapabilityCalculator.ToInfo(false, LiveProviderMode.GoogleMeet, Account("school.example.test"));
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Manual, AutoImportAvailable: false, ScopesGranted: true), off);

        var none = RecordingCapabilityCalculator.ToInfo(true, LiveProviderMode.GoogleMeet, account: null);
        Assert.Equal(new RecordingCapabilityInfo(RecordingCapability.Manual, AutoImportAvailable: true, ScopesGranted: false), none);
    }

    // ---- Account kind --------------------------------------------------------------------------------

    [Fact]
    public void AccountKind_FollowsTheHostedDomain_AndNeverCheckedIsUnknown()
    {
        Assert.Equal(GoogleAccountKind.Workspace, Account("school.example.test").AccountKind);
        Assert.Equal(GoogleAccountKind.Personal, Account(hostedDomain: null).AccountKind);
        Assert.Equal(GoogleAccountKind.Personal, Account("   ").AccountKind);
        Assert.Equal(GoogleAccountKind.Unknown, Account(hostedDomain: null, checkedKind: false).AccountKind);
    }

    [Fact]
    public void HostedDomain_IsNormalised_AndBounded()
    {
        Assert.Equal("school.example.test", Account("  School.Example.TEST ").HOSTED_DOMAIN);
        Assert.Throws<ArgumentException>(() => Account(new string('a', 256)));
    }

    [Fact]
    public void ResolveAccountKind_FillsAnUnknownRowOnce_AndNeverOverwritesACheckedOne()
    {
        var clock = new FakeClock(Now);
        var unknown = Account(hostedDomain: null, checkedKind: false);

        unknown.ResolveAccountKind("school.example.test", clock);
        Assert.Equal(GoogleAccountKind.Workspace, unknown.AccountKind);
        Assert.Equal(Now, unknown.ACCOUNT_KIND_CHECKED_AT_UTC);

        unknown.ResolveAccountKind(null, new FakeClock(Now.AddDays(1)));
        Assert.Equal("school.example.test", unknown.HOSTED_DOMAIN);
        Assert.Equal(Now, unknown.ACCOUNT_KIND_CHECKED_AT_UTC);
    }

    [Fact]
    public void Reconnect_RestampsTheHostedDomain_SoAnAccountSwitchedToPersonalIsNoLongerWorkspace()
    {
        var clock = new FakeClock(Now);
        var account = Account("school.example.test");

        account.Reconnect("sub-2", "me@gmail.test", "enc-2", GoogleScopes.CalendarEventsOwned, clock, hostedDomain: null);

        Assert.Equal(GoogleAccountKind.Personal, account.AccountKind);
        Assert.Null(account.HOSTED_DOMAIN);
        Assert.False(account.HasRecordingScopes);
    }

    // ---- Backoff -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 60)]
    [InlineData(4, 60)]
    [InlineData(50, 60)]
    [InlineData(-1, 10)]
    public void Backoff_Steps_AreTenTwentyFortyThenHourly(int step, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), RecordingImportBackoff.ForStep(step));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]
    [InlineData(6, 60)]
    public void Backoff_AfterTheNthConsecutiveFailure(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), RecordingImportBackoff.ForFailure(attempts));
    }

    [Theory]
    [InlineData(-5, 10)]   // not yet due: treated as the first look
    [InlineData(0, 10)]    // the first look
    [InlineData(9, 10)]
    [InlineData(10, 20)]   // second look due at +10
    [InlineData(29, 20)]
    [InlineData(30, 40)]   // third at +30
    [InlineData(69, 40)]
    [InlineData(70, 60)]   // fourth at +70, hourly from then on
    [InlineData(130, 60)]
    [InlineData(100000, 60)]
    public void Backoff_SearchPolls_FallAtPlus0_10_30_70_ThenHourly(int minutesSinceFirstSearch, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            RecordingImportBackoff.ForSearch(TimeSpan.FromMinutes(minutesSinceFirstSearch)));
    }

    // ---- Meet code -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "abc-defg-hij")]
    [InlineData("https://meet.google.com/abc-defg-hij?authuser=1", "abc-defg-hij")]
    [InlineData("  https://meet.google.com/abc-defg-hij/  ", "abc-defg-hij")]
    [InlineData("https://meet.google.com/abc-defg-hij#frag", "abc-defg-hij")]
    public void MeetCode_IsTheLastPathSegment(string url, string expected)
    {
        Assert.Equal(expected, GoogleMeetCode.TryParse(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://meet.google.com/")]
    [InlineData("https://meet.google.com/lookup/abc-defg-hij-extra")]
    [InlineData("https://meet.google.com/ABC-DEFG-HIJ")]   // codes are lower-case
    [InlineData("https://meet.google.com/abcd-efg-hij")]   // wrong shape
    [InlineData("https://meet.google.com/ab1-defg-hij")]   // digits are not part of a code
    [InlineData("https://zoom.us/j/123456789")]
    [InlineData("https://meet.google.com/abc-defg-hij/extra")] // the LAST segment must be the code
    public void MeetCode_RejectsAnythingThatIsNotAMeetCode(string? url)
    {
        Assert.Null(GoogleMeetCode.TryParse(url));
    }

    [Theory]
    [InlineData("abc-defg-hij", true)]
    [InlineData("abc-defg-hi", false)]
    [InlineData("abc_defg_hij", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MeetCode_Shape(string? code, bool valid)
    {
        Assert.Equal(valid, GoogleMeetCode.IsValid(code));
        Assert.True(GoogleMeetCode.IsValid(GoogleMeetCode.DevCode));
    }

    // ---- Options -------------------------------------------------------------------------------------

    [Fact]
    public void Options_DefaultsMatchTheContract_AndTheFeatureIsOff()
    {
        var auto = new LiveOptions().Recording.AutoImport;

        Assert.False(auto.Enabled);
        Assert.Equal(10, auto.FirstSearchDelayMinutes);
        Assert.Equal(12, auto.SearchWindowHours);
        Assert.Equal(6, auto.MaxAttempts);
        Assert.Equal(8192, auto.MaxFileSizeMegabytes);
        Assert.Equal(180, auto.TransferLeaseMinutes);
        Assert.Equal(5, auto.BatchSize);
        Assert.Equal(string.Empty, auto.DevSampleFilePath);
        Assert.Equal(8192L * 1024 * 1024, auto.MaxFileSizeBytes);
    }

    [Fact]
    public void Options_BindFromTheContractedSection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Live:Recording:AutoImport:Enabled"] = "true",
            ["Live:Recording:AutoImport:FirstSearchDelayMinutes"] = "15",
            ["Live:Recording:AutoImport:SearchWindowHours"] = "24",
            ["Live:Recording:AutoImport:MaxAttempts"] = "3",
            ["Live:Recording:AutoImport:MaxFileSizeMegabytes"] = "100",
            ["Live:Recording:AutoImport:TransferLeaseMinutes"] = "60",
            ["Live:Recording:AutoImport:BatchSize"] = "2",
            ["Live:Recording:AutoImport:DevSampleFilePath"] = "/tmp/sample.mp4",
        }).Build();

        var options = new LiveOptions();
        configuration.GetSection(LiveOptions.SectionName).Bind(options);

        var auto = options.Recording.AutoImport;
        Assert.True(auto.Enabled);
        Assert.Equal((15, 24, 3, 100, 60, 2), (auto.FirstSearchDelayMinutes, auto.SearchWindowHours, auto.MaxAttempts, auto.MaxFileSizeMegabytes, auto.TransferLeaseMinutes, auto.BatchSize));
        Assert.Equal("/tmp/sample.mp4", auto.DevSampleFilePath);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(Action<LiveRecordingAutoImportOptions> shape)
    {
        var options = LiveTestData.Options(o => shape(o.Recording.AutoImport));
        return new LiveOptionsValidator().Validate(null, options);
    }

    [Fact]
    public void Options_Validation_AcceptsTheDefaults_AndAnEnabledFeature()
    {
        Assert.True(Validate(_ => { }).Succeeded);
        Assert.True(Validate(a => a.Enabled = true).Succeeded);
        Assert.True(Validate(a => a.SearchWindowHours = 72).Succeeded);
    }

    [Theory]
    [InlineData(nameof(LiveRecordingAutoImportOptions.FirstSearchDelayMinutes))]
    [InlineData(nameof(LiveRecordingAutoImportOptions.SearchWindowHours))]
    [InlineData(nameof(LiveRecordingAutoImportOptions.MaxAttempts))]
    [InlineData(nameof(LiveRecordingAutoImportOptions.MaxFileSizeMegabytes))]
    [InlineData(nameof(LiveRecordingAutoImportOptions.TransferLeaseMinutes))]
    [InlineData(nameof(LiveRecordingAutoImportOptions.BatchSize))]
    public void Options_Validation_RefusesZeroAndNegativeValues(string property)
    {
        foreach (var bad in new[] { 0, -1 })
        {
            var result = Validate(a => typeof(LiveRecordingAutoImportOptions).GetProperty(property)!.SetValue(a, bad));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, f => f.Contains(property, StringComparison.Ordinal) && f.Contains("greater than zero", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Options_Validation_RefusesASearchWindowOverSeventyTwoHours()
    {
        var result = Validate(a => a.SearchWindowHours = 73);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("at most 72", StringComparison.Ordinal));
    }

    // ---- The shipped appsettings ---------------------------------------------------------------------

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiriUpSkill.sln")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException("SiriUpSkill.sln not found above the test output directory.");
    }

    [Theory]
    [InlineData("Siri.Api")]
    [InlineData("Siri.Workers")]
    public void ShippedAppSettings_KeepTheImportOff_WithTheContractedDefaults_AndNoDevSample(string host)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(RepoFile("src", host, "appsettings.json")).Build();

        var options = new LiveOptions();
        configuration.GetSection(LiveOptions.SectionName).Bind(options);
        var shipped = options.Recording.AutoImport;
        var defaults = new LiveRecordingAutoImportOptions();

        Assert.False(shipped.Enabled);
        Assert.Equal(
            (defaults.FirstSearchDelayMinutes, defaults.SearchWindowHours, defaults.MaxAttempts, defaults.MaxFileSizeMegabytes, defaults.TransferLeaseMinutes, defaults.BatchSize),
            (shipped.FirstSearchDelayMinutes, shipped.SearchWindowHours, shipped.MaxAttempts, shipped.MaxFileSizeMegabytes, shipped.TransferLeaseMinutes, shipped.BatchSize));
        Assert.Equal(string.Empty, shipped.DevSampleFilePath);
    }

    // ---- Production guard ----------------------------------------------------------------------------

    private static IReadOnlyList<string> ProductionProblems(string? devSamplePath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Live:PublicBaseUrl"] = "https://app.example.test",
            ["Live:Recording:AutoImport:DevSampleFilePath"] = devSamplePath,
        }).Build();

        return LiveProductionRequirements.GetProblems(configuration);
    }

    [Fact]
    public void ProductionGuard_AnEmptyDevSamplePath_IsFine()
    {
        Assert.Empty(ProductionProblems(null));
        Assert.Empty(ProductionProblems(""));
        Assert.Empty(ProductionProblems("   "));
    }

    [Fact]
    public void ProductionGuard_ADevSamplePath_IsRefused_WithoutRepeatingThePath()
    {
        var problems = ProductionProblems("/secret/dir/sample.mp4");

        var problem = Assert.Single(problems);
        Assert.Contains("DevSampleFilePath", problem);
        Assert.DoesNotContain("/secret/dir", problem);
    }

    // ---- OAuth state ---------------------------------------------------------------------------------

    [Fact]
    public void OAuthState_DefaultsToTheCalendarPurpose_AndRoundTripsThePurposeAsAString()
    {
        var state = new GoogleOAuthState(Guid.NewGuid(), "verifier", "/instructor/live-settings");
        Assert.Equal(GoogleOAuthPurpose.Calendar, state.Purpose);

        var recording = state with { Purpose = GoogleOAuthPurpose.RecordingAccess };
        var json = JsonSerializer.Serialize(recording);

        Assert.Contains("\"RecordingAccess\"", json);
        Assert.Equal(GoogleOAuthPurpose.RecordingAccess, JsonSerializer.Deserialize<GoogleOAuthState>(json)!.Purpose);
    }

    [Fact]
    public void OAuthState_AStoredPayloadFromBeforeP1113_ReadsAsTheCalendarPurpose()
    {
        // A state saved by the previous release (still in Redis during a deploy) has no Purpose member.
        var json = $$"""{"UserId":"{{Guid.NewGuid()}}","CodeVerifier":"v","ReturnPath":"/instructor/live-settings"}""";

        var state = JsonSerializer.Deserialize<GoogleOAuthState>(json)!;

        Assert.Equal(GoogleOAuthPurpose.Calendar, state.Purpose);
        Assert.Equal("v", state.CodeVerifier);
    }
}
