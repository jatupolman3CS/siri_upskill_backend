using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Infrastructure.Search;

namespace Siri.UnitTests.Catalog.Search;

/// <summary>
/// <see cref="MeilisearchCourseSearchIndex"/> against a stub <see cref="HttpMessageHandler"/>: every request it sends (method, path, auth header, JSON body)
/// is asserted against Meilisearch's REST API, and every way the engine can fail is shown to degrade the way the contract promises — search returns null and
/// the caller falls back, maintenance calls throw <see cref="CourseSearchIndexException"/>. No network, no Meilisearch needed.
/// </summary>
public sealed class MeilisearchCourseSearchIndexTests
{
    private const string ApiKey = "test-master-key-1234567890";
    private const string Uid = "docs_test";
    private static readonly DateTime Start = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Recorded(HttpMethod Method, string PathAndQuery, string Body, AuthenticationHeaderValue? Authorization)
    {
        public JsonElement Json() => JsonDocument.Parse(Body).RootElement;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        public Func<Recorded, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
            (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, "{}"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new Recorded(request.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization);
            Requests.Add(recorded);
            return await Responder(recorded, cancellationToken);
        }

        public List<string> Calls => Requests.Select(r => $"{r.Method} {r.PathAndQuery}").ToList();
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Harness
    {
        public required MeilisearchCourseSearchIndex Index { get; init; }

        public required StubHandler Handler { get; init; }

        public required MeilisearchAvailability Availability { get; init; }

        public required FakeClock Clock { get; init; }
    }

    private static Harness Create(Action<MeilisearchOptions>? configure = null)
    {
        var options = new MeilisearchOptions
        {
            Url = "http://meili.test:7700",
            ApiKey = ApiKey,
            DocumentsIndexUid = Uid,
            TaskWaitTimeoutSeconds = 5,
        };
        configure?.Invoke(options);

        var wrapped = Options.Create(options);
        var clock = new FakeClock(Start);
        var availability = new MeilisearchAvailability(clock, wrapped);
        var handler = new StubHandler();
        var http = new HttpClient(handler) { BaseAddress = options.GetBaseAddress() };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        return new Harness
        {
            Index = new MeilisearchCourseSearchIndex(http, wrapped, availability, NullLogger<MeilisearchCourseSearchIndex>.Instance),
            Handler = handler,
            Availability = availability,
            Clock = clock,
        };
    }

    private static CourseSearchDocument Document(string title = "Python") =>
        CourseSearchDocument.ForCourse(
            Guid.NewGuid(), "slug", title, null, null, Guid.NewGuid(), "Somchai", null, Guid.NewGuid(), null, null);

    private static string Hits(params Guid[] ids) =>
        "{\"hits\":[" + string.Join(",", ids.Select(id => $"{{\"courseId\":\"{id}\"}}")) + "],\"estimatedTotalHits\":" + ids.Length + "}";

    // ---- search ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchCourseIds_SendsAuthenticatedFilteredQuery_AndReturnsIdsInEngineOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Hits(second, first)));

        var ids = await h.Index.SearchCourseIdsAsync("  สมชาย  ", CancellationToken.None);

        Assert.Equal([second, first], ids);
        var request = Assert.Single(h.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/indexes/{Uid}/search", request.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(ApiKey, request.Authorization?.Parameter);

        var body = request.Json();
        Assert.Equal("สมชาย", body.GetProperty("q").GetString());
        Assert.Equal("type = \"course\"", body.GetProperty("filter").GetString());
        Assert.Equal(1000, body.GetProperty("limit").GetInt32());
        Assert.Equal(["courseId"], body.GetProperty("attributesToRetrieve").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task SearchCourseIds_NoMatches_ReturnsEmptyListNotNull()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Hits()));

        var ids = await h.Index.SearchCourseIdsAsync("zzzz", CancellationToken.None);

        Assert.NotNull(ids);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task SearchCourseIds_IgnoresDuplicateAndMalformedHits()
    {
        var id = Guid.NewGuid();
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(
            HttpStatusCode.OK,
            $"{{\"hits\":[{{\"courseId\":\"{id}\"}},{{\"courseId\":\"not-a-guid\"}},{{\"other\":1}},{{\"courseId\":\"{id}\"}},{{\"courseId\":null}}]}}"));

        var ids = await h.Index.SearchCourseIdsAsync("x", CancellationToken.None);

        Assert.Equal([id], ids);
    }

