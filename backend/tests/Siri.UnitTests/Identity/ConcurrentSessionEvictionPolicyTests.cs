using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Identity;

public class ConcurrentSessionEvictionPolicyTests
{
    private static UserSession NewSession(DateTime createdAtUtc) =>
        UserSession.Start(Guid.NewGuid(), "device-" + Guid.NewGuid(), null, null, null, new FakeClock(createdAtUtc));

    [Fact]
    public void SelectSessionsToEvict_CountWithinLimit_ReturnsEmpty()
    {
        var sessions = new[]
        {
            NewSession(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc)),
            NewSession(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc)),
        };

        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict(sessions, effectiveLimit: 2);

        Assert.Empty(evicted);
    }

    [Fact]
    public void SelectSessionsToEvict_CountBelowLimit_ReturnsEmpty()
    {
        var sessions = new[] { NewSession(DateTime.UtcNow) };

        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict(sessions, effectiveLimit: 2);

        Assert.Empty(evicted);
    }

    [Fact]
    public void SelectSessionsToEvict_EmptyInput_ReturnsEmpty()
    {
        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict([], effectiveLimit: 2);

        Assert.Empty(evicted);
    }

    [Fact]
    public void SelectSessionsToEvict_OneOverLimit_ReturnsTheSingleOldestSession()
    {
        var oldest = NewSession(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var middle = NewSession(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var newest = NewSession(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict([middle, newest, oldest], effectiveLimit: 2);

        Assert.Single(evicted);
        Assert.Same(oldest, evicted[0]);
    }

    /// <summary>
    /// N active sessions and a limit of 2 must evict exactly the oldest (N-2), ordered
    /// oldest-first — security.md's literal "revoke session เก่าสุด" (oldest by CreatedAtUtc, not
    /// LastSeenAtUtc/least-recently-used).
    /// </summary>
    [Fact]
    public void SelectSessionsToEvict_MultipleOverLimit_ReturnsOldestFirstUpToExcessCount()
    {
        var s1 = NewSession(new DateTime(2026, 8, 17, 6, 0, 0, DateTimeKind.Utc)); // oldest
        var s2 = NewSession(new DateTime(2026, 8, 17, 7, 0, 0, DateTimeKind.Utc));
        var s3 = NewSession(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var s4 = NewSession(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var s5 = NewSession(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc)); // newest

        // Deliberately shuffled input order — the policy must sort by CreatedAtUtc itself, not rely
        // on the caller having already ordered the collection.
        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict([s3, s5, s1, s4, s2], effectiveLimit: 2);

        Assert.Equal(3, evicted.Count);
        Assert.Equal([s1, s2, s3], evicted); // oldest three, oldest-first
    }

    [Fact]
    public void SelectSessionsToEvict_NewlyCreatedSessionIncludedAndWithinLimit_IsNeverEvicted()
    {
        // Mirrors Login's real call shape: the brand-new session is always the most recently created,
        // so with a sane (>=1) limit it can never be among the sessions selected for eviction.
        var older1 = NewSession(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var older2 = NewSession(new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));
        var brandNew = NewSession(new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc));

        var evicted = ConcurrentSessionEvictionPolicy.SelectSessionsToEvict([older1, older2, brandNew], effectiveLimit: 2);

        Assert.Single(evicted);
        Assert.Same(older1, evicted[0]);
        Assert.DoesNotContain(brandNew, evicted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SelectSessionsToEvict_NonPositiveLimit_ThrowsArgumentOutOfRangeException(int limit)
    {
        var sessions = new[] { NewSession(DateTime.UtcNow) };

        Assert.Throws<ArgumentOutOfRangeException>(() => ConcurrentSessionEvictionPolicy.SelectSessionsToEvict(sessions, limit));
    }

    [Fact]
    public void SelectSessionsToEvict_NullActiveSessions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ConcurrentSessionEvictionPolicy.SelectSessionsToEvict(null!, effectiveLimit: 2));
    }
}
