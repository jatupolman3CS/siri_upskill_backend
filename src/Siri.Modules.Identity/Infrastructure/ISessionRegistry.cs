namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// The Redis mirror of active <see cref="Domain.USER_SESSION"/>s (ARCHITECTURE.md §5:
/// <c>session:{userId}:{sessionId}</c>, TTL = refresh token lifetime). Deliberately thin — two
/// methods, no query/read surface — because nothing in this codebase reads this data back yet
/// (see this interface's own "phased design" note below and <c>RedisSessionRegistry</c>'s doc
/// comment for why writes here must never be allowed to fail the caller's request).
/// <para>
/// <b>Why this exists now with nothing consuming it yet</b> (task instruction: document this
/// explicitly rather than treating it as a shortcut). ARCHITECTURE.md §5 designs Redis as the
/// runtime source of truth for concurrent-session checks, with MSSQL as the audit mirror — and says
/// a Redis outage should "fail closed for playback but fail open for browsing". But *playback*
/// (the one thing that section says should fail closed) does not exist in this codebase yet — it is
/// Phase 2/Media (`docs/TASKS.md` P2-04, which depends on this task). Building a real fail-closed
/// gate today would be gating a feature that cannot be reached, and — worse — flipping today's
/// actual eviction *decision* (which is what SE-03 requires right now: evict-the-oldest-session-over-
/// the-limit) to depend on Redis being up would make a routine Redis blip able to break login itself,
/// for zero benefit (nothing downstream distinguishes "Redis says 1 device" from "Redis says 3
/// devices" yet). So, for this task: MSSQL (<c>UserSessions</c>, already durable and already the
/// audit trail) is authoritative for the eviction decision, and Redis is written to *opportunistically*
/// as a cache/mirror that Phase 2's playback-token issuance will read from once it exists — exactly
/// the role ARCHITECTURE.md §5 assigns it, just built in the order "make the authoritative decision
/// work now, wire the fast-path cache's read side later" rather than the other way around. The actual
/// fail-closed check belongs entirely to that later Media task, against the mirror this task starts
/// populating correctly today.
/// </para>
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Writes/refreshes the mirror key for one active session, expiring after
    /// <paramref name="ttl"/> (callers pass the same refresh-token lifetime the corresponding
    /// <see cref="Domain.REFRESH_TOKEN"/> was issued with, so the two never drift apart). Best-effort —
    /// see <c>RedisSessionRegistry</c>'s doc comment for why a Redis failure here must never
    /// propagate to the caller.</summary>
    Task RegisterAsync(Guid userId, Guid sessionId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Removes one session's mirror key — called on every path that revokes a
    /// <see cref="Domain.USER_SESSION"/> in MSSQL (SE-03 eviction, and Refresh's reuse-detection
    /// family-revocation), so the mirror never lags a revocation that already happened at the
    /// authoritative source. Best-effort, same reasoning as <see cref="RegisterAsync"/>.</summary>
    Task RemoveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
}
