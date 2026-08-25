using Siri.SharedKernel;

namespace Siri.Modules.Media.Domain;

/// <summary>
/// An immutable record of one playback token issuance — a pure forensics log, not an entitlement/device-
/// limit enforcement mechanism (docs/DECISIONS.md D-17's resolution of task P2-04's open question: device-
/// limit enforcement reuses Identity's existing <c>ISessionRegistry</c>/<c>UserSessions</c> via the JWT's
/// <c>sid</c> claim — a later task — not a parallel counting system built on this table). Written purely so
/// someone can investigate a leak after the fact (docs/DATABASE.md: "ใช้สืบสวนกรณีรั่วไหล"). Append-only —
/// deliberately no method to change a row after <see cref="Issue"/>, same shape
/// <c>Siri.Modules.Identity.Domain.SecurityAudit</c> already establishes for this kind of entity.
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>
/// — unlike <see cref="MEDIA_ASSET"/>/<see cref="MEDIA_UPLOAD_SESSION"/>, this row is never updated after
/// insert (its own <see cref="ISSUED_AT_UTC"/> is already the one timestamp that matters), so
/// <c>IAuditable</c>'s <c>UpdatedAtUtc</c>/<c>UpdatedBy</c> would never be meaningful — same reasoning
/// <c>SecurityAudit</c> gives for skipping it entirely rather than leaving them permanently null. Still
/// follows the UPPERCASE naming exception (D-17) — see <see cref="MEDIA_ASSET"/>'s own doc comment.
/// </para>
/// <para><see cref="USER_ID"/>/<see cref="EPISODE_ID"/>/<see cref="SESSION_ID"/> all carry no database-level
/// FK constraint — all cross-module/cross-schema (Identity/Catalog/Identity respectively), same reasoning
/// <see cref="MEDIA_ASSET.UPLOADED_BY_USER_ID"/>'s own doc comment already gives.</para>
/// <para>Entity + EF config + repository only in this scaffold pass — deliberately no dedicated
/// Service/Endpoints (docs/DECISIONS.md D-17); see <c>IPlaybackSessionRepository</c>'s own doc comment.</para>
/// </summary>
public sealed class PLAYBACK_SESSION
{
    /// <summary>EF Core materialization only.</summary>
    private PLAYBACK_SESSION()
    {
    }

    public Guid PLAYBACK_SESSION_ID { get; private set; }

    /// <summary>FKs to <c>identity.Users</c> conceptually — see class doc comment for why there is no FK
    /// constraint.</summary>
    public Guid USER_ID { get; private set; }

    /// <summary>FKs to <c>catalog.CourseEpisodes</c> conceptually — see class doc comment for why there is
    /// no FK constraint.</summary>
    public Guid EPISODE_ID { get; private set; }

    /// <summary>The Identity <c>UserSession.Id</c> this playback token was issued under (the JWT's own
    /// <c>sid</c> claim value, see class doc comment) — no FK constraint, same reasoning.</summary>
    public Guid SESSION_ID { get; private set; }

    public DateTime ISSUED_AT_UTC { get; private set; }

    public DateTime EXPIRES_AT_UTC { get; private set; }

    public string? IP_ADDRESS { get; private set; }

    public string? DEVICE_ID { get; private set; }

    /// <summary>Records one playback token issuance.</summary>
    public static PLAYBACK_SESSION Issue(
        Guid userId, Guid episodeId, Guid sessionId, DateTime expiresAtUtc, string? ipAddress, string? deviceId, IClock clock)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (episodeId == Guid.Empty)
        {
            throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        }

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(clock);

        return new PLAYBACK_SESSION
        {
            PLAYBACK_SESSION_ID = UuidV7.NewId(),
            USER_ID = userId,
            EPISODE_ID = episodeId,
            SESSION_ID = sessionId,
            ISSUED_AT_UTC = clock.UtcNow,
            EXPIRES_AT_UTC = expiresAtUtc,
            IP_ADDRESS = ipAddress,
            DEVICE_ID = deviceId,
        };
    }
}
