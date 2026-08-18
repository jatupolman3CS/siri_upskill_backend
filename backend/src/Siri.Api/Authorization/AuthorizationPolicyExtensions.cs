using Microsoft.AspNetCore.Authorization;
using Siri.Modules.Identity.Domain;

namespace Siri.Api.Authorization;

/// <summary>
/// Authorization policy composition for the API host (task P0-22) — see ARCHITECTURE.md §2's AuthZ
/// row: "policy-based: CourseOwner, EnrolledInCourse, AdminOnly, InstructorOnly". CORS and rate
/// limiting (Program.cs's other two cross-cutting <c>AddXxx(options => ...)</c> blocks) stay inline
/// in Program.cs, but authorization is pulled into its own extension method instead, specifically so
/// unit tests can build the exact same <see cref="AuthorizationOptions"/> Program.cs registers and
/// assert on the real policies via <c>IAuthorizationService</c> — see
/// <c>Siri.UnitTests.Authorization.AuthorizationPolicyTests</c> — rather than duplicating the policy
/// definitions inside test code (a duplicate could silently drift from the real one). CLAUDE.md rule
/// 10 calls out "การตัดสินสิทธิ์" (authorization decisions) as one of the things that must ship with
/// tests, unlike CORS/rate-limit config, which is why this one concern gets the extra indirection and
/// the other two don't.
/// </summary>
public static class AuthorizationPolicyExtensions
{
    /// <summary>Requires the caller's <see cref="System.Security.Claims.ClaimTypes.Role"/> claims to
    /// include <see cref="Role.AdminName"/> or <see cref="Role.SuperAdminName"/>.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Requires the caller's <see cref="System.Security.Claims.ClaimTypes.Role"/> claims to
    /// include <see cref="Role.InstructorName"/>, <see cref="Role.AdminName"/>, or
    /// <see cref="Role.SuperAdminName"/> — see this method's own doc comment for why Admin/SuperAdmin
    /// are deliberately included here too.</summary>
    public const string InstructorOnly = "InstructorOnly";

    /// <summary>
    /// Registers the two authorization policies this codebase can actually build today.
    /// <see cref="AdminOnly"/> and <see cref="InstructorOnly"/> both use <c>RequireRole(...)</c>,
    /// which is OR-semantics out of the box (<c>RolesAuthorizationRequirement</c> succeeds if the
    /// caller has <em>any one</em> of the listed roles) — exactly the shape both policies need:
    /// <list type="bullet">
    /// <item><b>AdminOnly</b> — Admin OR SuperAdmin. SuperAdmin is a strict superset of Admin, so a
    /// superadmin must never be locked out of an admin-only area; if it only checked "Admin", a
    /// SuperAdmin-only account (no separate "Admin" role also assigned) would fail every admin
    /// screen, which defeats the point of having a SuperAdmin tier at all.</item>
    /// <item><b>InstructorOnly</b> — Instructor OR Admin OR SuperAdmin (a deliberate design choice,
    /// not left implicit, per the task instruction that called this out explicitly). Admins
    /// occasionally need to act on instructor-only resources for support/moderation — e.g. fixing a
    /// course on an instructor's behalf, investigating a dispute — without the platform needing a
    /// second "impersonate instructor" mechanism just for that. This is one-directional: an Instructor
    /// role alone does <em>not</em> satisfy <see cref="AdminOnly"/> — admin access is never implied by
    /// being an instructor, only the reverse.</item>
    /// </list>
    /// <para>
    /// <b>Not defined here — <c>CourseOwner</c>/<c>EnrolledInCourse</c>:</b> both need real entities
    /// (<c>Course</c>, <c>Enrollment</c>) that do not exist yet — Catalog and Learning are still empty
    /// stubs (see <c>CatalogModule.cs</c>/<c>LearningModule.cs</c>, both no-op today) until much later
    /// in <c>docs/ROADMAP.md</c>. Building policy scaffolding against entities that don't exist yet
    /// would be speculative, against this codebase's established YAGNI discipline (the same reasoning
    /// <c>ISessionRegistry</c>'s own "phased design" doc comment applies elsewhere: don't build the
    /// abstraction before there's a second real thing to abstract over). When Catalog/Learning ship
    /// real <c>Course</c>/<c>Enrollment</c> types, add both policies here, following this exact same
    /// <c>options.AddPolicy(name, policy => ...)</c> shape — but expect <c>RequireAssertion(...)</c>
    /// handlers instead of <c>RequireRole(...)</c>, since ownership/enrollment is a per-resource id
    /// comparison against <c>IUserContext.UserId</c> (backend.md: "ต้องเช็ค ownership/สิทธิ์"), not a
    /// role claim.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSiriAuthorizationPolicies(this IServiceCollection services)
    {
        return services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminOnly, policy => policy.RequireRole(Role.AdminName, Role.SuperAdminName));

            options.AddPolicy(
                InstructorOnly,
                policy => policy.RequireRole(Role.InstructorName, Role.AdminName, Role.SuperAdminName));
        });
    }
}
