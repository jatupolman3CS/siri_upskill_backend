using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>
/// <see cref="IGoogleMeetRecordingProvider"/> over plain <see cref="HttpClient"/> against the documented Google Meet REST API v2
/// (<c>conferenceRecords.list</c>, <c>conferenceRecords.recordings.list</c>) and the Drive v3 <c>files.get</c> download
/// (P11-13 contract section 4). No Google SDK.
/// <para>
/// Acts for the one instructor whose access token is supplied per call. The meeting code, Drive file id, access token and recording
/// names are never logged — only the operation name, HTTP status and Google's sanitised reason token are (see
/// <see cref="GoogleHttp.MapMeetDriveErrorAsync"/>). There is no in-process retry: the Live import job owns backoff, and a half-read
/// download cannot be replayed anyway.
/// </para>
/// <para>
/// The download is streamed: the response is opened with <see cref="HttpCompletionOption.ResponseHeadersRead"/> and handed to the caller,
/// never buffered whole (a recording can be several GB). Disposing the returned <see cref="MeetRecordingDownload"/> releases the
/// connection. Redirects are refused by the named client, so the bearer token can never follow one.
/// </para>
/// </summary>
public sealed class GoogleMeetRecordingProvider : IGoogleMeetRecordingProvider
{
    /// <summary>Named <see cref="HttpClient"/> registered by <c>AddGoogleIntegration</c> (Meet REST + Drive).</summary>
    public const string HttpClientName = "google-meet";

    internal const string ConferenceRecordsEndpoint = "https://meet.googleapis.com/v2/conferenceRecords";
    internal const string MeetApiBase = "https://meet.googleapis.com/v2/";
    internal const string DriveFilesEndpoint = "https://www.googleapis.com/drive/v3/files/";

    /// <summary>Documented maximum for both list calls (higher values are capped by Google).</summary>
    internal const int PageSize = 100;

    /// <summary>Hard stop on pagination so a misbehaving response (a token that never ends) cannot loop forever.</summary>
    internal const int MaxPages = 20;

