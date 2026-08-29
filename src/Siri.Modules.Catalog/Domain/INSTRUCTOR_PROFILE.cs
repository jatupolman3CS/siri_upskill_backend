using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// An application to become an instructor, and — once <see cref="Approve"/>d — the instructor's public
/// profile (docs/DATABASE.md "catalog" section: "InstructorProfiles(Id PK, UserId FK UQ, DisplayName,
/// Headline, Bio, AvatarUrl, RevenueSharePercent decimal(5,2) default 70.00, Status, ApprovedAtUtc)").
/// One row per <see cref="UserId"/>, ever — see <see cref="Resubmit"/> for what happens after a rejection
/// instead of a second row.
/// <para>
/// <see cref="UserId"/> is a plain column with no database-level FK to <c>identity.Users</c> — a
/// cross-module/cross-schema FK is exactly the physical coupling docs/ARCHITECTURE.md §1 keeps modules as
/// separate projects to avoid, the same reasoning <c>CourseConfiguration</c>'s doc comment already gives
/// for <c>COURSE.TrailerMediaAssetId</c> never getting one either. Existence of the referenced user is an
/// application-layer concern for whichever handler writes it (<c>Features/ApplyAsInstructor/Handler.cs</c>
/// — the caller is always the authenticated <see cref="Siri.SharedKernel.IUserContext"/>, so the id is
/// trustworthy by construction, not something this entity needs to verify itself).
/// </para>
/// <para>
/// Granting the actual <c>Instructor</c> role (so <c>InstructorOnly</c>-gated endpoints work for this
/// user) is deliberately NOT this entity's concern either — <c>Roles</c>/<c>UserRoles</c> are
/// <c>Siri.Modules.Identity</c> data, reached only through that module's own <c>Contracts</c> namespace
/// (see <c>Features/ApproveInstructorApplication/Handler.cs</c>). This entity only tracks the
/// application/profile itself.
/// </para>
/// </summary>
public sealed class INSTRUCTOR_PROFILE : IAuditable
{
    /// <summary>docs/DATABASE.md: "RevenueSharePercent decimal(5,2) default 70.00" — the locked default
    /// from docs/DECISIONS.md's Q4 ("default ที่ใช้วางแผน: 70/30 ตั้งค่าได้ต่อ instructor"). Per-instructor
    /// overrides are Q4/P6-03's territory (still an open decision) — this task only ever writes the
    /// default; nothing here exposes a way to change it.</summary>
    public const decimal DefaultRevenueSharePercent = 70.00m;

    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private INSTRUCTOR_PROFILE()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>FKs to <c>identity.Users.Id</c> conceptually — see this class's own doc comment for why
    /// there is no database-level FK constraint.</summary>
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Short public tagline (e.g. "Senior Full-Stack Developer, 10+ ปีประสบการณ์") — optional,
    /// unlike <see cref="Bio"/>.</summary>
    public string? Headline { get; private set; }

    public string Bio { get; private set; } = string.Empty;

    /// <summary>Not settable through <see cref="Apply"/>/<see cref="Resubmit"/> — no avatar-upload
    /// feature exists yet (Media module is still a stub, same forward-reference situation
    /// <c>COURSE.ThumbnailUrl</c>/<c>TrailerMediaAssetId</c> are already in). Stays <c>null</c> until a
    /// later task wires up an upload flow and its own setter.</summary>
    public string? AvatarUrl { get; private set; }

    public decimal RevenueSharePercent { get; private set; }

    public InstructorApplicationStatus Status { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
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

    /// <summary>Submits a brand-new application. Starts <see cref="InstructorApplicationStatus.Pending"/>
    /// with <see cref="DefaultRevenueSharePercent"/> — the handler calling this is responsible for having
    /// already checked no <see cref="INSTRUCTOR_PROFILE"/> exists yet for <paramref name="userId"/> (this
    /// factory has no way to query that itself — same "existence/uniqueness is the handler's job" split
    /// <c>CATEGORY.Create</c>/<c>COURSE.Create</c> already draw).</summary>
    public static INSTRUCTOR_PROFILE Apply(Guid userId, string displayName, string? headline, string bio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bio);

        return new INSTRUCTOR_PROFILE
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            DisplayName = displayName,
            Headline = headline,
            Bio = bio,
            RevenueSharePercent = DefaultRevenueSharePercent,
            Status = InstructorApplicationStatus.Pending,
        };
    }

    /// <summary>
    /// Re-submits a previously <see cref="InstructorApplicationStatus.Rejected"/> application —
    /// overwrites the submitted fields and moves it back to
    /// <see cref="InstructorApplicationStatus.Pending"/> for another review pass, reusing this same row
    /// rather than leaving <c>UserId</c>'s unique constraint make a rejection permanent. Deliberately NOT
    /// exposed as "resubmit while still Pending" or "resubmit an Approved profile" — only a genuine
    /// rejection is something a second attempt makes sense for; the former is just a duplicate request,
    /// the latter is an already-instructor account (profile edits, if ever needed, are a different
    /// operation from re-applying).
    /// </summary>
    public void Resubmit(string displayName, string? headline, string bio)
    {
        if (Status != InstructorApplicationStatus.Rejected)
        {
            throw new InvalidOperationException($"Cannot resubmit an application in {Status} status.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bio);

        DisplayName = displayName;
        Headline = headline;
        Bio = bio;
        Status = InstructorApplicationStatus.Pending;
    }

    /// <summary>Approves a pending application. Granting the <c>Instructor</c> role is the caller's job
    /// (see this class's own doc comment) — this only flips this entity's own state.</summary>
    public void Approve(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != InstructorApplicationStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot approve an application in {Status} status.");
        }

        Status = InstructorApplicationStatus.Approved;
        ApprovedAtUtc = clock.UtcNow;
    }

    /// <summary>Rejects a pending application. No role to revoke — a <see cref="InstructorApplicationStatus.Pending"/>
    /// application never held the <c>Instructor</c> role in the first place.</summary>
    public void Reject()
    {
        if (Status != InstructorApplicationStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot reject an application in {Status} status.");
        }

        Status = InstructorApplicationStatus.Rejected;
    }
}
