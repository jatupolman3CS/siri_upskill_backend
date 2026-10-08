using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using Siri.Integrations.Google;

namespace Siri.UnitTests.Google;

public sealed class GoogleCalendarProviderTests
{
    private const string Token = "ya29.calendar-access-token";
    private const string EventsUrl = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    private static readonly DateTime Start = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 8, 5, 30, 0, DateTimeKind.Utc);

    private static CalendarEventRequest Request(string? description = "เข้าห้องเรียนผ่านแพลตฟอร์ม: https://app.example.com/live/abc/join") => new(
        "คอร์ส Excel — คาบที่ 1",
        description,
        Start,
        End,
        "0a1b2c3d4e5f60718293a4b5c6d7e8f9",
        "11112222333344445555666677778888");

    private static GoogleCalendarProvider Create(StubHttpHandler handler, CapturingLogger<GoogleCalendarProvider>? logger = null) =>
        new(new StubHttpClientFactory(handler), logger ?? new CapturingLogger<GoogleCalendarProvider>());

    private static string EventJson(
        string id = "ev123",
        string? hangoutLink = "https://meet.google.com/abc-defg-hij",
        string? pendingStatus = null,
        string? status = null,
        string? extra = null)
    {
        var node = new JsonObject { ["id"] = id, ["kind"] = "calendar#event" };
        if (status is not null)
        {
            node["status"] = status;
        }

        if (hangoutLink is not null)
        {
            node["hangoutLink"] = hangoutLink;
        }

        if (pendingStatus is not null)
        {
            node["conferenceData"] = new JsonObject
            {
                ["createRequest"] = new JsonObject
                {
                    ["requestId"] = "r",
                    ["status"] = new JsonObject { ["statusCode"] = pendingStatus },
                },
            };
        }

        var json = node.ToJsonString();
        return extra is null ? json : json[..^1] + "," + extra + "}";
    }

    // ---- CreateEventWithMeetAsync ------------------------------------------------------------------------------

    [Fact]
    public async Task CreateEventWithMeetAsync_SendsTheDocumentedInsertRequest()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson());

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{EventsUrl}?conferenceDataVersion=1&sendUpdates=none", request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        Assert.Equal("application/json", request.ContentType);

        var body = request.JsonBody;
        Assert.Equal("คอร์ส Excel — คาบที่ 1", (string?)body["summary"]);
        Assert.Equal("เข้าห้องเรียนผ่านแพลตฟอร์ม: https://app.example.com/live/abc/join", (string?)body["description"]);
        Assert.Equal("2026-10-08T03:00:00Z", (string?)body["start"]!["dateTime"]);
        Assert.Equal("Asia/Bangkok", (string?)body["start"]!["timeZone"]);
        Assert.Equal("2026-10-08T05:30:00Z", (string?)body["end"]!["dateTime"]);
        Assert.Equal("Asia/Bangkok", (string?)body["end"]!["timeZone"]);

        var createRequest = body["conferenceData"]!["createRequest"]!;
        Assert.Equal("0a1b2c3d4e5f60718293a4b5c6d7e8f9", (string?)createRequest["requestId"]);
        Assert.Equal("hangoutsMeet", (string?)createRequest["conferenceSolutionKey"]!["type"]);

        Assert.False((bool)body["guestsCanModify"]!);
        Assert.False((bool)body["guestsCanInviteOthers"]!);
        Assert.False((bool)body["guestsCanSeeOtherGuests"]!);
        Assert.False((bool)body["anyoneCanAddSelf"]!);
        Assert.Equal("11112222333344445555666677778888", (string?)body["extendedProperties"]!["private"]!["siriSessionId"]);
        Assert.Null(body["attendees"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CalendarEventResult("ev123", "https://meet.google.com/abc-defg-hij", false), result.Value);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_NullDescription_OmitsTheMember()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson());

        await Create(handler).CreateEventWithMeetAsync(Token, Request(description: null), CancellationToken.None);

        Assert.Null(handler.Requests[0].JsonBody["description"]);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_NonUtcKinds_AreNormalisedToUtc()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson());
        var unspecified = new CalendarEventRequest("s", null, new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Unspecified), End, "r", "p");

        await Create(handler).CreateEventWithMeetAsync(Token, unspecified, CancellationToken.None);

        Assert.Equal("2026-10-08T03:00:00Z", (string?)handler.Requests[0].JsonBody["start"]!["dateTime"]);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_ConferencePending_ReportsPendingWithoutUrl()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson(hangoutLink: null, pendingStatus: "pending"));

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CalendarEventResult("ev123", null, true), result.Value);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_ConferenceFailure_IsNotPendingAndHasNoUrl()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson(hangoutLink: null, pendingStatus: "failure"));

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CalendarEventResult("ev123", null, false), result.Value);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_ReadsTheVideoEntryPointWhenThereIsNoHangoutLink()
    {
        var entryPoints = """
            "conferenceData":{"entryPoints":[
              {"entryPointType":"phone","uri":"tel:+66-2-000-0000"},
              {"entryPointType":"video","uri":"https://meet.google.com/xyz-abcd-efg"}]}
            """;
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson(hangoutLink: null, extra: entryPoints));

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.Equal("https://meet.google.com/xyz-abcd-efg", result.Value.MeetUrl);
        Assert.False(result.Value.ConferencePending);
    }

    [Theory]
    [InlineData("http://meet.google.com/abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task CreateEventWithMeetAsync_NonHttpsMeetLink_IsIgnored(string link)
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson(hangoutLink: link));

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MeetUrl);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_ResponseWithoutId_IsTransient()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, """{"kind":"calendar#event"}"""))
            .CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Theory]
    [InlineData("", "r", "p")]
    [InlineData("s", "", "p")]
    [InlineData("s", "r", " ")]
    public async Task CreateEventWithMeetAsync_MissingIdentifiers_IsBadRequestWithoutCallingGoogle(string summary, string requestId, string sessionId)
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).CreateEventWithMeetAsync(Token, new CalendarEventRequest(summary, null, Start, End, requestId, sessionId), CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateEventWithMeetAsync_EndNotAfterStart_IsBadRequestWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).CreateEventWithMeetAsync(Token, new CalendarEventRequest("s", null, End, End, "r", "p"), CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    // ---- FindEventByPrivateSessionIdAsync ----------------------------------------------------------------------

    [Fact]
    public async Task FindEventByPrivateSessionIdAsync_QueriesByPrivateExtendedProperty()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, $$"""{"items":[{{EventJson("found1")}}]}""");

        var result = await Create(handler).FindEventByPrivateSessionIdAsync(Token, "11112222333344445555666677778888", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith($"{EventsUrl}?", request.Uri.AbsoluteUri);
        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("siriSessionId=11112222333344445555666677778888", query["privateExtendedProperty"]);
        Assert.Equal("1", query["maxResults"]);
        Assert.Equal("false", query["showDeleted"]);
        Assert.Equal(3, query.Count);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        Assert.Null(request.Body);

        Assert.True(result.IsSuccess);
        Assert.Equal("found1", result.Value!.EventId);
    }

    [Theory]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"kind":"calendar#events"}""")]
    public async Task FindEventByPrivateSessionIdAsync_NoItems_IsSuccessWithNull(string body)
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, body)).FindEventByPrivateSessionIdAsync(Token, "p", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindEventByPrivateSessionIdAsync_SkipsCancelledEvents()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, $$"""{"items":[{{EventJson("gone", status: "cancelled")}}]}""");

        var result = await Create(handler).FindEventByPrivateSessionIdAsync(Token, "p", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindEventByPrivateSessionIdAsync_BlankSessionId_IsBadRequestWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();

        var result = await Create(handler).FindEventByPrivateSessionIdAsync(Token, " ", CancellationToken.None);

        Assert.Equal(GoogleErrors.BadRequestCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    // ---- GetEventAsync -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEventAsync_GetsTheEventById()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev9", hangoutLink: "https://meet.google.com/aaa-bbbb-ccc"));

        var result = await Create(handler).GetEventAsync(Token, "ev9", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{EventsUrl}/ev9", request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        Assert.Equal(new CalendarEventResult("ev9", "https://meet.google.com/aaa-bbbb-ccc", false), result.Value);
    }

    [Fact]
    public async Task GetEventAsync_EscapesTheEventIdInThePath()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("a/b"));

        await Create(handler).GetEventAsync(Token, "a/b?x=1", CancellationToken.None);

        Assert.Equal($"{EventsUrl}/a%2Fb%3Fx%3D1", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetEventAsync_404Or410_IsNotFound(HttpStatusCode status)
    {
        var result = await Create(StubHttpHandler.Always(status, """{"error":{"code":404,"errors":[{"reason":"notFound"}]}}""")).GetEventAsync(Token, "x", CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task GetEventAsync_CancelledEvent_IsNotFound()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, EventJson("x", status: "cancelled"))).GetEventAsync(Token, "x", CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task GetEventAsync_StillPending_ReportsPending()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, EventJson(hangoutLink: null, pendingStatus: "pending"))).GetEventAsync(Token, "ev123", CancellationToken.None);

        Assert.True(result.Value.ConferencePending);
        Assert.Null(result.Value.MeetUrl);
    }

    // ---- UpdateEventAsync --------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateEventAsync_PatchesOnlyTheFieldsTheCallerOwns()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev123"));

        var result = await Create(handler).UpdateEventAsync(Token, "ev123", Request(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{EventsUrl}/ev123?conferenceDataVersion=1&sendUpdates=none", request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", request.Authorization);

        var body = request.JsonBody;
        Assert.Equal(["description", "end", "start", "summary"], body.Select(p => p.Key).Order().ToArray());
        Assert.Equal("2026-10-08T03:00:00Z", (string?)body["start"]!["dateTime"]);
        Assert.Equal("Asia/Bangkok", (string?)body["start"]!["timeZone"]);
        Assert.Null(body["conferenceData"]);
        Assert.Null(body["extendedProperties"]);
        Assert.Null(body["attendees"]);
        Assert.True(result.IsSuccess);
        Assert.Equal("ev123", result.Value.EventId);
    }

    [Fact]
    public async Task UpdateEventAsync_NullDescription_ClearsItWithAnEmptyString()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev123"));

        await Create(handler).UpdateEventAsync(Token, "ev123", Request(description: null), CancellationToken.None);

        Assert.Equal(string.Empty, (string?)handler.Requests[0].JsonBody["description"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task UpdateEventAsync_EventGone_IsNotFoundSoTheCallerCanRecreateIt(HttpStatusCode status)
    {
        var result = await Create(StubHttpHandler.Always(status, "")).UpdateEventAsync(Token, "gone", Request(), CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
    }

    // ---- DeleteEventAsync --------------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteEventAsync_SendsDeleteWithSendUpdatesNone()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.NoContent);

        var result = await Create(handler).DeleteEventAsync(Token, "ev123", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{EventsUrl}/ev123?sendUpdates=none", request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", request.Authorization);
        Assert.Null(request.Body);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task DeleteEventAsync_SuccessOrAlreadyGone_IsSuccess(HttpStatusCode status)
    {
        var result = await Create(StubHttpHandler.Always(status, "")).DeleteEventAsync(Token, "ev", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", GoogleErrors.UnauthorizedCode)]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"errors":[{"reason":"insufficientPermissions"}]}}""", GoogleErrors.UnauthorizedCode)]
    [InlineData(HttpStatusCode.TooManyRequests, "", GoogleErrors.RateLimitedCode)]
    [InlineData(HttpStatusCode.InternalServerError, "", GoogleErrors.TransientCode)]
    public async Task DeleteEventAsync_Failures_MapToTheContractCodes(HttpStatusCode status, string body, string expectedCode)
    {
        var result = await Create(StubHttpHandler.Always(status, body)).DeleteEventAsync(Token, "ev", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    // ---- SetAttendeesAsync -------------------------------------------------------------------------------------

    private static string EventWithAttendees(string attendeesJson, string? status = null) =>
        EventJson("ev123", extra: $"\"attendees\":{attendeesJson}" + (status is null ? string.Empty : $",\"status\":\"{status}\""));

    [Fact]
    public async Task SetAttendeesAsync_ReadsTheEventThenPatchesTheWholeArrayWithSendUpdatesNone()
    {
        var existing = EventWithAttendees("""
            [{"email":"teacher@example.com","organizer":true,"self":true,"responseStatus":"accepted"},
             {"email":"keep@example.com","responseStatus":"accepted","id":"123"},
             {"email":"drop@example.com","responseStatus":"needsAction"}]
            """);
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, existing)
            .Enqueue(HttpStatusCode.OK, existing);

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", ["Keep@Example.com", "new@example.com"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);

        var get = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal($"{EventsUrl}/ev123", get.Uri.AbsoluteUri);

        var patch = handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal($"{EventsUrl}/ev123?sendUpdates=none", patch.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {Token}", patch.Authorization);

        var body = patch.JsonBody;
        Assert.Equal(["attendees"], body.Select(p => p.Key).ToArray());
        var attendees = body["attendees"]!.AsArray();
        Assert.Equal(3, attendees.Count);

        // organizer entry preserved (without the read-only flags)
        Assert.Equal("teacher@example.com", (string?)attendees[0]!["email"]);
        Assert.Equal("accepted", (string?)attendees[0]!["responseStatus"]);
        Assert.Null(attendees[0]!["organizer"]);
        Assert.Null(attendees[0]!["self"]);

        // existing wanted guest keeps its response status, and its stored spelling (case-insensitive match)
        Assert.Equal("keep@example.com", (string?)attendees[1]!["email"]);
        Assert.Equal("accepted", (string?)attendees[1]!["responseStatus"]);
        Assert.Null(attendees[1]!["id"]);

        // new guest is just { email }
        Assert.Equal(["email"], attendees[2]!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal("new@example.com", (string?)attendees[2]!["email"]);

        // the dropped guest is gone
        Assert.DoesNotContain("drop@example.com", patch.Body);
    }

    [Fact]
    public async Task SetAttendeesAsync_NoChange_SkipsThePatch()
    {
        var existing = EventWithAttendees("""[{"email":"teacher@example.com","organizer":true},{"email":"a@example.com","responseStatus":"accepted"}]""");
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, existing);

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", ["A@example.com"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task SetAttendeesAsync_EmptyListOnAnEventWithoutGuests_MakesNoPatch()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev123"));

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SetAttendeesAsync_EmptyList_RemovesEveryoneButTheOrganizer()
    {
        var existing = EventWithAttendees("""[{"email":"teacher@example.com","organizer":true},{"email":"a@example.com"}]""");
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, existing);

        await Create(handler).SetAttendeesAsync(Token, "ev123", [], CancellationToken.None);

        var attendees = handler.Requests[1].JsonBody["attendees"]!.AsArray();
        Assert.Equal("teacher@example.com", (string?)Assert.Single(attendees)!["email"]);
    }

    [Fact]
    public async Task SetAttendeesAsync_NormalisesInput_DedupesAndSkipsMalformed()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev123"));

        await Create(handler).SetAttendeesAsync(
            Token,
            "ev123",
            [" a@example.com ", "A@EXAMPLE.COM", "", "not-an-email", "a@@example.com", "x y@example.com", "b@example.com,c@example.com", "<d@example.com>"],
            CancellationToken.None);

        var attendees = handler.Requests[1].JsonBody["attendees"]!.AsArray();
        Assert.Equal("a@example.com", (string?)Assert.Single(attendees)!["email"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SetAttendeesAsync_EventGone_IsNotFoundAndDoesNotPatch(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Always(status, "");

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", ["a@example.com"], CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SetAttendeesAsync_CancelledEvent_IsNotFoundAndDoesNotPatch()
    {
        var handler = StubHttpHandler.Always(HttpStatusCode.OK, EventJson("ev123", status: "cancelled"));

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", ["a@example.com"], CancellationToken.None);

        Assert.Equal(GoogleErrors.NotFoundCode, result.Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SetAttendeesAsync_PatchFailure_MapsTheError()
    {
        var handler = new StubHttpHandler()
            .Enqueue(HttpStatusCode.OK, EventJson("ev123"))
            .Enqueue(HttpStatusCode.TooManyRequests, "");

        var result = await Create(handler).SetAttendeesAsync(Token, "ev123", ["a@example.com"], CancellationToken.None);

        Assert.Equal(GoogleErrors.RateLimitedCode, result.Error.Code);
    }

    // ---- error mapping (every method shares it) ----------------------------------------------------------------

    public static TheoryData<HttpStatusCode, string, string, string?> CalendarErrorCases => new()
    {
        { HttpStatusCode.Unauthorized, """{"error":{"code":401,"errors":[{"reason":"authError"}],"status":"UNAUTHENTICATED"}}""", GoogleErrors.UnauthorizedCode, "authError" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"insufficientPermissions"}]}}""", GoogleErrors.UnauthorizedCode, "insufficientPermissions" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"forbidden"}]}}""", GoogleErrors.UnauthorizedCode, "forbidden" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"ACCESS_TOKEN_SCOPE_INSUFFICIENT"}]}}""", GoogleErrors.UnauthorizedCode, "ACCESS_TOKEN_SCOPE_INSUFFICIENT" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""", GoogleErrors.RateLimitedCode, "rateLimitExceeded" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"userRateLimitExceeded"}]}}""", GoogleErrors.RateLimitedCode, "userRateLimitExceeded" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}""", GoogleErrors.RateLimitedCode, "quotaExceeded" },
        { HttpStatusCode.Forbidden, """{"error":{"code":403,"errors":[{"reason":"notACalendarUser"}]}}""", GoogleErrors.BadRequestCode, "notACalendarUser" },
        { HttpStatusCode.Forbidden, "", GoogleErrors.BadRequestCode, null },
        { HttpStatusCode.NotFound, """{"error":{"code":404,"errors":[{"reason":"notFound"}]}}""", GoogleErrors.NotFoundCode, "notFound" },
        { HttpStatusCode.Gone, """{"error":{"code":410,"errors":[{"reason":"deleted"}]}}""", GoogleErrors.NotFoundCode, "deleted" },
        { HttpStatusCode.TooManyRequests, """{"error":{"code":429,"errors":[{"reason":"rateLimitExceeded"}]}}""", GoogleErrors.RateLimitedCode, "rateLimitExceeded" },
        { HttpStatusCode.BadRequest, """{"error":{"code":400,"errors":[{"reason":"invalid"}]}}""", GoogleErrors.BadRequestCode, "invalid" },
        { HttpStatusCode.Conflict, "", GoogleErrors.BadRequestCode, null },
        { HttpStatusCode.Found, "", GoogleErrors.BadRequestCode, null },
        { HttpStatusCode.RequestTimeout, "", GoogleErrors.TransientCode, null },
        { HttpStatusCode.InternalServerError, "", GoogleErrors.TransientCode, null },
        { HttpStatusCode.BadGateway, "", GoogleErrors.TransientCode, null },
        { HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"errors":[{"reason":"backendError"}]}}""", GoogleErrors.TransientCode, "backendError" },
        { HttpStatusCode.GatewayTimeout, "", GoogleErrors.TransientCode, null },
    };

    [Theory]
    [MemberData(nameof(CalendarErrorCases))]
    public async Task EveryCalendarCall_MapsErrorResponsesToTheContractCodes(HttpStatusCode status, string body, string expectedCode, string? expectedReason)
    {
        var provider = Create(StubHttpHandler.Always(status, body));
        var results = new Siri.SharedKernel.Result[]
        {
            await provider.CreateEventWithMeetAsync(Token, Request(), CancellationToken.None),
            await provider.FindEventByPrivateSessionIdAsync(Token, "p", CancellationToken.None),
            await provider.GetEventAsync(Token, "e", CancellationToken.None),
            await provider.UpdateEventAsync(Token, "e", Request(), CancellationToken.None),
            await provider.SetAttendeesAsync(Token, "e", ["a@example.com"], CancellationToken.None),
        };

        Assert.All(results, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(expectedCode, result.Error.Code);
            Assert.Equal(expectedReason, result.Error.Reason);
        });
    }

    [Fact]
    public async Task ErrorMessages_NeverEchoGoogleFreeText()
    {
        var body = """{"error":{"code":403,"message":"The user someone@example.com may not edit https://meet.google.com/abc-defg-hij","errors":[{"reason":"notACalendarUser","message":"leak@example.com"}]}}""";

        var result = await Create(StubHttpHandler.Always(HttpStatusCode.Forbidden, body)).GetEventAsync(Token, "e", CancellationToken.None);

        Assert.DoesNotContain("someone@example.com", result.Error.Message);
        Assert.DoesNotContain("leak@example.com", result.Error.Message);
        Assert.DoesNotContain("meet.google.com", result.Error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad token")]
    [InlineData("a\r\nb")]
    public async Task EveryCalendarCall_UnusableToken_IsUnauthorizedWithoutCallingGoogle(string token)
    {
        var handler = new StubHttpHandler();
        var provider = Create(handler);
        var results = new Siri.SharedKernel.Result[]
        {
            await provider.CreateEventWithMeetAsync(token, Request(), CancellationToken.None),
            await provider.FindEventByPrivateSessionIdAsync(token, "p", CancellationToken.None),
            await provider.GetEventAsync(token, "e", CancellationToken.None),
            await provider.UpdateEventAsync(token, "e", Request(), CancellationToken.None),
            await provider.DeleteEventAsync(token, "e", CancellationToken.None),
            await provider.SetAttendeesAsync(token, "e", ["a@example.com"], CancellationToken.None),
        };

        Assert.All(results, r => Assert.Equal(GoogleErrors.UnauthorizedCode, r.Error.Code));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EveryCalendarCall_BlankEventId_IsBadRequestWithoutCallingGoogle()
    {
        var handler = new StubHttpHandler();
        var provider = Create(handler);
        var results = new Siri.SharedKernel.Result[]
        {
            await provider.GetEventAsync(Token, " ", CancellationToken.None),
            await provider.UpdateEventAsync(Token, "", Request(), CancellationToken.None),
            await provider.DeleteEventAsync(Token, "", CancellationToken.None),
            await provider.SetAttendeesAsync(Token, " ", ["a@example.com"], CancellationToken.None),
        };

        Assert.All(results, r => Assert.Equal(GoogleErrors.BadRequestCode, r.Error.Code));
        Assert.Empty(handler.Requests);
    }

    // ---- transport failures ------------------------------------------------------------------------------------

    [Fact]
    public async Task NetworkFailure_IsTransientNotAnException()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new HttpRequestException("connection reset"));

        var result = await Create(handler).CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task HttpClientTimeout_IsTransient()
    {
        var handler = new StubHttpHandler().EnqueueThrow(new TaskCanceledException("timeout"));

        var result = await Create(handler).DeleteEventAsync(Token, "ev", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpHandler().EnqueueThrow(new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).GetEventAsync(Token, "ev", cts.Token));
    }

    [Fact]
    public async Task MalformedSuccessBody_IsTransient()
    {
        var result = await Create(StubHttpHandler.Always(HttpStatusCode.OK, "{broken")).GetEventAsync(Token, "ev", CancellationToken.None);

        Assert.Equal(GoogleErrors.TransientCode, result.Error.Code);
    }

    // ---- logging -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tokens_Emails_AndMeetUrls_NeverReachTheLogs()
    {
        var logger = new CapturingLogger<GoogleCalendarProvider>();
        const string meetUrl = "https://meet.google.com/secret-room-abc";
        const string email = "learner.private@example.com";
        var errorBody = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = 403,
                ["message"] = $"{email} {meetUrl} {Token}",
                ["errors"] = new JsonArray(new JsonObject { ["reason"] = "notACalendarUser", ["message"] = email }),
            },
        }.ToJsonString();

        foreach (var (status, body) in new[]
        {
            (HttpStatusCode.OK, EventJson("ev1", hangoutLink: meetUrl, extra: $"\"attendees\":[{{\"email\":\"{email}\"}}]")),
            (HttpStatusCode.Forbidden, errorBody),
            (HttpStatusCode.Unauthorized, errorBody),
            (HttpStatusCode.InternalServerError, errorBody),
        })
        {
            var provider = Create(StubHttpHandler.Always(status, body), logger);
            await provider.CreateEventWithMeetAsync(Token, Request(), CancellationToken.None);
            await provider.GetEventAsync(Token, "ev1", CancellationToken.None);
            await provider.UpdateEventAsync(Token, "ev1", Request(), CancellationToken.None);
            await provider.DeleteEventAsync(Token, "ev1", CancellationToken.None);
            await provider.SetAttendeesAsync(Token, "ev1", [email, "not-an-email"], CancellationToken.None);
            await provider.FindEventByPrivateSessionIdAsync(Token, "p", CancellationToken.None);
        }

        await Create(new StubHttpHandler().EnqueueThrow(new HttpRequestException("boom")), logger)
            .SetAttendeesAsync(Token, "ev1", [email], CancellationToken.None);

        var logs = logger.AllText;
        Assert.NotEmpty(logger.Lines);
        foreach (var secret in new[] { Token, email, meetUrl, "secret-room-abc", "not-an-email" })
        {
            Assert.DoesNotContain(secret, logs);
        }
    }
}