    // A Meet code: three letters, four letters, three letters (abc-defg-hij). The only shape that is ever put inside the list filter.
    private static readonly Regex MeetingCodePattern =
        new("^[a-z]{3}-[a-z]{4}-[a-z]{3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // "conferenceRecords/{id}" as Google names it. Checked before the name is placed in a URL path.
    private static readonly Regex ConferenceRecordNamePattern =
        new("^conferenceRecords/[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // A Drive file id: letters, digits, '-' and '_'. Checked before the id is placed in a URL path.
    private static readonly Regex DriveFileIdPattern =
        new("^[A-Za-z0-9_-]{1,200}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // Google writes RFC 3339 with up to nanosecond fractions; .NET reads at most 7 fraction digits, so longer fractions are cut.
    private static readonly Regex LongFraction =
        new(@"(\.\d{7})\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleMeetRecordingProvider> _logger;

    public GoogleMeetRecordingProvider(IHttpClientFactory httpClientFactory, ILogger<GoogleMeetRecordingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>True for a Meet room code in the <c>abc-defg-hij</c> shape (lower-case letters only).</summary>
    public static bool IsValidMeetingCode(string? meetingCode) =>
        !string.IsNullOrEmpty(meetingCode) && MeetingCodePattern.IsMatch(meetingCode);

    public async Task<Result<IReadOnlyList<MeetRecording>>> FindRecordingsAsync(
        string accessToken,
        string meetingCode,
        DateTime notBeforeUtc,
        DateTime notAfterUtc,
        CancellationToken ct)
    {
        const string operation = "conferenceRecords.list";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<IReadOnlyList<MeetRecording>>(operation);
        }

        if (!IsValidMeetingCode(meetingCode))
        {
            return Result.Failure<IReadOnlyList<MeetRecording>>(
                GoogleErrors.BadRequest("Google conferenceRecords.list was not attempted: the meeting code is not in the abc-defg-hij form."));
        }

        var conferences = await ListConferencesAsync(accessToken, meetingCode, notBeforeUtc, notAfterUtc, ct).ConfigureAwait(false);
        if (conferences.IsFailure)
        {
            return Result.Failure<IReadOnlyList<MeetRecording>>(conferences.Error);
        }

        var recordings = new List<(DateTime Order, MeetRecording Recording)>();
        foreach (var conference in conferences.Value)
        {
            var listed = await ListRecordingsAsync(accessToken, conference.Name, ct).ConfigureAwait(false);
            if (listed.IsFailure)
            {
                return Result.Failure<IReadOnlyList<MeetRecording>>(listed.Error);
            }

            recordings.AddRange(listed.Value.Select(r => (r.StartedAtUtc ?? conference.StartedAtUtc, r)));
        }

        IReadOnlyList<MeetRecording> ordered = recordings
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Recording.RecordingName, StringComparer.Ordinal)
            .Select(x => x.Recording)
            .ToList();
        return Result.Success(ordered);
    }

    public async Task<Result<MeetRecordingDownload>> OpenDownloadAsync(string accessToken, string driveFileId, CancellationToken ct)
    {
        const string operation = "files.get(media)";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<MeetRecordingDownload>(operation);
        }

        if (string.IsNullOrEmpty(driveFileId) || !DriveFileIdPattern.IsMatch(driveFileId))
        {
            return Result.Failure<MeetRecordingDownload>(
                GoogleErrors.BadRequest("Google files.get was not attempted: the file id is missing or malformed."));
        }

        var opened = await GoogleHttp.SendStreamingAsync<MeetRecordingDownload>(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => BuildGet($"{DriveFilesEndpoint}{driveFileId}?alt=media&supportsAllDrives=true", accessToken),
            async (response, owner, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<MeetRecordingDownload>(
                        await GoogleHttp.MapMeetDriveErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                }

                // A 200 that is a web page or a JSON error document is not a video: refuse to stream it on to the video provider.
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null
                    && (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                        || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Google {Operation} returned a non-media body ({MediaType}).", operation, mediaType);
                    return Result.Failure<MeetRecordingDownload>(GoogleErrors.Transient($"Google {operation} returned an unexpected response."));
                }

                var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var download = new MeetRecordingDownload(
                    stream,
                    response.Content.Headers.ContentLength,
                    ReadFileName(response),
                    owner);
                return Result.Success(download);
            },
            ct).ConfigureAwait(false);

        if (opened.IsFailure || opened.Value.ContentLength is not null)
        {
            return opened;
        }

        // The media response carried no Content-Length (chunked): ask Drive for the size so the caller can enforce its limit.
        var size = await ReadMetadataAsync(accessToken, driveFileId, ct).ConfigureAwait(false);
        if (size is null)
        {
            return opened;
        }

        var enriched = new MeetRecordingDownload(opened.Value.Content, size.Value.Size, opened.Value.FileName ?? size.Value.Name, opened.Value);
        return Result.Success(enriched);
    }

    private async Task<Result<IReadOnlyList<ConferenceInfo>>> ListConferencesAsync(
        string accessToken, string meetingCode, DateTime notBeforeUtc, DateTime notAfterUtc, CancellationToken ct)
    {
        const string operation = "conferenceRecords.list";

        // The filter is the documented form: space.meeting_code = "abc-mnop-xyz". The code was validated, so nothing else can reach it.
        var filter = Uri.EscapeDataString($"space.meeting_code = \"{meetingCode}\"");
        var found = new List<ConferenceInfo>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{ConferenceRecordsEndpoint}?filter={filter}&pageSize={PageSize}";
            if (pageToken is not null)
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var current = await GoogleHttp.SendAsync<(IReadOnlyList<ConferenceInfo> Items, string? Next, bool OlderThanWindow)>(
                _httpClientFactory,
                HttpClientName,
                _logger,
                operation,
                () => BuildGet(url, accessToken),
                async (response, token) =>
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return Result.Failure<(IReadOnlyList<ConferenceInfo>, string?, bool)>(
                            await GoogleHttp.MapMeetDriveErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                    }

                    var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                    var items = new List<ConferenceInfo>();
                    var olderThanWindow = false;
                    if (root?["conferenceRecords"] is JsonArray records)
                    {
                        foreach (var record in records)
                        {
                            var name = GoogleHttp.GetString(record?["name"]);
                            var started = ParseTimestamp(GoogleHttp.GetString(record?["startTime"]));
                            if (name is null || started is null || !ConferenceRecordNamePattern.IsMatch(name))
                            {
                                continue;
                            }

                            if (started < notBeforeUtc)
                            {
                                olderThanWindow = true;
                                continue;
                            }

                            if (started <= notAfterUtc)
                            {
                                items.Add(new ConferenceInfo(name, started.Value));
                            }
                        }
                    }

                    var next = GoogleHttp.GetString(root?["nextPageToken"]);
                    return Result.Success<(IReadOnlyList<ConferenceInfo>, string?, bool)>(
                        (items, string.IsNullOrEmpty(next) ? null : next, olderThanWindow));
                },
                ct).ConfigureAwait(false);

            if (current.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ConferenceInfo>>(current.Error);
            }

            found.AddRange(current.Value.Items);

            // Google lists newest conferences first: once a page reached one that started before the window, later pages are older still.
            if (current.Value.Next is null || current.Value.OlderThanWindow || string.Equals(current.Value.Next, pageToken, StringComparison.Ordinal))
            {
                break;
            }

            pageToken = current.Value.Next;
        }

        return Result.Success<IReadOnlyList<ConferenceInfo>>(found);
    }

