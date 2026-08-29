using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>
/// A refresh token issued to one <see cref="USER_SESSION"/>. Supports rotation-with-reuse-detection
/// (SECURITY.md: "refresh token ... rotation + reuse detection") — that flow is a later task; this
/// is only the entity/schema it runs on. Only the token's hash is ever persisted, never the raw
/// value (security.md: "TokenHash เก็บ hash เท่านั้น").
/// </summary>
public sealed class REFRESH_TOKEN
{
    /// <summary>EF Core materialization only.</summary>
    private REFRESH_TOKEN()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Id of the token this one was rotated into, once rotated. <c>null</c> until then.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public static REFRESH_TOKEN Issue(Guid userId, Guid sessionId, string tokenHash, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new REFRESH_TOKEN
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            SessionId = sessionId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    /// <summary>Revokes this token, optionally recording the token it was rotated into
    /// (<paramref name="replacedByTokenId"/> is <c>null</c> for an outright revoke, e.g. logout or
    /// reuse-detection, and set for a normal rotation).</summary>
    public void Revoke(Guid? replacedByTokenId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (RevokedAtUtc is not null)
        {
            return; // idempotent — already revoked
        }

        RevokedAtUtc = clock.UtcNow;
        ReplacedByTokenId = replacedByTokenId;
    }
}
