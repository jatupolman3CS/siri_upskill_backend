using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="ISessionRegistry"/> implementation over <see cref="IConnectionMultiplexer"/>. Key shape
/// is exactly ARCHITECTURE.md §5's design: <c>session:{userId}:{sessionId}</c>. The value is
/// deliberately empty (<see cref="RedisValue.EmptyString"/>) — presence-with-TTL is the entire
/// signal this phase needs (see <see cref="ISessionRegistry"/>'s "phased design" note); there is
/// nothing to read out of the value yet.
/// <para>
/// <b>Fail-open, by design, for this phase</b> (task instruction, ARCHITECTURE.md §5): every method
/// here catches and logs rather than letting a Redis exception propagate. MSSQL's
/// <c>UserSessions</c>/<c>RefreshTokens</c> rows are the authoritative state Login/Refresh's handlers
/// already committed by the time these methods run (see <c>Features/Login/Handler.cs</c> — this is
/// only called after the DB-authoritative <c>SaveChangesAsync</c> succeeds) — a Redis outage at that
/// point means only the *mirror* is stale, not that the eviction/revocation itself failed to happen.
/// Nothing in this codebase currently reads this mirror back for an authorization decision (that is
/// Phase 2's playback-token issuance, not built yet), so there is no feature today that a stale or
/// missing mirror key could incorrectly let through — letting a Redis blip fail the login/refresh
/// request it rode in on would make authentication itself less available for zero present security
/// benefit. Once Phase 2 adds a real reader, *that* code path is the one that gets to decide whether
/// a miss means "fail closed" — not this write-side mirror.
/// </para>
/// </summary>
public sealed class RedisSessionRegistry(IConnectionMultiplexer redis, ILogger<RedisSessionRegistry> logger)
    : ISessionRegistry
{
    public async Task RegisterAsync(Guid userId, Guid sessionId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        // StackExchange.Redis's IDatabaseAsync methods take no CancellationToken parameter (cancel by
        // disposing the multiplexer/timeout instead) — this is the only place backend.md's "ต้องรับ +
        // ส่งต่อ CancellationToken เสมอ" can apply here: bail out before issuing the command at all if
        // the caller already cancelled.
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.StringSetAsync(BuildKey(userId, sessionId), RedisValue.EmptyString, ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Deliberately broad + logged, not silent (backend.md: "ห้าม catch (Exception) { } เงียบ ๆ")
            // — see this class's own doc comment for why a Redis failure must not propagate here.
            logger.LogWarning(
                ex,
                "Failed to mirror session {SessionId} for user {UserId} into Redis — continuing (fail-open, see RedisSessionRegistry's doc comment).",
                sessionId,
                userId);
        }
    }

    public async Task RemoveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!redis.IsConnected)
        {
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync(BuildKey(userId, sessionId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to remove Redis mirror key for session {SessionId}, user {UserId} — continuing (fail-open, see RedisSessionRegistry's doc comment).",
                sessionId,
                userId);
        }
    }

    private static RedisKey BuildKey(Guid userId, Guid sessionId) => $"session:{userId}:{sessionId}";
}