    private async Task<Result<IReadOnlyList<MeetRecording>>> ListRecordingsAsync(string accessToken, string conferenceRecordName, CancellationToken ct)
    {
        const string operation = "conferenceRecords.recordings.list";
        var found = new List<MeetRecording>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{MeetApiBase}{conferenceRecordName}/recordings?pageSize={PageSize}";
            if (pageToken is not null)
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var current = await GoogleHttp.SendAsync<(IReadOnlyList<MeetRecording> Items, string? Next)>(
                _httpClientFactory,
                HttpClientName,
                _logger,
                operation,
                () => BuildGet(url, accessToken),
                async (response, token) =>
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return Result.Failure<(IReadOnlyList<MeetRecording>, string?)>(
                            await GoogleHttp.MapMeetDriveErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                    }

                    var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                    var items = new List<MeetRecording>();
                    if (root?["recordings"] is JsonArray recordings)
                    {
                        foreach (var node in recordings)
                        {
                            if (ParseRecording(node) is { } recording)
                            {
                                items.Add(recording);
                            }
                        }
                    }

                    var next = GoogleHttp.GetString(root?["nextPageToken"]);
                    return Result.Success<(IReadOnlyList<MeetRecording>, string?)>((items, string.IsNullOrEmpty(next) ? null : next));
                },
                ct).ConfigureAwait(false);

            if (current.IsFailure)
            {
                return Result.Failure<IReadOnlyList<MeetRecording>>(current.Error);
            }

            found.AddRange(current.Value.Items);
            if (current.Value.Next is null || string.Equals(current.Value.Next, pageToken, StringComparison.Ordinal))
            {
                break;
            }

            pageToken = current.Value.Next;
        }

        return Result.Success<IReadOnlyList<MeetRecording>>(found);
    }

    /// <summary>Best-effort <c>files.get?fields=size,name</c>. Any failure just means "size unknown" — the download itself already succeeded.</summary>
    private async Task<(long Size, string? Name)?> ReadMetadataAsync(string accessToken, string driveFileId, CancellationToken ct)
    {
        const string operation = "files.get(metadata)";
        var result = await GoogleHttp.SendAsync<(long Size, string? Name)?>(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => BuildGet($"{DriveFilesEndpoint}{driveFileId}?fields=size,name&supportsAllDrives=true", accessToken),
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<(long, string?)?>(
                        await GoogleHttp.MapMeetDriveErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                }

                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                // Drive reports int64 as a JSON string ("size": "123").
                var sizeText = GoogleHttp.GetString(root?["size"]);
                if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0)
                {
                    return Result.Success<(long, string?)?>(null);
                }

                return Result.Success<(long, string?)?>((size, GoogleHttp.GetString(root?["name"])));
            },
            ct).ConfigureAwait(false);

        return result.IsSuccess ? result.Value : null;
    }

    private static MeetRecording? ParseRecording(JsonNode? node)
    {
        var name = GoogleHttp.GetString(node?["name"]);
        if (name is null)
        {
            return null;
        }

        var state = GoogleHttp.GetString(node?["state"]) switch
        {
            "FILE_GENERATED" => MeetRecordingState.FileGenerated,
            "ENDED" => MeetRecordingState.Ended,
            // STARTED, STATE_UNSPECIFIED and any state Google adds later: nothing to download yet, so keep waiting.
            _ => MeetRecordingState.Started,
        };

        var fileId = GoogleHttp.GetString(node?["driveDestination"]?["file"]);

        // A "file generated" recording without a file id cannot be downloaded: treat it as "ended, file not ready" so the caller keeps looking.
        if (state == MeetRecordingState.FileGenerated && string.IsNullOrEmpty(fileId))
        {
            state = MeetRecordingState.Ended;
        }

        return new MeetRecording(
            name,
            state,
            ParseTimestamp(GoogleHttp.GetString(node?["startTime"])),
            ParseTimestamp(GoogleHttp.GetString(node?["endTime"])),
            state == MeetRecordingState.FileGenerated ? fileId : null);
    }

    /// <summary>Parses a Google RFC 3339 timestamp (e.g. <c>2026-10-08T03:00:00.123456789Z</c>) to a UTC instant; <c>null</c> when absent or unreadable.</summary>
    internal static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = LongFraction.Replace(value, "$1");
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static string? ReadFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim('"');
    }

    private static HttpRequestMessage BuildGet(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        GoogleHttp.SetBearer(request, accessToken);
        return request;
    }

    private sealed record ConferenceInfo(string Name, DateTime StartedAtUtc);
}
