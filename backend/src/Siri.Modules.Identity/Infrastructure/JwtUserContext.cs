using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Real, JWT-backed <see cref="IUserContext"/> — reads the authenticated principal that the JwtBearer
/// authentication handler (<c>Siri.Api/Program.cs</c>'s <c>AddJwtBearer</c>) attaches to the current
/// request, using exactly the claim types <see cref="AccessTokenGenerator"/> wrote into the token
/// (<see cref="ClaimTypes.NameIdentifier"/> for the user id, <see cref="ClaimTypes.Role"/> per role —
/// see that class's own doc comment for the full claim-shape rationale).
/// <para>
/// Takes over from <c>Siri.Persistence</c>'s <c>AnonymousUserContext</c> the moment this module
/// registers it: that placeholder is deliberately registered with <c>TryAddScoped</c> specifically so
/// a later, real registration wins (see its own doc comment) — <c>IdentityModule.AddIdentityModule</c>
/// registers this one with a plain <c>AddScoped</c> after <c>AddPersistence</c> runs in
/// <c>Program.cs</c>'s DI chain, so this implementation is what every handler actually resolves at
/// runtime.
/// </para>
/// <para>
/// Reads through <see cref="IHttpContextAccessor"/> rather than injecting <see cref="HttpContext"/>
/// directly — <see cref="HttpContext"/> is ambient framework state, not itself a constructor-injectable
/// scoped service, and <see cref="IHttpContextAccessor"/> is the documented, supported way for a
/// scoped/injected service like this one to reach it. Every member handles a <c>null</c>
/// <see cref="IHttpContextAccessor.HttpContext"/> (background jobs, design-time tooling, or any other
/// caller running outside a real HTTP request) the same way it handles an unauthenticated request —
/// as anonymous, never throwing.
/// </para>
/// </summary>
public sealed class JwtUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid? UserId
    {
        get
        {
            var principal = CurrentAuthenticatedPrincipalOrNull();
            if (principal is null)
            {
                return null;
            }

            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            var principal = CurrentAuthenticatedPrincipalOrNull();
            return principal is null
                ? []
                : principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        }
    }

    public bool IsAuthenticated => CurrentAuthenticatedPrincipalOrNull() is not null;

    private ClaimsPrincipal? CurrentAuthenticatedPrincipalOrNull()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true ? principal : null;
    }
}
