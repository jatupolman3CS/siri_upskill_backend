using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="SESSION_JOIN_LOG"/> (docs/contracts/
/// P11-05-live-learner-instructor-api-join-gate.md §2) — an append-only record, so the only behaviour is
/// <see cref="SESSION_JOIN_LOG.Record"/> and its input guards.</summary>
public class SessionJoinLogTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(LiveParticipantRole.Learner)]
    [InlineData(LiveParticipantRole.Instructor)]
    public void Record_ValidInput_CapturesEveryField(LiveParticipantRole role)
    {
        var sessionId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authSessionId = Guid.NewGuid();

        var log = SESSION_JOIN_LOG.Record(sessionId, courseId, userId, role, authSessionId, Now, "203.0.113.7", "Mozilla/5.0");

        Assert.NotEqual(Guid.Empty, log.SESSION_JOIN_LOG_ID);
        Assert.Equal(sessionId, log.SESSION_ID);
        Assert.Equal(courseId, log.COURSE_ID);
        Assert.Equal(userId, log.USER_ID);
        Assert.Equal(role, log.ROLE);
        Assert.Equal(authSessionId, log.AUTH_SESSION_ID);
        Assert.Equal(Now, log.JOINED_AT_UTC);
        Assert.Equal("203.0.113.7", log.IP_ADDRESS);
        Assert.Equal("Mozilla/5.0", log.USER_AGENT);
    }

    [Fact]
    public void Record_GeneratesDistinctTimeOrderedIds()
    {
        var sessionId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var first = SESSION_JOIN_LOG.Record(sessionId, courseId, userId, LiveParticipantRole.Learner, null, Now, null, null);
        var second = SESSION_JOIN_LOG.Record(sessionId, courseId, userId, LiveParticipantRole.Learner, null, Now, null, null);

        Assert.NotEqual(first.SESSION_JOIN_LOG_ID, second.SESSION_JOIN_LOG_ID);
    }

    [Fact]
    public void Record_NoAuthSessionIpOrUserAgent_StoresNulls()
    {
        var log = SESSION_JOIN_LOG.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, null, Now, null, null);

        Assert.Null(log.AUTH_SESSION_ID);
        Assert.Null(log.IP_ADDRESS);
        Assert.Null(log.USER_AGENT);
    }

    [Fact]
    public void Record_EmptyAuthSessionId_IsStoredAsNull()
    {
        var log = SESSION_JOIN_LOG.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, Guid.Empty, Now, null, null);

        Assert.Null(log.AUTH_SESSION_ID);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_BlankIpOrUserAgent_IsStoredAsNull(string blank)
    {
        var log = SESSION_JOIN_LOG.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, null, Now, blank, blank);

        Assert.Null(log.IP_ADDRESS);
        Assert.Null(log.USER_AGENT);
    }

    [Fact]
    public void Record_OverlongIpAndUserAgent_AreCutToTheirColumns()
    {
        var log = SESSION_JOIN_LOG.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, null, Now, new string('1', 200), new string('u', 1000));

        Assert.Equal(64, log.IP_ADDRESS!.Length);
        Assert.Equal(300, log.USER_AGENT!.Length);
    }

    [Fact]
    public void Record_SurroundingWhitespace_IsTrimmed()
    {
        var log = SESSION_JOIN_LOG.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, null, Now, "  203.0.113.7 ", "\tAgent/1.0  ");

        Assert.Equal("203.0.113.7", log.IP_ADDRESS);
        Assert.Equal("Agent/1.0", log.USER_AGENT);
    }

    [Fact]
    public void Record_EmptyIds_Throw()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => SESSION_JOIN_LOG.Record(Guid.Empty, id, id, LiveParticipantRole.Learner, null, Now, null, null));
        Assert.Throws<ArgumentException>(() => SESSION_JOIN_LOG.Record(id, Guid.Empty, id, LiveParticipantRole.Learner, null, Now, null, null));
        Assert.Throws<ArgumentException>(() => SESSION_JOIN_LOG.Record(id, id, Guid.Empty, LiveParticipantRole.Learner, null, Now, null, null));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Record_NonUtcTimestamp_Throws(DateTimeKind kind)
    {
        var id = Guid.NewGuid();
        var notUtc = DateTime.SpecifyKind(Now, kind);

        Assert.Throws<ArgumentException>(() => SESSION_JOIN_LOG.Record(id, id, id, LiveParticipantRole.Learner, null, notUtc, null, null));
    }
}
