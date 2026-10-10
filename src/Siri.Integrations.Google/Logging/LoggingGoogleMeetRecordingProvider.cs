using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google.Logging;

/// <summary>The file the fake recording provider serves as "the Meet recording".</summary>
/// <param name="Content">Readable stream; the caller disposes it.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="FileName">Display name.</param>
public sealed record MeetRecordingDevSample(Stream Content, long Length, string FileName);

/// <summary>
/// Where <see cref="LoggingGoogleMeetRecordingProvider"/> gets the bytes it serves (P11-13, development only). The Live module registers an
/// implementation backed by <c>Live:Recording:AutoImport:DevSampleFilePath</c>; when nothing is registered
/// <see cref="DefaultMeetRecordingDevSampleSource"/> serves a few KB of placeholder bytes.
/// </summary>
public interface IMeetRecordingDevSampleSource
{
    /// <summary>Opens a fresh stream over the sample. Throws when a configured file cannot be read (a development misconfiguration).</summary>
    Task<MeetRecordingDevSample> OpenAsync(CancellationToken ct);
}

/// <summary>
/// A few KB of bytes (an MP4 <c>ftyp</c> box followed by zeros). It is <b>not a playable video</b>: with no real sample configured the whole pipeline
/// (find, copy, hand to the video provider) still runs end to end, but the video provider will refuse to transcode it. Point
/// <c>Live:Recording:AutoImport:DevSampleFilePath</c> at a real short MP4 to rehearse a successful import.
/// </summary>
public sealed class DefaultMeetRecordingDevSampleSource : IMeetRecordingDevSampleSource
{
    private const int SampleLength = 4096;

    public Task<MeetRecordingDevSample> OpenAsync(CancellationToken ct)
    {
        var bytes = new byte[SampleLength];
        // 'ftyp' box: size 24, "ftyp", brand "isom", minor version 512, compatible brands "isom" "iso2".
        byte[] header = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'i', (byte)'s', (byte)'o', (byte)'2'];
        header.CopyTo(bytes, 0);

        return Task.FromResult(new MeetRecordingDevSample(new MemoryStream(bytes, writable: false), bytes.Length, "dev-recording.mp4"));
    }
}

/// <summary>
/// <b>DEVELOPMENT ONLY.</b> A fake <see cref="IGoogleMeetRecordingProvider"/> that never contacts Google (P11-13 contract section 4). Selected
/// exclusively by the explicit setting <c>Live:Provider=Logging</c>; never a default; refused in Production.
/// <para>
/// <see cref="FindRecordingsAsync"/> answers one <see cref="MeetRecordingState.FileGenerated"/> recording as soon as the search window has opened
/// (<c>now &gt;= notBeforeUtc</c>), and nothing before that. <see cref="OpenDownloadAsync"/> serves the bytes from
/// <see cref="IMeetRecordingDevSampleSource"/>. Like the fake OAuth service it only accepts tokens it issued (the <c>dev-</c> prefix) and only file
/// ids it handed out, so the unauthorized / not-found paths stay testable. The meeting code is never logged or stored: ids are derived from its hash.
/// </para>
/// </summary>
public sealed class LoggingGoogleMeetRecordingProvider : IGoogleMeetRecordingProvider
{
    public const string DevFileIdPrefix = "dev-";

    private readonly IMeetRecordingDevSampleSource _sampleSource;
    private readonly IClock _clock;
    private readonly ILogger<LoggingGoogleMeetRecordingProvider> _logger;

    public LoggingGoogleMeetRecordingProvider(
        IMeetRecordingDevSampleSource sampleSource,
        IClock clock,
        ILogger<LoggingGoogleMeetRecordingProvider> logger)
    {
        _sampleSource = sampleSource;
        _clock = clock;
        _logger = logger;
        _logger.LogWarning(
            "LoggingGoogleMeetRecordingProvider is active (Live:Provider=Logging): Google Meet recordings are FAKED. Development only - never use in production.");
    }

    public Task<Result<IReadOnlyList<MeetRecording>>> FindRecordingsAsync(
        string accessToken, string meetingCode, DateTime notBeforeUtc, DateTime notAfterUtc, CancellationToken ct)
    {
        if (!IsDevToken(accessToken))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<MeetRecording>>(
                GoogleErrors.Unauthorized("Fake Google Meet: not a dev access token.")));
        }

        var now = _clock.UtcNow;
        if (string.IsNullOrWhiteSpace(meetingCode) || now < notBeforeUtc || notAfterUtc < notBeforeUtc)
        {
            return Task.FromResult(Result.Success<IReadOnlyList<MeetRecording>>([]));
        }

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(meetingCode)))[..12];
        var ended = now < notAfterUtc ? now : notAfterUtc;
        IReadOnlyList<MeetRecording> recordings =
        [
            new MeetRecording($"conferenceRecords/dev-{key}/recordings/dev-1", MeetRecordingState.FileGenerated, notBeforeUtc, ended, $"{DevFileIdPrefix}{key}"),
        ];

        _logger.LogInformation("Fake Google Meet: one recording found.");
        return Task.FromResult(Result.Success(recordings));
    }

    public async Task<Result<MeetRecordingDownload>> OpenDownloadAsync(string accessToken, string driveFileId, CancellationToken ct)
    {
        if (!IsDevToken(accessToken))
        {
            return Result.Failure<MeetRecordingDownload>(GoogleErrors.Unauthorized("Fake Google Drive: not a dev access token."));
        }

        if (string.IsNullOrEmpty(driveFileId) || !driveFileId.StartsWith(DevFileIdPrefix, StringComparison.Ordinal))
        {
            return Result.Failure<MeetRecordingDownload>(GoogleErrors.NotFound("Fake Google Drive: no such file."));
        }

        var sample = await _sampleSource.OpenAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Fake Google Drive: serving the dev sample ({Length} bytes).", sample.Length);
        return Result.Success(new MeetRecordingDownload(sample.Content, sample.Length, sample.FileName, owner: null));
    }

    private static bool IsDevToken(string? accessToken) =>
        !string.IsNullOrEmpty(accessToken) && accessToken.StartsWith(LoggingGoogleOAuthService.DevTokenPrefix, StringComparison.Ordinal);
}
