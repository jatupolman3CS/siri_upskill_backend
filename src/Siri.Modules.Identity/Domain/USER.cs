using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>
/// A platform account. Role (Learner/Instructor/Admin/SuperAdmin) is data attached through
/// <see cref="Roles"/>, not a subtype — the same person can hold more than one.
/// <para>
/// Protects its own invariants: every property has a private setter and can only change through
/// the methods below (backend.md: "Entity ต้องปกป้อง invariant ของตัวเอง: property เป็น private set,
/// เปลี่ยนสถานะผ่าน method"). Password <em>hashing</em> is deliberately not here — this entity only
/// stores the already-computed <see cref="PasswordHash"/>; computing it is an application-layer
/// concern (see <c>Infrastructure/UserPasswordHasher.cs</c>), wired up for real registration/login
/// in a later task.
/// </para>
/// </summary>
public sealed class USER : IAuditable
{
    private readonly List<ROLE> _roles = [];

    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private USER()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Upper-invariant form of <see cref="Email"/>, used for uniqueness/lookup.</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? AvatarUrl { get; private set; }

    public string? PhoneNumber { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTime? EmailConfirmedAtUtc { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    /// <summary>Per-account override for SE-03's concurrent-login limit (security.md: "ตั้งค่าได้ระดับ
    /// system และ override รายบัญชีได้"). <c>null</c> (the default for every account) means "use the
    /// system-wide default" (<c>ConcurrentSessionOptions.MaxConcurrentSessions</c>, Login's enforcement
    /// handler resolves the effective limit from this — this entity has no config dependency of its
    /// own, per backend.md's "ห้ามให้ Domain รู้จัก EF, HttpContext, หรือ DTO" spirit, which extends to
    /// not knowing about Options either). Set through <see cref="SetMaxConcurrentSessionsOverride"/>,
    /// never a public setter.</summary>
    public int? MaxConcurrentSessionsOverride { get; private set; }

    /// <summary>Roles currently assigned to this user. Mutate only through <see cref="AssignRole"/>/
    /// <see cref="RemoveRole"/> — never expose the backing list directly.</summary>
    public IReadOnlyCollection<ROLE> Roles => _roles.AsReadOnly();

    // ---- IAuditable ---------------------------------------------------------------------------
    // Implemented explicitly so the "real" properties below can keep private setters, per
    // IAuditable's own doc comment: the interceptor writes through EF change-tracker metadata, not
    // this interface, so explicit implementation is enough to satisfy the contract.
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>Registers a new account. Starts in <see cref="UserStatus.PendingEmailConfirmation"/>
    /// — callers still need to drive <see cref="ConfirmEmail"/> once the confirmation link is used.</summary>
    public static USER Register(string email, string normalizedEmail, string passwordHash, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new USER
        {
            Id = UuidV7.NewId(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Status = UserStatus.PendingEmailConfirmation,
            TwoFactorEnabled = false,
        };
    }

    public void ConfirmEmail(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != UserStatus.PendingEmailConfirmation)
        {
            throw new InvalidOperationException($"Cannot confirm email for a user in {Status} status.");
        }

        Status = UserStatus.Active;
        EmailConfirmedAtUtc = clock.UtcNow;
    }

    public void Suspend(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == UserStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot suspend a deleted user.");
        }

        Status = UserStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status != UserStatus.Suspended)
        {
            throw new InvalidOperationException($"Cannot reactivate a user in {Status} status.");
        }

        Status = UserStatus.Active;
    }

    /// <summary>Marks the account deleted (PDPA "right to erasure"). Anonymizing the actual
    /// PII fields is a handler-level concern for a later task — this only flips the status.</summary>
    public void Delete()
    {
        Status = UserStatus.Deleted;
    }

