using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Siri.Integrations.Google;

namespace Siri.UnitTests.Google;

public sealed class GoogleMeetRecordingProviderTests
{
    private const string Token = "ya29.meet-access-token";
    private const string Code = "abc-defg-hij";
    private const string FileId = "1AbCdEfGhIjKlMnOpQrStUvWxYz_0123456789";

    private static readonly DateTime NotBefore = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NotAfter = new(2026, 10, 8, 20, 0, 0, DateTimeKind.Utc);

    private static GoogleMeetRecordingProvider Create(HttpMessageHandler handler, CapturingLogger<GoogleMeetRecordingProvider>? logger = null) =>
        new(new StubHttpClientFactory(handler), logger ?? new CapturingLogger<GoogleMeetRecordingProvider>());

    private static string ConferencesJson(string? nextPageToken = null, params (string Name, string Start)[] records)
    {
        var root = new JsonObject
        {
            ["conferenceRecords"] = new JsonArray(records
                .Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["startTime"] = r.Start,
                    ["endTime"] = "2026-10-08T06:00:00Z",
                    ["space"] = "spaces/x",
                })
                .ToArray()),
        };
        if (nextPageToken is not null)
        {
            root["nextPageToken"] = nextPageToken;
        }

        return root.ToJsonString();
    }

    private static string RecordingsJson(string? nextPageToken = null, params string[] recordings)
    {
        var root = new JsonObject { ["recordings"] = new JsonArray(recordings.Select(r => JsonNode.Parse(r)).ToArray()) };
        if (nextPageToken is not null)
        {
            root["nextPageToken"] = nextPageToken;
        }

        return root.ToJsonString();
    }

    private static string Recording(string name, string state, string start, string end, string? file = null)
    {
        var node = new JsonObject { ["name"] = name, ["state"] = state, ["startTime"] = start, ["endTime"] = end };
        if (file is not null)
        {
            node["driveDestination"] = new JsonObject
            {
                ["file"] = file,
                ["exportUri"] = $"https://drive.google.com/file/d/{file}/view",
            };
        }

        return node.ToJsonString();
    }

    // ---- FindRecordingsAsync: request shape ------------------------------------------------------------------

    [Fact]
    public async Task FindRecordingsAsync_FiltersByMeetingCodeAndSendsBearer()
    {
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK, ConferencesJson());

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("meet.googleapis.com", request.Uri.Host);
        Assert.Equal("/v2/conferenceRecords", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal($"space.meeting_code = \"{Code}\"", query["filter"]);
        Assert.Equal("100", query["pageSize"]);
        Assert.Null(query["pageToken"]);
        Assert.Equal($"Bearer {Token}", request.Authorization);
    }

    [Fact]
    public async Task FindRecordingsAsync_ListsTheRecordingsOfEachConferenceInTheWindow()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00.123456789Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/c1/recordings/r1", "FILE_GENERATED", "2026-10-08T05:01:00Z", "2026-10-08T06:30:00Z", FileId)));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        var recording = Assert.Single(result.Value);
        Assert.Equal("conferenceRecords/c1/recordings/r1", recording.RecordingName);
        Assert.Equal(MeetRecordingState.FileGenerated, recording.State);
        Assert.Equal(FileId, recording.DriveFileId);
        Assert.Equal(new DateTime(2026, 10, 8, 5, 1, 0, DateTimeKind.Utc), recording.StartedAtUtc);
        Assert.Equal(new DateTime(2026, 10, 8, 6, 30, 0, DateTimeKind.Utc), recording.EndedAtUtc);
        Assert.Equal(DateTimeKind.Utc, recording.StartedAtUtc!.Value.Kind);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/v2/conferenceRecords/c1/recordings", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal("100", HttpUtility.ParseQueryString(handler.Requests[1].Uri.Query)["pageSize"]);
        Assert.Equal($"Bearer {Token}", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task FindRecordingsAsync_IgnoresConferencesOutsideTheWindow()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(
                null,
                ("conferenceRecords/late", "2026-10-08T21:00:00Z"),
                ("conferenceRecords/inside", "2026-10-08T05:00:00Z"),
                ("conferenceRecords/early", "2026-10-08T02:59:59Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson());

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Empty(result.Value);
        // One list call, plus the recordings call of the single conference that started inside the window.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/v2/conferenceRecords/inside/recordings", handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task FindRecordingsAsync_FollowsConferencePagination()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson("tok/1+2", ("conferenceRecords/new", "2026-10-08T10:00:00Z")))
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/old", "2026-10-08T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/new/recordings/r", "ENDED", "2026-10-08T10:01:00Z", "2026-10-08T11:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/old/recordings/r", "FILE_GENERATED", "2026-10-08T05:01:00Z", "2026-10-08T06:00:00Z", FileId)));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("tok/1+2", HttpUtility.ParseQueryString(handler.Requests[1].Uri.Query)["pageToken"]);
        // Oldest first.
        Assert.Equal(["conferenceRecords/old/recordings/r", "conferenceRecords/new/recordings/r"], result.Value.Select(r => r.RecordingName));
    }

    [Fact]
    public async Task FindRecordingsAsync_StopsPagingOnceAPageReachesConferencesOlderThanTheWindow()
    {
        // Google lists newest first, so a conference older than the window means every later page is older too.
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson("more", ("conferenceRecords/in", "2026-10-08T05:00:00Z"), ("conferenceRecords/old", "2026-10-07T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson());

        await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Query.Contains("pageToken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindRecordingsAsync_FollowsRecordingPagination()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson("next-rec", Recording("conferenceRecords/c1/recordings/a", "ENDED", "2026-10-08T05:01:00Z", "2026-10-08T05:30:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/c1/recordings/b", "FILE_GENERATED", "2026-10-08T05:31:00Z", "2026-10-08T06:00:00Z", FileId)));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("next-rec", HttpUtility.ParseQueryString(handler.Requests[2].Uri.Query)["pageToken"]);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task FindRecordingsAsync_EndlessPageTokens_StopAtTheHardPageCap()
    {
        var page = 0;
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // Every page claims there is another one, with a fresh token each time: only the cap can end this.
            Content = new StringContent(ConferencesJson($"token-{++page}"), Encoding.UTF8, "application/json"),
        });

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GoogleMeetRecordingProvider.MaxPages, handler.Requests.Count);
    }

    [Theory]
    [InlineData("STARTED", MeetRecordingState.Started)]
    [InlineData("ENDED", MeetRecordingState.Ended)]
    [InlineData("FILE_GENERATED", MeetRecordingState.FileGenerated)]
    [InlineData("STATE_UNSPECIFIED", MeetRecordingState.Started)]
    [InlineData("SOMETHING_NEW", MeetRecordingState.Started)]
    public async Task FindRecordingsAsync_MapsEveryRecordingState(string wire, MeetRecordingState expected)
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/c1/recordings/r", wire, "2026-10-08T05:01:00Z", "2026-10-08T06:00:00Z", FileId)));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        var recording = Assert.Single(result.Value);
        Assert.Equal(expected, recording.State);
        // The Drive file id is only ever surfaced once the file is generated.
        Assert.Equal(expected == MeetRecordingState.FileGenerated ? FileId : null, recording.DriveFileId);
    }

    [Fact]
    public async Task FindRecordingsAsync_FileGeneratedWithoutAFileId_ReadsAsEnded()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(null, Recording("conferenceRecords/c1/recordings/r", "FILE_GENERATED", "2026-10-08T05:01:00Z", "2026-10-08T06:00:00Z")));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        var recording = Assert.Single(result.Value);
        Assert.Equal(MeetRecordingState.Ended, recording.State);
        Assert.Null(recording.DriveFileId);
    }

    [Fact]
    public async Task FindRecordingsAsync_OldestRecordingFirst()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00Z")))
            .Enqueue(HttpStatusCode.OK, RecordingsJson(
                null,
                Recording("conferenceRecords/c1/recordings/second", "ENDED", "2026-10-08T07:00:00Z", "2026-10-08T07:30:00Z"),
                Recording("conferenceRecords/c1/recordings/first", "ENDED", "2026-10-08T05:10:00Z", "2026-10-08T05:50:00Z")));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(["conferenceRecords/c1/recordings/first", "conferenceRecords/c1/recordings/second"], result.Value.Select(r => r.RecordingName));
    }

    // ---- FindRecordingsAsync: guards --------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc-defg-hi")]
    [InlineData("ABC-DEFG-HIJ")]
    [InlineData("abc-defg-hij\" OR space.name = \"x")]
    [InlineData("abc-defg-hij ")]
    [InlineData("abcdefghij")]
    public async Task FindRecordingsAsync_MalformedMeetingCode_FailsWithoutCallingGoogle(string code)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).FindRecordingsAsync(Token, code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tok en")]
    [InlineData("tok\r\nX-Injected: 1")]
    public async Task FindRecordingsAsync_UnusableToken_IsUnauthorizedWithoutCallingGoogle(string token)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).FindRecordingsAsync(token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("abc-defg-hij", true)]
    [InlineData("xyz-abcd-efg", true)]
    [InlineData("abc-defg-hi1", false)]
    [InlineData(null, false)]
    public void IsValidMeetingCode_AcceptsOnlyTheMeetShape(string? code, bool expected)
    {
        Assert.Equal(expected, GoogleMeetRecordingProvider.IsValidMeetingCode(code));
    }

    // ---- error mapping (both list calls and the download) ------------------------------------------------------

    public static TheoryData<HttpStatusCode, string, string, string?> ErrorCases => new()
    {
        { HttpStatusCode.Unauthorized, """{"error":{"code":401,"status":"UNAUTHENTICATED"}}""", GoogleErrors.UnauthorizedCode, "UNAUTHENTICATED" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED"}}""", GoogleErrors.ForbiddenCode, "PERMISSION_DENIED" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"insufficientFilePermissions"}]}}""", GoogleErrors.ForbiddenCode, "insufficientFilePermissions" },
        { HttpStatusCode.Forbidden, "", GoogleErrors.ForbiddenCode, null },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"userRateLimitExceeded"}]}}""", GoogleErrors.TransientCode, "userRateLimitExceeded" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"accessNotConfigured"}]}}""", GoogleErrors.BadRequestCode, "accessNotConfigured" },
        { HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND"}}""", GoogleErrors.NotFoundCode, "NOT_FOUND" },
        { HttpStatusCode.Gone, "", GoogleErrors.NotFoundCode, null },
        { HttpStatusCode.TooManyRequests, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""", GoogleErrors.TransientCode, "RESOURCE_EXHAUSTED" },
        { HttpStatusCode.InternalServerError, "", GoogleErrors.TransientCode, null },
        { HttpStatusCode.BadGateway, "<html>bad gateway</html>", GoogleErrors.TransientCode, null },
        { HttpStatusCode.ServiceUnavailable, """{"error":{"status":"UNAVAILABLE"}}""", GoogleErrors.TransientCode, "UNAVAILABLE" },
        { HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT"}}""", GoogleErrors.BadRequestCode, "INVALID_ARGUMENT" },
        { HttpStatusCode.Redirect, "", GoogleErrors.BadRequestCode, null },
    };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task FindRecordingsAsync_ConferenceListFailure_IsMappedToATypedError(HttpStatusCode status, string body, string code, string? reason)
    {
        var result = await Create(StubHttpHandler.Always(status, body)).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(reason, result.Error.Reason);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task FindRecordingsAsync_RecordingListFailure_IsMappedToATypedError(HttpStatusCode status, string body, string code, string? reason)
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, ConferencesJson(null, ("conferenceRecords/c1", "2026-10-08T05:00:00Z")))
            .Enqueue(status, body);

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(reason, result.Error.Reason);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task OpenDownloadAsync_Failure_IsMappedToATypedError(HttpStatusCode status, string body, string code, string? reason)
    {
        var result = await Create(StubHttpHandler.Always(status, body)).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(reason, result.Error.Reason);
    }

    [Fact]
    public async Task FindRecordingsAsync_NetworkFailure_IsTransient()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new HttpRequestException("connection reset"));

        var result = await Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task FindRecordingsAsync_UnreadableBody_IsTransient()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, "<html>not json</html>")).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task FindRecordingsAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpHandler().EnqueueThrow(new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(handler).FindRecordingsAsync(Token, Code, NotBefore, NotAfter, cts.Token));
    }

    [Fact]
    public async Task EveryOperation_NeverLogsTheTokenTheMeetingCodeOrTheFileId()
    {
        var logger = new CapturingLogger<GoogleMeetRecordingProvider>();
        var failures = new StubHttpHandler()
            .Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED","message":"abc-defg-hij forbidden for file 1AbCdEfGhIjKlMnOpQrStUvWxYz_0123456789"}}""");
        var provider = Create(failures, logger);

        await provider.FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);
        await provider.OpenDownloadAsync(Token, FileId, CancellationToken.None);

        var network = Create(new StubHttpHandler().EnqueueThrow(new HttpRequestException("connection reset")), logger);
        await network.FindRecordingsAsync(Token, Code, NotBefore, NotAfter, CancellationToken.None);
        await network.OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(Token, logger.AllText);
        Assert.DoesNotContain(Code, logger.AllText);
        Assert.DoesNotContain(FileId, logger.AllText);
        // The failure still tells an operator what went wrong: status + Google's reason token.
        Assert.Contains("PERMISSION_DENIED", logger.AllText);
    }

    // ---- OpenDownloadAsync ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenDownloadAsync_RequestsTheMediaOfTheFileAndReportsLengthAndName()
    {
        var bytes = Encoding.UTF8.GetBytes("pretend-this-is-a-video");
        using var handler = new ScriptedHandler(_ =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"class 1.mp4\"" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var download = result.Value;
        Assert.Equal(bytes.Length, download.ContentLength);
        Assert.Equal("class 1.mp4", download.FileName);
        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("www.googleapis.com", request.Uri.Host);
        Assert.Equal($"/drive/v3/files/{FileId}", request.Uri.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("media", query["alt"]);
        Assert.Equal("true", query["supportsAllDrives"]);
        Assert.Equal($"Bearer {Token}", request.Authorization);
    }

    [Fact]
    public async Task OpenDownloadAsync_DoesNotBufferTheBodyBeforeTheCallerReadsIt()
    {
        var tracking = new TrackingStream(new byte[1024 * 1024]);
        using var handler = new ScriptedHandler(_ =>
        {
            var content = new StreamContent(tracking);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            content.Headers.ContentLength = tracking.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        using var download = result.Value;
        Assert.Equal(0, tracking.BytesRead);
        var buffer = new byte[4096];
        var read = await download.Content.ReadAsync(buffer);
        Assert.Equal(buffer.Length, read);
        Assert.Equal(buffer.Length, tracking.BytesRead);
    }

    [Fact]
    public async Task OpenDownloadAsync_DisposingTheDownloadReleasesTheResponse()
    {
        var tracking = new TrackingStream(new byte[16]);
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(tracking) });

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);
        Assert.False(tracking.IsDisposed);

        result.Value.Dispose();

        Assert.True(tracking.IsDisposed);
    }

    [Fact]
    public async Task OpenDownloadAsync_WithoutContentLength_AsksDriveForTheSize()
    {
        var calls = 0;
        using var handler = new ScriptedHandler(request =>
        {
            calls++;
            if (request.RequestUri!.Query.Contains("alt=media", StringComparison.Ordinal))
            {
                // A non-seekable stream gives StreamContent no length: the response is chunked.
                var content = new StreamContent(new NonSeekableStream(new byte[64]));
                content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"size":"1234567890123","name":"recording.mp4"}""", Encoding.UTF8, "application/json"),
            };
        });

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        using var download = result.Value;
        Assert.Equal(1234567890123L, download.ContentLength);
        Assert.Equal("recording.mp4", download.FileName);
        Assert.Equal(2, calls);
        var metadata = handler.Requests[1];
        Assert.Equal($"/drive/v3/files/{FileId}", metadata.Uri.AbsolutePath);
        Assert.Equal("size,name", HttpUtility.ParseQueryString(metadata.Uri.Query)["fields"]);
    }

    [Fact]
    public async Task OpenDownloadAsync_SizeLookupFailing_StillReturnsTheDownloadWithUnknownLength()
    {
        using var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Query.Contains("alt=media", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(new byte[8])) };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var download = result.Value;
        Assert.Null(download.ContentLength);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    public async Task OpenDownloadAsync_NonMediaBody_IsRefusedAsTransient(string contentType)
    {
        var result = await Create(new StubHttpHandler().Enqueue(HttpStatusCode.OK, "<html>sign in</html>", contentType))
            .OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("a/b")]
    [InlineData("id?alt=json")]
    public async Task OpenDownloadAsync_MalformedFileId_FailsWithoutCallingGoogle(string fileId)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).OpenDownloadAsync(Token, fileId, CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenDownloadAsync_UnusableToken_IsUnauthorizedWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).OpenDownloadAsync(" ", FileId, CancellationToken.None);

        Assert.Equal(GoogleErrors.UnauthorizedCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenDownloadAsync_NetworkFailure_IsTransient()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new HttpRequestException("dns"));

        var result = await Create(handler).OpenDownloadAsync(Token, FileId, CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    // ---- ParseTimestamp -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-10-08T05:00:00Z")]
    [InlineData("2026-10-08T12:00:00+07:00")]
    public void ParseTimestamp_ReadsRfc3339InAnyOffsetToUtc(string value)
    {
        var parsed = GoogleMeetRecordingProvider.ParseTimestamp(value);

        Assert.Equal(new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void ParseTimestamp_NanosecondFractions_AreReadWithTheFirstSevenDigits()
    {
        var parsed = GoogleMeetRecordingProvider.ParseTimestamp("2026-10-08T05:00:00.123456789Z");

        Assert.Equal(new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc).AddTicks(1234567), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void ParseTimestamp_AbsentOrUnreadable_IsNull(string? value)
    {
        Assert.Null(GoogleMeetRecordingProvider.ParseTimestamp(value));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    /// <summary>Answers each request from a function and records it. The body is not read, so streamed responses stay untouched.</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
                null,
                null));
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A readable stream that counts what was pulled from it, so a test can prove a body was streamed rather than buffered.</summary>
    private sealed class TrackingStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public long BytesRead { get; private set; }

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
