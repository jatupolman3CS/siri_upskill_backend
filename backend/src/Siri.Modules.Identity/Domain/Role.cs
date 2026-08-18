namespace Siri.Modules.Identity.Domain;

/// <summary>
/// A fixed, platform-defined role (Learner/Instructor/Admin/SuperAdmin) — reference/lookup data,
/// not something users or admins create through the app. In practice the four rows are seeded via
/// <c>Infrastructure/RoleConfiguration.cs</c>'s <c>HasData</c> call (using anonymous objects, not
/// this constructor — <c>HasData</c> needs to work independently of constructor/setter
/// accessibility) using the fixed ids below, and every other instance is loaded from the database.
/// </summary>
public sealed class Role
{
    /// <summary>Fixed, hardcoded ids for the seeded system roles. Migrations must be deterministic
    /// (<c>HasData</c> can't use <see cref="Siri.SharedKernel.UuidV7"/>), and code elsewhere can use
    /// these constants to refer to a role without a database round-trip.</summary>
    public static readonly Guid LearnerId = new("00000000-0000-0000-0000-000000000001");

    public static readonly Guid InstructorId = new("00000000-0000-0000-0000-000000000002");

    public static readonly Guid AdminId = new("00000000-0000-0000-0000-000000000003");

    public static readonly Guid SuperAdminId = new("00000000-0000-0000-0000-000000000004");

    /// <summary>
    /// Exact <see cref="Name"/> strings for the four seeded system roles (task P0-22) — centralized
    /// here, next to the matching ids above, so the three places that must all agree on these exact
    /// literal values (<c>RoleConfiguration.HasData</c>'s seed data, <c>AccessTokenGenerator</c>'s
    /// <see cref="System.Security.Claims.ClaimTypes.Role"/> claims, and the <c>AdminOnly</c>/
    /// <c>InstructorOnly</c> policies' <c>RequireRole(...)</c> calls in <c>Siri.Api/Program.cs</c>)
    /// can never silently drift apart from a typo in one of them.
    /// </summary>
    public const string LearnerName = "Learner";

    public const string InstructorName = "Instructor";

    public const string AdminName = "Admin";

    public const string SuperAdminName = "SuperAdmin";

    /// <summary>EF Core materialization only.</summary>
    private Role()
    {
    }

    public Role(Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
}
