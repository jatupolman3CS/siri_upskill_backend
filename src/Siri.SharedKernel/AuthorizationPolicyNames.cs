namespace Siri.SharedKernel;

/// <summary>
/// Names of the authorization policies <c>Siri.Api.Authorization.AuthorizationPolicyExtensions
/// .AddSiriAuthorizationPolicies()</c> registers. Kept here (not in <c>Siri.Api</c>, where the
/// policies are actually built) so any module can reference the policy name in its own
/// <c>.RequireAuthorization(...)</c> calls without referencing <c>Siri.Api</c> itself — <c>Siri.Api</c>
/// already references every module project, so a module referencing back would be a circular project
/// reference. Only the two name strings live here; the policy *definitions* (which need Identity's
/// <c>Role</c> entity, visible only to <c>Siri.Api</c> — SharedKernel must not reference module
/// projects) stay put.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>Requires the caller's role claims to include Admin or SuperAdmin.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Requires the caller's role claims to include Instructor, Admin, or SuperAdmin.</summary>
    public const string InstructorOnly = "InstructorOnly";
}
