using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Siri.Modules.Identity.Infrastructure;

namespace Siri.UnitTests.Identity;

public class JwtUserContextTests
{
    private static IHttpContextAccessor CreateAccessor(HttpContext? httpContext)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return accessor;
    }

    private static HttpContext CreateAuthenticatedHttpContext(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth"); // non-null authenticationType => IsAuthenticated == true
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void UserId_AuthenticatedPrincipalWithNameIdentifierClaim_ReturnsParsedGuid()
    {
        var userId = Guid.NewGuid();
        var context = new JwtUserContext(CreateAccessor(CreateAuthenticatedHttpContext(userId)));

        Assert.Equal(userId, context.UserId);
        Assert.True(context.IsAuthenticated);
    }

    [Fact]
    public void UserId_UnauthenticatedPrincipal_ReturnsNull()
    {
        var anonymousIdentity = new ClaimsIdentity(); // no authenticationType => IsAuthenticated == false
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(anonymousIdentity) };
        var context = new JwtUserContext(CreateAccessor(httpContext));

        Assert.Null(context.UserId);
        Assert.False(context.IsAuthenticated);
        Assert.Empty(context.Roles);
    }

    [Fact]
    public void UserId_NoHttpContext_ReturnsNullWithoutThrowing()
    {
        var context = new JwtUserContext(CreateAccessor(null));

        Assert.Null(context.UserId);
        Assert.False(context.IsAuthenticated);
        Assert.Empty(context.Roles);
    }

    [Fact]
    public void Roles_AuthenticatedPrincipalWithRoleClaims_ReturnsAllRoleValues()
    {
        var context = new JwtUserContext(CreateAccessor(CreateAuthenticatedHttpContext(Guid.NewGuid(), "Learner", "Instructor")));

        Assert.Equal(2, context.Roles.Count);
        Assert.Contains("Learner", context.Roles);
        Assert.Contains("Instructor", context.Roles);
    }
}