    /// <summary>
    /// Anonymizes the account per PDPA Right to Erasure / Anonymization (P7-04).
    /// Replaces PII (Email, NormalizedEmail, DisplayName, PhoneNumber, AvatarUrl) with sanitized
    /// synthetic values, resets password hash to an unmatchable cryptographic random string,
    /// disables 2FA, and sets status to <see cref="UserStatus.Deleted"/>.
    /// </summary>
    public void Anonymize(string anonymizedEmail, string anonymizedDisplayName, string unmatchablePasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anonymizedEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(anonymizedDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(unmatchablePasswordHash);

        Email = anonymizedEmail;
        NormalizedEmail = anonymizedEmail.ToUpperInvariant();
        DisplayName = anonymizedDisplayName;
        PhoneNumber = null;
        AvatarUrl = null;
        PasswordHash = unmatchablePasswordHash;
        TwoFactorEnabled = false;
        Status = UserStatus.Deleted;
    }

    public void RecordLogin(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        LastLoginAtUtc = clock.UtcNow;
    }

    /// <summary>
    /// Replaces <see cref="PasswordHash"/> with an already-computed hash (P0-21's "forgot password"
    /// flow calls this after redeeming a valid <see cref="USER_SECURITY_TOKEN"/> — see
    /// <c>Features/ResetPassword/Handler.cs</c>). Deliberately distinct from how <see cref="Register"/>
    /// sets the very first hash: that happens once, as part of bringing a brand-new account into
    /// existence, whereas this is a deliberate mutation of an already-existing account, so it gets its
    /// own named method rather than a public setter (backend.md: "property เป็น private set, เปลี่ยน
    /// สถานะผ่าน method") — the same "hashing is an application-layer concern, this entity only stores
    /// the result" split <see cref="Register"/>'s own doc comment already draws; the caller (via
    /// <see cref="Infrastructure.UserPasswordHasher"/>) computes <paramref name="newPasswordHash"/>
    /// before this is ever called.
    /// <para>
    /// Guards the one invariant that matters here: a <see cref="UserStatus.Deleted"/> account's
    /// password is never meaningful again (PDPA anonymize-in-place — see <see cref="Delete"/>'s doc
    /// comment), so changing it would be nonsensical, not merely unusual. Every other status
    /// (<see cref="UserStatus.Active"/>, <see cref="UserStatus.PendingEmailConfirmation"/>,
    /// <see cref="UserStatus.Suspended"/>) is allowed through at the domain level — <c>ResetPasswordHandler</c>
    /// separately restricts *its own* flow to <see cref="UserStatus.Active"/> accounts only (task
    /// instruction), but that is a handler-level policy decision about when a reset link should work,
    /// not a domain invariant this method itself needs to enforce.
    /// </para>
    /// </summary>
    public void ChangePassword(string newPasswordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        if (Status == UserStatus.Deleted)
        {
            throw new InvalidOperationException("Cannot change the password of a deleted account.");
        }

        PasswordHash = newPasswordHash;
    }

    public void EnableTwoFactor() => TwoFactorEnabled = true;

    public void DisableTwoFactor() => TwoFactorEnabled = false;

    /// <summary>Sets (or, with <c>null</c>, clears) this account's SE-03 concurrent-session-limit
    /// override. <c>value</c> must be a positive integer or <c>null</c> — zero or negative would mean
    /// either "no sessions ever allowed" (not what this account-management feature is for; suspend/
    /// delete the account instead) or a nonsensical limit, so both are rejected outright rather than
    /// silently clamped.</summary>
    public void SetMaxConcurrentSessionsOverride(int? value)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "MaxConcurrentSessionsOverride must be a positive integer, or null to use the system default.");
        }

        MaxConcurrentSessionsOverride = value;
    }

    public void AssignRole(ROLE role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (_roles.Any(r => r.Id == role.Id))
        {
            return; // idempotent — already has this role
        }

        _roles.Add(role);
    }

    public void RemoveRole(ROLE role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _roles.RemoveAll(r => r.Id == role.Id);
    }
}
