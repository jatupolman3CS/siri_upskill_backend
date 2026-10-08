using System.Text.Json;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using StackExchange.Redis;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// Redis-backed <see cref="IGoogleOAuthStateStore"/> (P11-03 contract section 6.1). Key <c>live:google:oauth:{sha256hex(state)}</c> with a TTL —
/// the raw <c>state</c> is never stored. <see cref="TryConsumeAsync"/> is a <c>StringGet</c> followed by <c>KeyDelete</c>, and only the
/// caller whose delete succeeds gets the payload: the dev stack runs Garnet, which may lack <c>GETDEL</c>, and this is equally safe
/// on Redis (a replayed callback loses the delete race and gets nothing).
/// <para>
/// Fail-closed, the opposite of the cache-style Redis consumers: if Redis is unreachable <see cref="TrySaveAsync"/> reports failure
/// and <see cref="TryConsumeAsync"/> returns <c>null</c>, so an OAuth flow can never complete without a recorded state. Nothing about
/// the state, verifier or payload is logged.
/// </para>
/// </summary>
public sealed class RedisGoogleOAuthStateStore(IConnectionMultiplexer redis, ILogger<RedisGoogleOAuthStateStore> logger) : IGoogleOAuthStateStore
{
    internal const string KeyPrefix = "live:google:oauth:";

    public async Task<bool> TrySaveAsync(string state, GoogleOAuthState payload, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            logger.LogWarning("Google OAuth state could not be stored: Redis is not connected (failing closed).");
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(payload);
            return await redis.GetDatabase()
                .StringSetAsync(KeyFor(state), json, timeToLive, When.NotExists)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning("Google OAuth state could not be stored: {ExceptionType} (failing closed).", ex.GetType().Name);
            return false;
        }
    }

    public async Task<GoogleOAuthState?> TryConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

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

    private static string KeyFor(string state) => KeyPrefix + GoogleOAuthPkce.HashState(state);
}
