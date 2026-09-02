using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Caches the public category tree's already-serialized JSON response under one Redis key. Narrowly
/// scoped on purpose (backend.md forbids "god service" <c>IXxxService</c> classes) — three methods,
/// nothing else.
/// <para>
/// String-based, not typed to a response DTO: this lives in <c>Infrastructure/</c>, and a
/// <c>Features/GetCategoryTree/</c> response type living there would mean Infrastructure reaching up
/// into Features, backwards from the normal dependency direction. <c>GetCategoryTreeHandler</c> owns
/// serialization; this class just stores/retrieves bytes.
/// </para>
/// <para>
/// Fail-open, same pattern as <c>Siri.Modules.Identity.Infrastructure.RedisSessionRegistry</c>: every
/// method catches and logs rather than letting a Redis exception propagate. Every write handler also
/// calls <see cref="InvalidateAsync"/> after a successful save, so the TTL below is a belt-and-suspenders
/// ceiling on staleness, not the primary freshness mechanism — a Redis outage degrades public category
/// browsing to "hits the DB every request," never a 500.
/// </para>
/// </summary>
public sealed class CategoryTreeCache(IConnectionMultiplexer redis, ILogger<CategoryTreeCache> logger)
{
    private const string Key = "catalog:categories:tree";

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    /// <summary>Returns the cached tree JSON, or <c>null</c> on a cache miss or Redis failure — either
    /// way the caller's correct response is "go read the database instead."</summary>
    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            return null;
        }

        try
        {
            var database = redis.GetDatabase();
            var value = await database.StringGetAsync(Key).ConfigureAwait(false);
            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read the category tree cache — falling back to the database (fail-open).");
            return null;
        }
    }

    public async Task SetAsync(string treeJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.StringSetAsync(Key, treeJson, Ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write the category tree cache — continuing without caching this response (fail-open).");
        }
    }

    /// <summary>Called by every write handler (Create/Update/Reorder/Delete) after a successful save.
    /// If this itself fails, the stale entry simply lives until <see cref="Ttl"/> expires — no worse
    /// than the cache being briefly behind, never wrong in a way that breaks anything.</summary>
    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync(Key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invalidate the category tree cache — it will self-correct after its TTL expires (fail-open).");
        }
    }
}
