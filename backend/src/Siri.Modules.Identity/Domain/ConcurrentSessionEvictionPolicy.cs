namespace Siri.Modules.Identity.Domain;

/// <summary>
/// Pure selection logic for SE-03 concurrent-login enforcement — no EF/DB, HTTP, clock, or config
/// access, so it is testable in isolation from <c>Features/Login/Handler.cs</c>'s DB-orchestration
/// (backend.md: "เขียน test มาพร้อมโค้ด สำหรับ ... การตัดสินสิทธิ์"; this <em>is</em> the
/// "which device gets kicked" entitlement decision). Given a user's current active sessions (the
/// newly-created one included — the caller is responsible for that, see Login's handler) and the
/// effective limit for that account, decides which sessions must be evicted to bring the count back
/// within limit.
/// <para>
/// <b>Oldest-first</b> (security.md's literal "เกิน limit → revoke session เก่าสุด" — oldest by
/// <see cref="UserSession.CreatedAtUtc"/>, i.e. when the session started, not
/// <see cref="UserSession.LastSeenAtUtc"/>/least-recently-used). The task instructions call this out
/// explicitly as the interpretation to use absent a strong reason to deviate — there isn't one here:
/// "oldest login" is simple, predictable from a user's point of view (the device that logged in
/// longest ago is the one that goes), and matches the literal Thai wording exactly.
/// </para>
/// </summary>
public static class ConcurrentSessionEvictionPolicy
{
    /// <summary>
    /// Returns the sessions that must be evicted from <paramref name="activeSessions"/> so that at
    /// most <paramref name="effectiveLimit"/> remain — oldest (by <see cref="UserSession.CreatedAtUtc"/>)
    /// first. Empty if the count is already within the limit. Does not mutate/revoke anything itself
    /// (<see cref="UserSession.Revoke"/> needs an <c>IClock</c>, which this pure policy deliberately
    /// does not take — the caller drives the actual revocation).
    /// </summary>
    public static IReadOnlyList<UserSession> SelectSessionsToEvict(
        IReadOnlyCollection<UserSession> activeSessions, int effectiveLimit)
    {
        ArgumentNullException.ThrowIfNull(activeSessions);

        if (effectiveLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveLimit), effectiveLimit, "effectiveLimit must be at least 1.");
        }

        var excessCount = activeSessions.Count - effectiveLimit;
        if (excessCount <= 0)
        {
            return [];
        }

        return activeSessions
            .OrderBy(s => s.CreatedAtUtc)
            .Take(excessCount)
            .ToList();
    }
}
