using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Siri.Modules.Identity.Infrastructure.Endpoints;

namespace Siri.UnitTests.Identity;

/// <summary>Pure unit tests for <see cref="CurrentSessionClaim.Read"/> — the "how is 'this is the
/// current session' determined" logic P0-18's task instructions specifically call out as isolable and
/// testable without a database. No HTTP pipeline/DI involved, same style as
/// <c>JwtUserContextTests</c>'s claim-reading tests.</summary>
public class CurrentSessionClaimTests
{
    private static ClaimsPrincipal PrincipalWithClaims(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    [Fact]
    public void Read_PrincipalWithValidSidClaim_ReturnsParsedGuid()
    {
        var sessionId = Guid.NewGuid();
        var principal = PrincipalWithClaims(new Claim(JwtRegisteredClaimNames.Sid, sessionId.ToString()));

        Assert.Equal(sessionId, CurrentSessionClaim.Read(principal));
    }

    [Fact]
    public void Read_PrincipalWithoutSidClaim_ReturnsNull()
    {
        var principal = PrincipalWithClaims(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        Assert.Null(CurrentSessionClaim.Read(principal));
    }

    [Fact]
    public void Read_PrincipalWithMalformedSidClaim_ReturnsNullRatherThanThrowing()
    {
        var principal = PrincipalWithClaims(new Claim(JwtRegisteredClaimNames.Sid, "not-a-guid"));

        Assert.Null(CurrentSessionClaim.Read(principal));
    }

    [Fact]
    public void Read_AnonymousPrincipalWithNoClaimsAtAll_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(CurrentSessionClaim.Read(principal));
    }
}
