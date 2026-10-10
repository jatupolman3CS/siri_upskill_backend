using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Siri.Api.Authorization;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;

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
    private static readonly Guid CallerUserId = Guid.Parse("0192b3c4-d5e6-7a8b-9c0d-1e2f3a4b5c6d");

    /// <summary>Stands in for Catalog's <see cref="IInstructorApprovalReader"/>: approves exactly the ids it was given, and records every lookup.</summary>
    private sealed class FakeApprovalReader(params Guid[] approvedUserIds) : IInstructorApprovalReader
    {
        public List<Guid> Lookups { get; } = [];

        public Task<bool> IsApprovedAsync(Guid userId, CancellationToken cancellationToken)
        {
            Lookups.Add(userId);
            return Task.FromResult(approvedUserIds.Contains(userId));
        }
    }

    private static IAuthorizationService BuildAuthorizationService(IInstructorApprovalReader? approvalReader = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // DefaultAuthorizationService needs ILogger<T> resolvable
        services.AddSingleton(approvalReader ?? new FakeApprovalReader());
        services.AddSiriAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles) => PrincipalWithRolesAndUserId(CallerUserId, roles);

    private static ClaimsPrincipal PrincipalWithRolesAndUserId(Guid? userId, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        if (userId is { } id)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString())); // the claim AccessTokenGenerator writes the user id into
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth"); // non-null => IsAuthenticated == true
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal AnonymousPrincipal() =>
        new(new ClaimsIdentity()); // no authenticationType => IsAuthenticated == false

    [Theory]
    [InlineData(ROLE.AdminName)]
    [InlineData(ROLE.SuperAdminName)]
    public async Task AuthorizeAsync_AdminOnly_CallerHasAdminOrSuperAdminRole_Succeeds(string role)
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyNames.AdminOnly);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(ROLE.LearnerName)]
    [InlineData(ROLE.InstructorName)]
    public async Task AuthorizeAsync_AdminOnly_CallerHasNonAdminRole_Fails(string role)
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyNames.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_AdminOnly_UnauthenticatedCaller_Fails()
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(AnonymousPrincipal(), AuthorizationPolicyNames.AdminOnly);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(ROLE.AdminName)]
    [InlineData(ROLE.SuperAdminName)]
    public async Task AuthorizeAsync_InstructorOnly_CallerHasAdminOrSuperAdminRole_SucceedsWithoutApplicationLookup(string role)
    {
        var reader = new FakeApprovalReader();
        var authorizationService = BuildAuthorizationService(reader);

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(role), AuthorizationPolicyNames.InstructorOnly);

        Assert.True(result.Succeeded);
        Assert.Empty(reader.Lookups); // admins are not applicants — no database hit for them
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_InstructorWithApprovedApplication_Succeeds()
    {
        var reader = new FakeApprovalReader(CallerUserId);
        var authorizationService = BuildAuthorizationService(reader);

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(ROLE.InstructorName), AuthorizationPolicyNames.InstructorOnly);

        Assert.True(result.Succeeded);
        Assert.Equal([CallerUserId], reader.Lookups); // the lookup is for the caller's own id from the token, nobody else's
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_InstructorRoleWithoutApprovedApplication_Fails()
    {
        // The Instructor role alone is not enough: pending, rejected, or no application at all (role assigned by hand / invited / seeded).
        var reader = new FakeApprovalReader(Guid.NewGuid());
        var authorizationService = BuildAuthorizationService(reader);

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(ROLE.InstructorName), AuthorizationPolicyNames.InstructorOnly);

        Assert.False(result.Succeeded);
        Assert.Equal([CallerUserId], reader.Lookups);
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_InstructorRoleWithoutUserIdClaim_FailsWithoutLookup()
    {
        var reader = new FakeApprovalReader(CallerUserId);
        var authorizationService = BuildAuthorizationService(reader);

        var result = await authorizationService.AuthorizeAsync(
            PrincipalWithRolesAndUserId(userId: null, ROLE.InstructorName),
            AuthorizationPolicyNames.InstructorOnly);

        Assert.False(result.Succeeded);
        Assert.Empty(reader.Lookups);
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_CallerHasOnlyLearnerRole_FailsWithoutLookup()
    {
        var reader = new FakeApprovalReader(CallerUserId); // even an "approved" id must not matter without the role
        var authorizationService = BuildAuthorizationService(reader);

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(ROLE.LearnerName), AuthorizationPolicyNames.InstructorOnly);

        Assert.False(result.Succeeded);
        Assert.Empty(reader.Lookups);
    }

    [Fact]
    public async Task AuthorizeAsync_InstructorOnly_UnauthenticatedCaller_Fails()
    {
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(AnonymousPrincipal(), AuthorizationPolicyNames.InstructorOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_AdminOnly_CallerHasOnlyInstructorRole_Fails()
    {
        // One-directional check (documented on AddSiriAuthorizationPolicies): Instructor does NOT
        // imply Admin, only the reverse.
        var authorizationService = BuildAuthorizationService();

        var result = await authorizationService.AuthorizeAsync(PrincipalWithRoles(ROLE.InstructorName), AuthorizationPolicyNames.AdminOnly);

        Assert.False(result.Succeeded);
    }
}
