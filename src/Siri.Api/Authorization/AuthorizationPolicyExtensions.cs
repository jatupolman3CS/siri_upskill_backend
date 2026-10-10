using Microsoft.AspNetCore.Authorization;
using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;

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
    /// <summary>
    /// Registers the two authorization policies this codebase can actually build today.
    /// <see cref="AuthorizationPolicyNames.AdminOnly"/> and <see cref="AuthorizationPolicyNames.InstructorOnly"/>
    /// both use <c>RequireRole(...)</c>,
    /// which is OR-semantics out of the box (<c>RolesAuthorizationRequirement</c> succeeds if the
    /// caller has <em>any one</em> of the listed roles) — exactly the shape both policies need:
    /// <list type="bullet">
    /// <item><b>AdminOnly</b> — Admin OR SuperAdmin. SuperAdmin is a strict superset of Admin, so a
    /// superadmin must never be locked out of an admin-only area; if it only checked "Admin", a
    /// SuperAdmin-only account (no separate "Admin" role also assigned) would fail every admin
    /// screen, which defeats the point of having a SuperAdmin tier at all.</item>
    /// <item><b>InstructorOnly</b> — an <em>approved</em> Instructor, OR Admin OR SuperAdmin (a
    /// deliberate design choice, not left implicit, per the task instruction that called this out
    /// explicitly). Admins occasionally need to act on instructor-only resources for
    /// support/moderation — e.g. fixing a course on an instructor's behalf, investigating a
    /// dispute — without the platform needing a second "impersonate instructor" mechanism just for
    /// that. This is one-directional: an Instructor role alone does <em>not</em> satisfy
    /// <see cref="AuthorizationPolicyNames.AdminOnly"/> — admin access is never implied by being an
    /// instructor, only the reverse. The Instructor role is also not enough on its own for this
    /// policy: <see cref="ApprovedInstructorAuthorizationHandler"/> additionally requires an admin-approved
    /// application, so a user who merely holds the role (assigned by hand, invited, seeded) without an
    /// approval reaches none of the studio API.</item>
    /// </list>
    /// <para>
    /// <b>Not defined here — <c>CourseOwner</c>/<c>EnrolledInCourse</c>:</b> both need real entities
    /// (<c>Course</c>, <c>Enrollment</c>) that do not exist yet — Catalog ships its first real feature
    /// (Category, P1-01) before Course itself (P1-02), and Learning (home of <c>Enrollment</c>) is
    /// still an empty stub (see <c>LearningModule.cs</c>, no-op today) until much later in
    /// <c>docs/ROADMAP.md</c>. Building policy scaffolding against entities that don't exist yet
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
        // Scoped, not singleton: the handler reads the application status through a scoped DbContext-backed contract.
        // `AddAuthorization` plus `AddScoped<IAuthorizationHandler, ...>` is the supported way to add handlers (they are
        // resolved as `IEnumerable<IAuthorizationHandler>` per request).
        services.AddScoped<IAuthorizationHandler, ApprovedInstructorAuthorizationHandler>();

        return services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicyNames.AdminOnly,
                policy => policy.RequireRole(ROLE.AdminName, ROLE.SuperAdminName));

            options.AddPolicy(
                AuthorizationPolicyNames.InstructorOnly,
                policy => policy.RequireAuthenticatedUser().AddRequirements(new ApprovedInstructorRequirement()));
        });
    }
}
