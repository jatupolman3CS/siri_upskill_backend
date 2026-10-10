using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Domain;

namespace Siri.Api.Authorization;

/// <summary>
/// The requirement behind <c>InstructorOnly</c>: Admin/SuperAdmin, or an <c>Instructor</c> whose application an admin has approved.
/// </summary>
public sealed class ApprovedInstructorRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Evaluates <see cref="ApprovedInstructorRequirement"/>. The <c>Instructor</c> role in the token is necessary but not sufficient:
/// it can exist without an approved application (an admin assigning the role by hand, an invite, a seed), and an instructor who has
/// not been approved must not reach the studio API — the only instructor-facing endpoints they may call are the application
/// endpoints (<c>/api/catalog/instructors/apply</c>, <c>/me</c>), which sit behind plain authentication, not this policy. The approval
/// is read from the database on every evaluation (never cached in the token), so an admin rejecting or revoking takes effect on the
/// next request instead of when the access token expires.
/// <para>
/// Admin and SuperAdmin pass without a lookup: they use the studio for support/moderation (see <c>AddSiriAuthorizationPolicies</c>)
/// and are not applicants. Everything else, including an authenticated user with no usable id claim, fails closed.
/// </para>
/// </summary>
public sealed class ApprovedInstructorAuthorizationHandler(IServiceProvider services)
    : AuthorizationHandler<ApprovedInstructorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApprovedInstructorRequirement requirement)
    {
        var user = context.User;

        if (user.IsInRole(ROLE.AdminName) || user.IsInRole(ROLE.SuperAdminName))
        {
            context.Succeed(requirement);
            return;
        }

        if (!user.IsInRole(ROLE.InstructorName))
        {
            return;
        }

        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        // Resolved only here, once the caller actually holds the Instructor role: ASP.NET Core constructs every registered
        // IAuthorizationHandler when it evaluates ANY policy, so a constructor dependency on the Catalog contract would make
        // AdminOnly (and every host that never registers Catalog) fail too. Catalog always registers it in the real host.
        var approvalReader = services.GetRequiredService<IInstructorApprovalReader>();
        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await approvalReader.IsApprovedAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }
}
