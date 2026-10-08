using Siri.SharedKernel;

namespace Siri.Modules.Live.Domain;

/// <summary>
/// An immutable record that a user was handed the room link of a live session — written by the join gate
/// at the moment the link is revealed (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md §2).
/// It answers "who asked for the link, when, from where", which is the only forensic handle there is on a
/// Meet link a learner passes on, and it is the evidence the refund hard-block (P11-12) reads: a learner row
/// for (<see cref="USER_ID"/>, <see cref="COURSE_ID"/>) means they attended a live session of that course.
/// <para>
/// <b>Append-only:</b> no mutator exists — the only way to obtain an instance is <see cref="Record"/>, and
/// rows are never updated or deleted. Like <c>PLAYBACK_SESSION</c> (and unlike the other Live entities) it
/// implements neither <c>IAuditable</c> nor <c>ISoftDelete</c>: <see cref="JOINED_AT_UTC"/> is the one
/// timestamp that matters and there is no update to audit. The user id, course id and session id must be
/// kept even if the personal data (<see cref="IP_ADDRESS"/>, <see cref="USER_AGENT"/>) is later scrubbed
/// (retention follow-up) — the refund rule depends on them.
/// </para>
/// <para>
/// <see cref="COURSE_ID"/> is denormalized from the session so the refund check can query by
/// (<see cref="USER_ID"/>, <see cref="COURSE_ID"/>) without a cross-module join. No FK on any id —
/// cross-module/cross-schema, same reasoning as <c>PLAYBACK_SESSION</c>. UPPERCASE naming per
/// docs/DECISIONS.md D-17.
/// </para>
/// </summary>
public sealed class SESSION_JOIN_LOG
{
    private const int IpAddressMaxLength = 64;
    private const int UserAgentMaxLength = 300;

    /// <summary>EF Core materialization only.</summary>
    private SESSION_JOIN_LOG()
    {
    }

    public Guid SESSION_JOIN_LOG_ID { get; private set; }

    /// <summary><c>CATALOG.COURSE_LIVE_SESSIONS.Id</c> — no FK.</summary>
    public Guid SESSION_ID { get; private set; }

    /// <summary>The session's course, denormalized for the refund check — no FK.</summary>
    public Guid COURSE_ID { get; private set; }

    /// <summary>Always taken from the authenticated user (<c>IUserContext</c>), never from the request — no FK.</summary>
    public Guid USER_ID { get; private set; }

    /// <summary>Refund and KPI logic count only <see cref="LiveParticipantRole.Learner"/> rows.</summary>
    public LiveParticipantRole ROLE { get; private set; }

    /// <summary>The <c>sid</c> claim of the access token (Identity <c>UserSession.Id</c>) — forensics, same as
    /// the playback log. <c>null</c> when the token carried none.</summary>
    public Guid? AUTH_SESSION_ID { get; private set; }

    /// <summary>The moment the room link was revealed.</summary>
    public DateTime JOINED_AT_UTC { get; private set; }

    /// <summary><c>RemoteIpAddress</c> — may be a proxy's address until forwarded headers are configured
    /// (an existing gap shared with the playback log).</summary>
    public string? IP_ADDRESS { get; private set; }

    /// <summary>The request's <c>User-Agent</c>, cut to the column length.</summary>
    public string? USER_AGENT { get; private set; }

    /// <summary>Records one link reveal. <paramref name="joinedAtUtc"/> must be UTC; the IP address and
    /// user agent are trimmed and cut to their column lengths (blank becomes <c>null</c>).</summary>
    public static SESSION_JOIN_LOG Record(
        Guid sessionId,
        Guid courseId,
        Guid userId,
        LiveParticipantRole role,
        Guid? authSessionId,
        DateTime joinedAtUtc,
        string? ipAddress,
        string? userAgent)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course ID cannot be empty.", nameof(courseId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (joinedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("joinedAtUtc must be UTC (database.md: Npgsql throws on non-Utc DateTime).", nameof(joinedAtUtc));
        }

        return new SESSION_JOIN_LOG
        {
            SESSION_JOIN_LOG_ID = UuidV7.NewId(),
            SESSION_ID = sessionId,
            COURSE_ID = courseId,
            USER_ID = userId,
            ROLE = role,
            AUTH_SESSION_ID = authSessionId == Guid.Empty ? null : authSessionId,
            JOINED_AT_UTC = joinedAtUtc,
            IP_ADDRESS = Normalize(ipAddress, IpAddressMaxLength),
            USER_AGENT = Normalize(userAgent, UserAgentMaxLength),
        };
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
