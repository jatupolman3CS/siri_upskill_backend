using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// <see cref="ICourseSearchIndex"/> over Meilisearch's REST API (plain <see cref="HttpClient"/> + System.Text.Json — no SDK dependency,
/// and every request is visible and unit-testable through a message handler). Registered as a typed client by <c>CatalogModule</c>, which
/// also sets the base address, bearer header and timeout; this class is only constructed when <see cref="MeilisearchOptions.IsActive"/>
/// (otherwise <see cref="DisabledCourseSearchIndex"/> is used).
/// <para>
/// Why the engine only returns ids: PostgreSQL decides what is visible and filters/sorts/pages the candidate set, so a stale index entry
/// (a course unpublished a moment ago) can never leak — the id simply fails the database's <c>Status = Published</c> check.
/// Thai is tokenised by Meilisearch's own segmenter (Thai has no spaces between words), and the instructor's display name is a searchable
/// attribute of every course document, so "search by teacher" works with the same call.
/// </para>
/// <para>
/// Failure model: <see cref="SearchCourseIdsAsync"/> swallows engine trouble into a <c>null</c> result (and feeds
/// <see cref="MeilisearchAvailability"/>'s circuit breaker); all write/maintenance methods throw <see cref="CourseSearchIndexException"/>.
/// The API key travels only in the <c>Authorization</c> header configured on the client — it is never logged or put into an exception message.
/// </para>
/// </summary>
public sealed class MeilisearchCourseSearchIndex(
    HttpClient http,
    IOptions<MeilisearchOptions> options,
    MeilisearchAvailability availability,
    ILogger<MeilisearchCourseSearchIndex> logger) : ICourseSearchIndex
{
    private const int MaxQueryLength = 200;
    private const int DeleteBatchSize = 1_000;
    private const int FetchPageSize = 1_000;
    private static readonly TimeSpan TaskPollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] SearchableAttributes =
        ["title", "instructorName", "subtitle", "categoryNameTh", "categoryNameEn", "instructorHeadline", "description"];

    private static readonly string[] FilterableAttributes = ["type", "instructorId", "categoryId"];

    private static readonly string CourseTypeFilter = $"type = \"{CourseSearchDocument.CourseType}\"";

    private MeilisearchOptions Options => options.Value;

    private string IndexPath => $"indexes/{Uri.EscapeDataString(Options.DocumentsIndexUid)}";

    public bool IsEnabled => Options.IsActive;

    public async Task<IReadOnlyList<Guid>?> SearchCourseIdsAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text) || availability.IsCircuitOpen)
        {
            return null;
        }

        var query = text.Trim();
        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Options.SearchTimeoutMilliseconds));

        try
        {
            var response = await SendAsync(
                HttpMethod.Post,
                $"{IndexPath}/search",
                new SearchRequest(query, CourseTypeFilter, Options.MaxSearchHits, ["courseId"]),
                timeout.Token).ConfigureAwait(false);

            var ids = ParseHitCourseIds(response);
            availability.RecordSuccess();
            return ids;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return RecordSearchFailure("the search timed out");
        }
        catch (CourseSearchIndexException ex)
        {
            return RecordSearchFailure(ex.Message);
        }
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var existing = await SendAsync(HttpMethod.Get, IndexPath, null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        if (existing is null)
        {
            var created = await SendAsync(
                HttpMethod.Post, "indexes", new CreateIndexRequest(Options.DocumentsIndexUid, "id"), cancellationToken).ConfigureAwait(false);

            try
            {
                await WaitForTaskAsync(ReadTaskUid(created), cancellationToken).ConfigureAwait(false);
            }
            catch (CourseSearchIndexException)
            {
                // Another instance created the index between our GET and POST (the creation task then fails with index_already_exists).
                // That is the outcome we wanted — anything else (the index still does not exist) is a real failure and is rethrown.
                var afterRace = await SendAsync(HttpMethod.Get, IndexPath, null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
                if (afterRace is null)
                {
                    throw;
                }
            }
        }

        var settings = await SendAsync(
            HttpMethod.Patch,
            $"{IndexPath}/settings",
            new IndexSettings(SearchableAttributes, FilterableAttributes, new PaginationSettings(Options.MaxSearchHits)),
            cancellationToken).ConfigureAwait(false);
        await WaitForTaskAsync(ReadTaskUid(settings), cancellationToken).ConfigureAwait(false);

        availability.MarkIndexEnsured();
    }

    public async Task UpsertCoursesAsync(
        IReadOnlyCollection<CourseSearchDocument> documents, bool waitForCompletion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (!IsEnabled || documents.Count == 0)
        {
            return;
        }

        if (!availability.IndexEnsured)
        {
            await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        }

        var taskUids = new List<long>();
        foreach (var batch in documents.Chunk(Options.IndexBatchSize))
        {
            var response = await SendAsync(
                HttpMethod.Post, $"{IndexPath}/documents?primaryKey=id", batch, cancellationToken).ConfigureAwait(false);
            taskUids.Add(ReadTaskUid(response));
        }

        if (waitForCompletion)
        {
            foreach (var taskUid in taskUids)
            {
                await WaitForTaskAsync(taskUid, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);
        if (!IsEnabled || courseIds.Count == 0)
        {
            return;
        }

        foreach (var batch in courseIds.Select(CourseSearchDocument.IdFor).Chunk(DeleteBatchSize))
        {
            // A missing index means there is nothing to delete — not an error.
            await SendAsync(HttpMethod.Post, $"{IndexPath}/documents/delete-batch", batch, cancellationToken, allowNotFound: true)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyCollection<Guid>> ListIndexedCourseIdsAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var ids = new List<Guid>();
        for (var offset = 0; ; offset += FetchPageSize)
        {
            var page = await SendAsync(
                HttpMethod.Post,
                $"{IndexPath}/documents/fetch",
                new FetchRequest(["courseId"], CourseTypeFilter, FetchPageSize, offset),
                cancellationToken,
                allowNotFound: true).ConfigureAwait(false);

            if (page is null || !page.Value.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var document in results.EnumerateArray())
            {
                count++;
                if (TryReadCourseId(document, out var courseId))
                {
                    ids.Add(courseId);
                }
            }

            if (count < FetchPageSize)
            {
                break;
            }
        }

        return ids;
    }

    public async Task<long?> CountCourseDocumentsAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var page = await SendAsync(
            HttpMethod.Post,
            $"{IndexPath}/documents/fetch",
            new FetchRequest(["id"], CourseTypeFilter, 1, 0),
            cancellationToken,
            allowNotFound: true).ConfigureAwait(false);

        return page is { } root && root.TryGetProperty("total", out var total) && total.TryGetInt64(out var count) ? count : null;
    }

    private IReadOnlyList<Guid>? RecordSearchFailure(string reason)
    {
        var tripped = availability.RecordFailure();
        if (tripped)
        {
            logger.LogWarning(
                "Meilisearch search failed ({Reason}); course search uses the database fallback and skips Meilisearch for {Seconds}s.",
                reason,
                Options.CircuitOpenSeconds);
        }
        else
        {
            logger.LogWarning("Meilisearch search failed ({Reason}); this request uses the database fallback.", reason);
        }

        return null;
    }

    private static IReadOnlyList<Guid> ParseHitCourseIds(JsonElement? response)
    {
        if (response is not { } root || !root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            throw new CourseSearchIndexException("Meilisearch returned a search response without a 'hits' array.");
        }

        var ids = new List<Guid>(hits.GetArrayLength());
        var seen = new HashSet<Guid>();
        foreach (var hit in hits.EnumerateArray())
        {
            if (TryReadCourseId(hit, out var courseId) && seen.Add(courseId))
            {
                ids.Add(courseId);
            }
        }

        return ids;
    }

    private static bool TryReadCourseId(JsonElement document, out Guid courseId)
    {
        courseId = Guid.Empty;
        return document.ValueKind == JsonValueKind.Object
            && document.TryGetProperty("courseId", out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out courseId);
    }

    private static long ReadTaskUid(JsonElement? response)
    {
        if (response is { } root && root.TryGetProperty("taskUid", out var uid) && uid.TryGetInt64(out var taskUid))
        {
            return taskUid;
        }

        throw new CourseSearchIndexException("Meilisearch did not return a task uid for an asynchronous operation.");
    }

    /// <summary>Polls a task until it succeeded. A failed/cancelled task throws; running past the budget only logs (the work is still queued and ordered).</summary>
    private async Task WaitForTaskAsync(long taskUid, CancellationToken cancellationToken)
    {
        var budget = TimeSpan.FromSeconds(Options.TaskWaitTimeoutSeconds);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            var task = await SendAsync(HttpMethod.Get, $"tasks/{taskUid}", null, cancellationToken).ConfigureAwait(false);
            var status = task is { } root && root.TryGetProperty("status", out var s) ? s.GetString() : null;

            switch (status)
            {
                case "succeeded":
                    return;
                case "failed":
                case "canceled":
                    throw new CourseSearchIndexException(
                        $"Meilisearch task {taskUid} {status}: {DescribeTaskError(task)}");
            }

            if (clock.Elapsed >= budget)
            {
                logger.LogWarning(
                    "Meilisearch task {TaskUid} is still {Status} after {Seconds}s; continuing without waiting for it.",
                    taskUid,
                    status ?? "unknown",
                    Options.TaskWaitTimeoutSeconds);
                return;
            }

            await Task.Delay(TaskPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string DescribeTaskError(JsonElement? task)
    {
        if (task is { } root && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            return $"{code} {message}".Trim();
        }

        return "no error details";
    }

    /// <summary>One HTTP call. Returns the parsed JSON body (null for an empty body, or a 404 when <paramref name="allowNotFound"/>).
    /// Non-success statuses become <see cref="CourseSearchIndexException"/> carrying Meilisearch's own error code/message.</summary>
    private async Task<JsonElement?> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CourseSearchIndexException($"Meilisearch {method} {path} could not be reached: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed (a caller/linked cancellation propagates as OperationCanceledException instead).
            throw new CourseSearchIndexException($"Meilisearch {method} {path} timed out.", ex);
        }

        using (response)
        {
            string content;
            try
            {
                content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new CourseSearchIndexException($"Meilisearch {method} {path} response could not be read: {ex.Message}", ex);
            }

            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CourseSearchIndexException(
                    $"Meilisearch {method} {path} returned {(int)response.StatusCode}: {DescribeError(content)}");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new CourseSearchIndexException($"Meilisearch {method} {path} returned a body that is not JSON.", ex);
            }
        }
    }

    private static string DescribeError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            return string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message) ? "no error details" : $"{code} {message}".Trim();
        }
        catch (JsonException)
        {
            return "a non-JSON error body";
        }
    }

    private sealed record SearchRequest(string Q, string Filter, int Limit, string[] AttributesToRetrieve);

    private sealed record CreateIndexRequest(string Uid, string PrimaryKey);

    private sealed record FetchRequest(string[] Fields, string Filter, int Limit, int Offset);

    private sealed record PaginationSettings(int MaxTotalHits);

    private sealed record IndexSettings(
        string[] SearchableAttributes, string[] FilterableAttributes, PaginationSettings Pagination);
}