    [Fact]
    public async Task SearchCourseIds_OverlongQuery_IsTruncated()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Hits()));

        await h.Index.SearchCourseIdsAsync(new string('a', 5_000), CancellationToken.None);

        Assert.Equal(200, h.Handler.Requests.Single().Json().GetProperty("q").GetString()!.Length);
    }

    [Fact]
    public async Task SearchCourseIds_WhenNotConfigured_ReturnsNullWithoutCallingTheEngine()
    {
        var h = Create(o => o.Url = string.Empty);

        Assert.False(h.Index.IsEnabled);
        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
        Assert.Empty(h.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{\"message\":\"boom\",\"code\":\"internal\"}")]
    [InlineData(HttpStatusCode.Unauthorized, "{\"message\":\"The provided API key is invalid.\",\"code\":\"invalid_api_key\"}")]
    [InlineData(HttpStatusCode.NotFound, "{\"message\":\"Index not found.\",\"code\":\"index_not_found\"}")]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    [InlineData(HttpStatusCode.OK, "{\"unexpected\":true}")]
    public async Task SearchCourseIds_EngineRejectsOrReturnsGarbage_ReturnsNullSoTheCallerFallsBack(HttpStatusCode status, string body)
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(status, body));

        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCourseIds_NetworkFailure_ReturnsNull()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => throw new HttpRequestException("connection refused");

        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCourseIds_HttpClientTimeout_ReturnsNull()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCourseIds_SlowEngine_IsCutOffAtTheSearchTimeout_AndFallsBack()
    {
        var h = Create(o => o.SearchTimeoutMilliseconds = 50);
        h.Handler.Responder = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Reply(HttpStatusCode.OK, Hits());
        };

        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
    }

    [Fact]
    public async Task SearchCourseIds_CallerCancellation_PropagatesInsteadOfBeingSwallowedAsAnEngineFailure()
    {
        var h = Create();
        h.Handler.Responder = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Reply(HttpStatusCode.OK, Hits());
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Index.SearchCourseIdsAsync("python", cts.Token));
    }

    // ---- circuit breaker ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchCourseIds_RepeatedFailures_OpenTheCircuit_SoAnOutageCostsOneTimeoutPerWindow()
    {
        var h = Create(o =>
        {
            o.CircuitBreakerFailureThreshold = 3;
            o.CircuitOpenSeconds = 30;
        });
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.ServiceUnavailable, "{}"));

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
        }

        Assert.Equal(3, h.Handler.Requests.Count);
        Assert.True(h.Availability.IsCircuitOpen);

        // Open: no further request reaches the engine.
        Assert.Null(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
        Assert.Equal(3, h.Handler.Requests.Count);

        // After the window the next search is let through again…
        h.Clock.UtcNow = Start.AddSeconds(31);
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Hits()));
        Assert.NotNull(await h.Index.SearchCourseIdsAsync("python", CancellationToken.None));
        Assert.Equal(4, h.Handler.Requests.Count);

        // …and one success closes the circuit for good.
        Assert.False(h.Availability.IsCircuitOpen);
    }

    [Fact]
    public async Task SearchCourseIds_OneFailureBelowTheThreshold_DoesNotOpenTheCircuit()
    {
        var h = Create(o => o.CircuitBreakerFailureThreshold = 3);
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.BadGateway, "{}"));
        await h.Index.SearchCourseIdsAsync("python", CancellationToken.None);

        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, Hits()));
        await h.Index.SearchCourseIdsAsync("python", CancellationToken.None);
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.BadGateway, "{}"));
        await h.Index.SearchCourseIdsAsync("python", CancellationToken.None);
        await h.Index.SearchCourseIdsAsync("python", CancellationToken.None);

        // Failures were never consecutive three times (a success reset the count), so the circuit stayed closed.
        Assert.False(h.Availability.IsCircuitOpen);
    }

    // ---- index + settings -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureIndex_MissingIndex_CreatesItThenAppliesSettingsAndWaitsForBothTasks()
    {
        var h = Create();
        h.Handler.Responder = (request, _) => Task.FromResult(request switch
        {
            { Method.Method: "GET", PathAndQuery: $"/indexes/{Uid}" } => Reply(HttpStatusCode.NotFound, "{\"code\":\"index_not_found\",\"message\":\"not found\"}"),
            { Method.Method: "POST", PathAndQuery: "/indexes" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":10,\"status\":\"enqueued\"}"),
            { Method.Method: "PATCH" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":11,\"status\":\"enqueued\"}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/10" } => Reply(HttpStatusCode.OK, "{\"uid\":10,\"status\":\"succeeded\"}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/11" } => Reply(HttpStatusCode.OK, "{\"uid\":11,\"status\":\"succeeded\"}"),
            _ => Reply(HttpStatusCode.BadRequest, "{}"),
        });

        await h.Index.EnsureIndexAsync(CancellationToken.None);

        Assert.Equal(
            [$"GET /indexes/{Uid}", "POST /indexes", "GET /tasks/10", $"PATCH /indexes/{Uid}/settings", "GET /tasks/11"],
            h.Handler.Calls);

        var create = h.Handler.Requests[1].Json();
        Assert.Equal(Uid, create.GetProperty("uid").GetString());
        Assert.Equal("id", create.GetProperty("primaryKey").GetString());

        var settings = h.Handler.Requests[3].Json();
        var searchable = settings.GetProperty("searchableAttributes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal("title", searchable[0]);
        Assert.Contains("instructorName", searchable);
        Assert.Contains("description", searchable);
        Assert.Equal(["type", "instructorId", "categoryId"], settings.GetProperty("filterableAttributes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1000, settings.GetProperty("pagination").GetProperty("maxTotalHits").GetInt32());
        Assert.True(h.Availability.IndexEnsured);
    }

    [Fact]
    public async Task EnsureIndex_ExistingIndex_OnlyReappliesSettings()
    {
        var h = Create();
        h.Handler.Responder = (request, _) => Task.FromResult(request switch
        {
            { Method.Method: "GET", PathAndQuery: $"/indexes/{Uid}" } => Reply(HttpStatusCode.OK, $"{{\"uid\":\"{Uid}\",\"primaryKey\":\"id\"}}"),
            { Method.Method: "PATCH" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":7}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/7" } => Reply(HttpStatusCode.OK, "{\"status\":\"succeeded\"}"),
            _ => Reply(HttpStatusCode.BadRequest, "{}"),
        });

        await h.Index.EnsureIndexAsync(CancellationToken.None);

        Assert.Equal([$"GET /indexes/{Uid}", $"PATCH /indexes/{Uid}/settings", "GET /tasks/7"], h.Handler.Calls);
    }

    [Fact]
    public async Task EnsureIndex_CreationLostARaceToAnotherInstance_IsNotAnError()
    {
        var h = Create();
        var getCount = 0;
        h.Handler.Responder = (request, _) => Task.FromResult(request switch
        {
            // First GET: missing. After the (failed) creation task the index is there, because the other instance created it.
            { Method.Method: "GET", PathAndQuery: $"/indexes/{Uid}" } => ++getCount == 1
                ? Reply(HttpStatusCode.NotFound, "{\"code\":\"index_not_found\"}")
                : Reply(HttpStatusCode.OK, "{}"),
            { Method.Method: "POST", PathAndQuery: "/indexes" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":1}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/1" } => Reply(
                HttpStatusCode.OK, "{\"status\":\"failed\",\"error\":{\"code\":\"index_already_exists\",\"message\":\"exists\"}}"),
            { Method.Method: "PATCH" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":2}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/2" } => Reply(HttpStatusCode.OK, "{\"status\":\"succeeded\"}"),
            _ => Reply(HttpStatusCode.BadRequest, "{}"),
        });

        await h.Index.EnsureIndexAsync(CancellationToken.None);

        Assert.True(h.Availability.IndexEnsured);
    }

    [Fact]
    public async Task EnsureIndex_FailedSettingsTask_Throws_AndDoesNotMarkTheIndexReady()
    {
        var h = Create();
        h.Handler.Responder = (request, _) => Task.FromResult(request switch
        {
            { Method.Method: "GET", PathAndQuery: $"/indexes/{Uid}" } => Reply(HttpStatusCode.OK, "{}"),
            { Method.Method: "PATCH" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":3}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/3" } => Reply(
                HttpStatusCode.OK, "{\"status\":\"failed\",\"error\":{\"code\":\"invalid_settings_searchable_attributes\",\"message\":\"nope\"}}"),
            _ => Reply(HttpStatusCode.BadRequest, "{}"),
        });

        var ex = await Assert.ThrowsAsync<CourseSearchIndexException>(() => h.Index.EnsureIndexAsync(CancellationToken.None));

        Assert.Contains("invalid_settings_searchable_attributes", ex.Message, StringComparison.Ordinal);
        Assert.False(h.Availability.IndexEnsured);
    }

    [Fact]
    public async Task EnsureIndex_WhenNotConfigured_DoesNothing()
    {
        var h = Create(o => o.ApiKey = "CHANGE_ME");

        await h.Index.EnsureIndexAsync(CancellationToken.None);

        Assert.Empty(h.Handler.Requests);
    }

    // ---- documents ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_SplitsIntoBatches_PostsToTheDocumentsEndpointWithPrimaryKey_AndWaitsWhenAsked()
    {
        var taskUid = 100;
        var h = Create(o => o.IndexBatchSize = 2);
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (request, _) => Task.FromResult(
            request.Method == HttpMethod.Post
                ? Reply(HttpStatusCode.Accepted, $"{{\"taskUid\":{taskUid++}}}")
                : Reply(HttpStatusCode.OK, "{\"status\":\"succeeded\"}"));
        var documents = Enumerable.Range(0, 5).Select(i => Document($"Course {i}")).ToList();

        await h.Index.UpsertCoursesAsync(documents, waitForCompletion: true, CancellationToken.None);

        var posts = h.Handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(3, posts.Count);
        Assert.All(posts, p => Assert.Equal($"/indexes/{Uid}/documents?primaryKey=id", p.PathAndQuery));
        Assert.Equal([2, 2, 1], posts.Select(p => p.Json().GetArrayLength()));
        Assert.Equal(["/tasks/100", "/tasks/101", "/tasks/102"], h.Handler.Requests.Where(r => r.Method == HttpMethod.Get).Select(r => r.PathAndQuery));
    }

    [Fact]
    public async Task Upsert_SerialisesDocumentsWithTheCamelCaseAttributeNamesTheSettingsRefer()
    {
        var h = Create();
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.Accepted, "{\"taskUid\":1}"));
        var courseId = Guid.NewGuid();
        var document = CourseSearchDocument.ForCourse(
            courseId, "python-basics", "Python เบื้องต้น", null, null, Guid.NewGuid(), "ธนกฤต ศรีสุวรรณ", "Dev", Guid.NewGuid(), "โปรแกรมมิ่ง", "Programming");

        await h.Index.UpsertCoursesAsync([document], waitForCompletion: false, CancellationToken.None);

        var sent = h.Handler.Requests.Single().Json()[0];
        Assert.Equal($"course_{courseId}", sent.GetProperty("id").GetString());
        Assert.Equal("course", sent.GetProperty("type").GetString());
        Assert.Equal(courseId.ToString(), sent.GetProperty("courseId").GetString());
        Assert.Equal("Python เบื้องต้น", sent.GetProperty("title").GetString());
        Assert.Equal("ธนกฤต ศรีสุวรรณ", sent.GetProperty("instructorName").GetString());
        Assert.Equal("โปรแกรมมิ่ง", sent.GetProperty("categoryNameTh").GetString());
        Assert.Equal("Programming", sent.GetProperty("categoryNameEn").GetString());
        // Null optional fields are omitted rather than indexed as null.
        Assert.False(sent.TryGetProperty("subtitle", out _));
        Assert.False(sent.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task Upsert_WithoutWaiting_DoesNotPollTasks()
    {
        var h = Create();
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.Accepted, "{\"taskUid\":5}"));

        await h.Index.UpsertCoursesAsync([Document()], waitForCompletion: false, CancellationToken.None);

        Assert.Single(h.Handler.Requests);
    }

    [Fact]
    public async Task Upsert_FirstUseInAProcess_EnsuresTheIndexBeforeWritingDocuments()
    {
        var h = Create();
        h.Handler.Responder = (request, _) => Task.FromResult(request switch
        {
            { Method.Method: "GET", PathAndQuery: $"/indexes/{Uid}" } => Reply(HttpStatusCode.OK, "{}"),
            { Method.Method: "PATCH" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":1}"),
            { Method.Method: "GET", PathAndQuery: "/tasks/1" } => Reply(HttpStatusCode.OK, "{\"status\":\"succeeded\"}"),
            { Method.Method: "POST" } => Reply(HttpStatusCode.Accepted, "{\"taskUid\":2}"),
            _ => Reply(HttpStatusCode.BadRequest, "{}"),
        });

        await h.Index.UpsertCoursesAsync([Document()], waitForCompletion: false, CancellationToken.None);

        Assert.Equal(
            [$"GET /indexes/{Uid}", $"PATCH /indexes/{Uid}/settings", "GET /tasks/1", $"POST /indexes/{Uid}/documents?primaryKey=id"],
            h.Handler.Calls);
    }

    [Fact]
    public async Task Upsert_RejectedByEngine_ThrowsWithTheEnginesErrorCode_ButNeverTheApiKey()
    {
        var h = Create();
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(
            HttpStatusCode.Forbidden, "{\"message\":\"The provided API key is invalid.\",\"code\":\"invalid_api_key\",\"type\":\"auth\"}"));

        var ex = await Assert.ThrowsAsync<CourseSearchIndexException>(
            () => h.Index.UpsertCoursesAsync([Document()], waitForCompletion: true, CancellationToken.None));

        Assert.Contains("invalid_api_key", ex.Message, StringComparison.Ordinal);
        Assert.Contains("403", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upsert_NetworkFailure_ThrowsCourseSearchIndexException()
    {
        var h = Create();
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (_, _) => throw new HttpRequestException("no route to host");

        var ex = await Assert.ThrowsAsync<CourseSearchIndexException>(
            () => h.Index.UpsertCoursesAsync([Document()], waitForCompletion: false, CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Upsert_NothingToWrite_DoesNotCallTheEngine()
    {
        var h = Create();

        await h.Index.UpsertCoursesAsync([], waitForCompletion: true, CancellationToken.None);

        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Upsert_TaskStillRunningPastTheBudget_ReturnsInsteadOfBlockingForever()
    {
        var h = Create(o => o.TaskWaitTimeoutSeconds = 1);
        h.Availability.MarkIndexEnsured();
        h.Handler.Responder = (request, _) => Task.FromResult(
            request.Method == HttpMethod.Post
                ? Reply(HttpStatusCode.Accepted, "{\"taskUid\":9}")
                : Reply(HttpStatusCode.OK, "{\"status\":\"processing\"}"));

        await h.Index.UpsertCoursesAsync([Document()], waitForCompletion: true, CancellationToken.None);

        Assert.True(h.Handler.Requests.Count(r => r.PathAndQuery == "/tasks/9") >= 2);
    }

    [Fact]
    public async Task DeleteCourses_SendsTypePrefixedIdsToDeleteBatch_InChunks()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.Accepted, "{\"taskUid\":1}"));
        var ids = Enumerable.Range(0, 1_500).Select(_ => Guid.NewGuid()).ToList();

        await h.Index.DeleteCoursesAsync(ids, CancellationToken.None);

        Assert.Equal(2, h.Handler.Requests.Count);
        Assert.All(h.Handler.Requests, r => Assert.Equal($"/indexes/{Uid}/documents/delete-batch", r.PathAndQuery));
        Assert.Equal([1000, 500], h.Handler.Requests.Select(r => r.Json().GetArrayLength()));
        Assert.Equal($"course_{ids[0]}", h.Handler.Requests[0].Json()[0].GetString());
    }

    [Fact]
    public async Task DeleteCourses_IndexDoesNotExist_IsNotAnError()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.NotFound, "{\"code\":\"index_not_found\"}"));

        await h.Index.DeleteCoursesAsync([Guid.NewGuid()], CancellationToken.None);
    }

    [Fact]
    public async Task ListIndexedCourseIds_PagesUntilAShortPage()
    {
        var h = Create();
        var firstPage = Enumerable.Range(0, 1_000).Select(_ => Guid.NewGuid()).ToList();
        var secondPage = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        h.Handler.Responder = (request, _) =>
        {
            var offset = request.Json().GetProperty("offset").GetInt32();
            var page = offset == 0 ? firstPage : secondPage;
            return Task.FromResult(Reply(
                HttpStatusCode.OK,
                "{\"results\":[" + string.Join(",", page.Select(id => $"{{\"courseId\":\"{id}\"}}")) + "],\"total\":1003}"));
        };

        var ids = await h.Index.ListIndexedCourseIdsAsync(CancellationToken.None);

        Assert.Equal(1_003, ids.Count);
        Assert.Equal(2, h.Handler.Requests.Count);
        var body = h.Handler.Requests[0].Json();
        Assert.Equal($"/indexes/{Uid}/documents/fetch", h.Handler.Requests[0].PathAndQuery);
        Assert.Equal("type = \"course\"", body.GetProperty("filter").GetString());
        Assert.Equal(1_000, h.Handler.Requests[1].Json().GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task ListIndexedCourseIds_IndexDoesNotExist_ReturnsEmpty()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.NotFound, "{\"code\":\"index_not_found\"}"));

        Assert.Empty(await h.Index.ListIndexedCourseIdsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountCourseDocuments_ReadsTheTotalOfTheCourseFilter()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, "{\"results\":[{\"id\":\"course_x\"}],\"offset\":0,\"limit\":1,\"total\":42}"));

        Assert.Equal(42, await h.Index.CountCourseDocumentsAsync(CancellationToken.None));
        Assert.Equal("type = \"course\"", h.Handler.Requests.Single().Json().GetProperty("filter").GetString());
    }

    [Fact]
    public async Task CountCourseDocuments_IndexDoesNotExist_ReturnsNull_ButUnreachableThrows()
    {
        var h = Create();
        h.Handler.Responder = (_, _) => Task.FromResult(Reply(HttpStatusCode.NotFound, "{\"code\":\"index_not_found\"}"));
        Assert.Null(await h.Index.CountCourseDocumentsAsync(CancellationToken.None));

        h.Handler.Responder = (_, _) => throw new HttpRequestException("down");
        await Assert.ThrowsAsync<CourseSearchIndexException>(() => h.Index.CountCourseDocumentsAsync(CancellationToken.None));
    }
}
