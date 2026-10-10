using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;

namespace Siri.UnitTests.Google;

/// <summary>The dev-only fake behind <c>Live:Provider=Logging</c> for the P11-13 recording import.</summary>
public sealed class LoggingGoogleMeetRecordingProviderTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc);
    private const string Token = "dev-access-token";
    private const string Code = "abc-defg-hij";

    private static LoggingGoogleMeetRecordingProvider Create(
        IMeetRecordingDevSampleSource? source = null,
        DateTime? now = null,
        CapturingLogger<LoggingGoogleMeetRecordingProvider>? logger = null) =>
        new(source ?? new DefaultMeetRecordingDevSampleSource(), new FixedClock(now ?? Now), logger ?? new CapturingLogger<LoggingGoogleMeetRecordingProvider>());

    [Fact]
    public async Task FindRecordingsAsync_BeforeTheWindowOpens_FindsNothing()
    {
        var result = await Create().FindRecordingsAsync(Token, Code, Now.AddMinutes(1), Now.AddHours(12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task FindRecordingsAsync_OnceTheWindowIsOpen_ReturnsOneGeneratedRecordingWithAFileId()
    {
        var notBefore = Now.AddHours(-2);

        var result = await Create().FindRecordingsAsync(Token, Code, notBefore, Now.AddHours(12), CancellationToken.None);

        var recording = Assert.Single(result.Value);
        Assert.Equal(MeetRecordingState.FileGenerated, recording.State);
        Assert.StartsWith("dev-", recording.DriveFileId);
        Assert.Equal(notBefore, recording.StartedAtUtc);
        Assert.Equal(Now, recording.EndedAtUtc);
    }

    [Fact]
    public async Task FindRecordingsAsync_IsDeterministicPerMeetingCode_AndDoesNotEchoTheCode()
    {
        var provider = Create();

        var first = await provider.FindRecordingsAsync(Token, Code, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);
        var second = await provider.FindRecordingsAsync(Token, Code, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);
        var other = await provider.FindRecordingsAsync(Token, "xyz-abcd-efg", Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);

        Assert.Equal(first.Value[0].DriveFileId, second.Value[0].DriveFileId);
        Assert.NotEqual(first.Value[0].DriveFileId, other.Value[0].DriveFileId);
        Assert.DoesNotContain(Code, first.Value[0].RecordingName);
        Assert.DoesNotContain(Code, first.Value[0].DriveFileId!);
    }

    [Fact]
    public async Task FindRecordingsAsync_ATokenTheFakeDidNotIssue_IsUnauthorized()
    {
        var result = await Create().FindRecordingsAsync("ya29.real", Code, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
    }

    [Fact]
    public async Task OpenDownloadAsync_WithoutAConfiguredSample_ServesAFewKilobytes()
    {
        var found = await Create().FindRecordingsAsync(Token, Code, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);

        var opened = await Create().OpenDownloadAsync(Token, found.Value[0].DriveFileId!, CancellationToken.None);

        using var download = opened.Value;
        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);
        Assert.Equal(copy.Length, download.ContentLength);
        Assert.InRange(copy.Length, 1024, 64 * 1024);
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(copy.ToArray(), 4, 4));
    }

    [Fact]
    public async Task OpenDownloadAsync_UsesTheConfiguredSampleSource()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var source = new FixedSampleSource(new MeetRecordingDevSample(new MemoryStream(bytes), bytes.Length, "lesson.mp4"));

        var opened = await Create(source).OpenDownloadAsync(Token, "dev-anything", CancellationToken.None);

        using var download = opened.Value;
        Assert.Equal(5, download.ContentLength);
        Assert.Equal("lesson.mp4", download.FileName);
        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Theory]
    [InlineData("1AbCdEfGhIjKlMnOpQrStUvWxYz")]
    [InlineData("")]
    public async Task OpenDownloadAsync_AFileIdTheFakeDidNotHandOut_IsNotFound(string fileId)
    {
        var result = await Create().OpenDownloadAsync(Token, fileId, CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task OpenDownloadAsync_ATokenTheFakeDidNotIssue_IsUnauthorized()
    {
        var result = await Create().OpenDownloadAsync("ya29.real", "dev-abc", CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
    }

    [Fact]
    public async Task EveryCall_NeverLogsTheMeetingCodeOrTheToken()
    {
        var logger = new CapturingLogger<LoggingGoogleMeetRecordingProvider>();
        var provider = Create(logger: logger);

        var found = await provider.FindRecordingsAsync(Token, Code, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None);
        (await provider.OpenDownloadAsync(Token, found.Value[0].DriveFileId!, CancellationToken.None)).Value.Dispose();

        Assert.DoesNotContain(Code, logger.AllText);
        Assert.DoesNotContain(Token, logger.AllText);
    }

    private sealed class FixedSampleSource(MeetRecordingDevSample sample) : IMeetRecordingDevSampleSource
    {
        public Task<MeetRecordingDevSample> OpenAsync(CancellationToken ct) => Task.FromResult(sample);
    }
}
