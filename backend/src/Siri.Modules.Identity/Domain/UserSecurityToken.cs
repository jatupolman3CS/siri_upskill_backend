using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>
/// A one-time, purpose-scoped security token issued to a <see cref="User"/> — email confirmation
/// today (P0-15), password reset later (P0-21, see <see cref="UserSecurityTokenPurpose.PasswordReset"/>).
/// Same shape/rules as <see cref="RefreshToken"/> deliberately: only the token's hash is ever
/// persisted, never the raw value (security.md: "TokenHash เก็บ hash เท่านั้น") — hashing (and
/// generating the raw value in the first place) is an application-layer concern, done by
/// <c>Infrastructure/ISecurityTokenGenerator.cs</c> before this type is ever touched, exactly the
/// same division of responsibility <see cref="RefreshToken.Issue"/> already uses.
/// <para>
/// SHA-256 (not PBKDF2/Argon2/BCrypt) is the right hash here, unlike <see cref="User.PasswordHash"/>:
/// a password hash must be deliberately slow and salted because the input space is small enough for
/// an attacker to brute-force offline (humans reuse short, guessable passwords) — but this token's
/// raw value is a full cryptographically random value from <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
/// (effectively unguessable by construction), so a fast, unsalted cryptographic hash is exactly what
/// you want: <see cref="ConfirmEmail"/> requests happen many times under normal use and a slow KDF
/// would just be needless CPU cost with no matching security benefit.
/// </para>
/// </summary>
public sealed class UserSecurityToken
{
    /// <summary>EF Core materialization only.</summary>
    private UserSecurityToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public UserSecurityTokenPurpose Purpose { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc is not null;

    public static UserSecurityToken Issue(Guid userId, UserSecurityTokenPurpose purpose, string tokenHash, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new UserSecurityToken
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public bool IsExpired(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return clock.UtcNow >= ExpiresAtUtc;
    }

    /// <summary>Not consumed yet and not expired — the single check a handler should make before
    /// trusting/redeeming a token. Deliberately does not distinguish *why* a token is invalid
    /// (already consumed vs. expired vs. never existed) in its own return value; callers must not
    /// surface that distinction to an API caller either (security.md's anti-enumeration spirit —
    /// telling an attacker "closer, but expired" vs. "wrong" narrows their guessing space).</summary>
    public bool IsValid(IClock clock) => !IsConsumed && !IsExpired(clock);

    /// <summary>
    /// Redeems this token. Rejects — throws, does not silently no-op — a second consumption or an
    /// expired token: unlike <see cref="RefreshToken.Revoke"/>'s (and the Notification module's
    /// <c>EmailOutboxMessage.RecordSent</c>) idempotent no-op style elsewhere in this codebase,
    /// letting a second <c>Consume</c> quietly succeed
    /// here would mean whatever privileged action the token grants (confirming an email today;
    /// resetting a password once P0-21 lands) could be repeated an unbounded number of times from a
    /// single leaked/observed token. Callers are expected to check <see cref="IsValid"/> first and
    /// treat a throw here as the same generic "invalid or expired" outcome, never a distinct one.
    /// </summary>
    public void Consume(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (IsConsumed)
        {
            throw new InvalidOperationException("This security token has already been consumed.");
        }

        if (IsExpired(clock))
        {
            throw new InvalidOperationException("This security token has expired.");
        }

        ConsumedAtUtc = clock.UtcNow;
    }
}
