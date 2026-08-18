using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Siri.Api.Authorization;
using Siri.Modules.Identity.Domain;

namespace Siri.UnitTests.Authorization;

/// <summary>
/// Exercises the exact policies <see cref="AuthorizationPolicyExtensions.AddSiriAuthorizationPolicies"/>
/// registers — the same extension method <c>Siri.Api/Program.cs</c> calls — via the real
/// <see cref="IAuthorizationService"/>, against hand-built <see cref="ClaimsPrincipal"/>s carrying the
/// exact <see cref="ClaimTypes.Role"/> claim shape <c>AccessTokenGenerator</c> writes into a real
/// access token.
/// <para>
/// Deliberately no HTTP round trip and no throwaway diagnostic endpoint added to production routing
/// (task P0-22's own suggested alternative: "test the policies directly via ASP.NET Core's
/// authorization testing primitives"). This is pure in-memory claims evaluation against a tiny,
/// self-contained DI container — no database, no Docker, no Testcontainers — so unlike the
/// database-backed Identity integration tests, this runs anywhere, including this environment.
/// </para>
/// <para>
/// An <see cref="AuthorizationResult"/> that fails here is the same underlying signal ASP.NET Core's
/// authorization middleware uses to choose between HTTP 401 (caller not authenticated at all) and 403
/// (authenticated but missing the required role) — that translation is standard framework behavior,
/// not something this codebase implements, so it is not re-tested here. What <em>is</em> this
/// codebase's own logic — which roles satisfy which policy — is exactly what these tests assert.
/// </para>
/// </summary>
public class AuthorizationPolicyTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // DefaultAuthorizationService needs ILogger<T> resolvable
        services.AddSiriAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth"); // non-null => IsAuthenticated == true
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal AnonymousPrincipal() =>
        new(new ClaimsIdentity()); // no authenticationType => IsAuthenticated == false

    [Theory]
    [InlineData(Role.AdminName)]
    [InlineData(Role.SuperAdminName)]
    public async Task AuthorizeAsync_AdminOnly_CallerHasAdminOrSuperAdminRole_Succeeds(string role)
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyExtensions.AdminOnly);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(Role.LearnerName)]
    [InlineData(Role.InstructorName)]
    public async Task AuthorizeAsync_AdminOnly_CallerHasNonAdminRole_Fails(string role)
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyExtensions.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_AdminOnly_UnauthenticatedCaller_Fails()
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(AnonymousPrincipal(), AuthorizationPolicyExtensions.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(Role.InstructorName)]
    [InlineData(Role.AdminName)]
    [InlineData(Role.SuperAdminName)]
    public async Task AuthorizeAsync_InstructorOnly_CallerHasInstructorOrAdminOrSuperAdminRole_Succeeds(string role)
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyExtensions.InstructorOnly);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_CallerHasOnlyLearnerRole_Fails()
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(Role.LearnerName), AuthorizationPolicyExtensions.InstructorOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_UnauthenticatedCaller_Fails()
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(AnonymousPrincipal(), AuthorizationPolicyExtensions.InstructorOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_AdminOnly_CallerHasOnlyInstructorRole_Fails()
    {
        // One-directional check (documented on AddSiriAuthorizationPolicies): Instructor does NOT
        // imply Admin, only the reverse.
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(Role.InstructorName), AuthorizationPolicyExtensions.AdminOnly);

        Assert.False(result.Succeeded);
    }
}
