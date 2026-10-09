using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// Redis-backed <see cref="IGoogleOAuthStateStore"/> (P11-03 contract section 6.1). Key <c>live:google:oauth:{sha256hex(state)}</c> with a TTL —
/// the raw <c>state</c> is never stored. <see cref="TryConsumeAsync"/> is a <c>StringGet</c> followed by <c>KeyDelete</c>, and only the
/// caller whose delete succeeds gets the payload: the dev stack runs Garnet, which may lack <c>GETDEL</c>, and this is equally safe
/// on Redis (a replayed callback loses the delete race and gets nothing).
/// <para>
/// <b>Redis down → this process's memory.</b> If Redis is unreachable (or refuses the write), the state is kept in a bounded, expiring,
/// single-use in-process table instead of failing the connect button. A state is still always <em>recorded</em> before the browser is sent to
/// Google, and still single-use (<see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/> is the "delete wins" step), so the
/// OAuth flow can never complete with an unrecorded or replayed state. The one limit is that the callback must reach the <em>same API process</em>:
/// with several replicas and Redis down, a callback landing on another replica finds nothing and ends in <c>state_invalid</c> (the instructor
/// just tries again). Production runs one API pod; give the replicas a working Redis before scaling out. Every fallback save is logged as a warning.
/// </para>
/// Nothing about the state, verifier or payload is logged.
/// </summary>
public sealed class RedisGoogleOAuthStateStore(IConnectionMultiplexer redis, IClock clock, ILogger<RedisGoogleOAuthStateStore> logger) : IGoogleOAuthStateStore
{
    internal const string KeyPrefix = "live:google:oauth:";

    /// <summary>Upper bound on states held in memory — a connect click stores one, so this is far above real use and only stops abuse from growing the table.</summary>
    internal const int MaxInMemoryStates = 1000;

    private readonly ConcurrentDictionary<string, (GoogleOAuthState Payload, DateTime ExpiresAtUtc)> _inMemory = new(StringComparer.Ordinal);

    public async Task<bool> TrySaveAsync(string state, GoogleOAuthState payload, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            logger.LogWarning("Google OAuth state: Redis is not connected, keeping it in this process's memory (single API replica only).");
            return SaveInMemory(state, payload, timeToLive);
        }

        try
        {
            var json = JsonSerializer.Serialize(payload);
            var stored = await redis.GetDatabase()
                .StringSetAsync(KeyFor(state), json, timeToLive, When.NotExists)
                .ConfigureAwait(false);
            if (stored)
            {
                return true;
            }

            // `NotExists` refused: this state value already exists. It is random, so that is not a normal event — never reuse it.
            return false;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning("Google OAuth state: Redis write failed ({ExceptionType}), keeping it in this process's memory (single API replica only).", ex.GetType().Name);
            return SaveInMemory(state, payload, timeToLive);
        }
    }

    public async Task<GoogleOAuthState?> TryConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // A state saved while Redis was down lives here; whoever removes it owns it (single use), and an expired one is refused.
        if (_inMemory.TryRemove(KeyFor(state), out var local))
        {
            return local.ExpiresAtUtc > clock.UtcNow ? local.Payload : null;
        }

        if (!redis.IsConnected)
        {
            logger.LogWarning("Google OAuth state could not be read: Redis is not connected.");
            return null;
        }

        try
        {
            var database = redis.GetDatabase();
            var key = KeyFor(state);

            var value = await database.StringGetAsync(key).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            // Whoever deletes the key owns the state; a concurrent replay gets false here and is refused.
            if (!await database.KeyDeleteAsync(key).ConfigureAwait(false))
            {
                return null;
            }

            return JsonSerializer.Deserialize<GoogleOAuthState>(value.ToString());
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            logger.LogWarning("Google OAuth state could not be read: {ExceptionType}.", ex.GetType().Name);
            return null;
        }
    }

    private bool SaveInMemory(string state, GoogleOAuthState payload, TimeSpan timeToLive)
    {
        var now = clock.UtcNow;

        if (_inMemory.Count >= MaxInMemoryStates)
        {
            foreach (var entry in _inMemory)
            {
                if (entry.Value.ExpiresAtUtc <= now)
                {
                    _inMemory.TryRemove(entry.Key, out _);
                }
            }

            if (_inMemory.Count >= MaxInMemoryStates)
            {
                // Still full of live states: refuse rather than grow — the caller reports "try again later".
                return false;
            }
        }

        return _inMemory.TryAdd(KeyFor(state), (payload, now + timeToLive));
    }

    private static string KeyFor(string state) => KeyPrefix + GoogleOAuthPkce.HashState(state);
}
