using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;

namespace Siri.UnitTests.Identity;

public class AccessTokenGeneratorTests
{
    private static readonly JwtOptions TestJwtOptions = new()
    {
        Issuer = "https://api.siriupskill.test",
        Audience = "siriupskill-frontend-test",
        SigningKey = new string('k', 64), // >= 32 chars, satisfies JwtOptions' MinLength(32)
        AccessTokenLifetimeMinutes = 15,
    };

    private static AccessTokenGenerator CreateGenerator(FakeClock clock) =>
        new(Options.Create(TestJwtOptions), clock);

    [Fact]
    public void Generate_ReturnsTokenExpiringExactlyAccessTokenLifetimeMinutesFromNow()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");

        var (accessToken, expiresAtUtc) = generator.Generate(user, Guid.NewGuid());

        Assert.NotEmpty(accessToken);
        Assert.Equal(clock.UtcNow.AddMinutes(TestJwtOptions.AccessTokenLifetimeMinutes), expiresAtUtc);
    }

    [Fact]
    public void Generate_TokenContainsUserIdAsSubjectAndNameIdentifierClaims()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");

        var (accessToken, _) = generator.Generate(user, Guid.NewGuid());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.Id.ToString());
    }

    [Fact]
    public void Generate_TokenContainsOneRoleClaimPerAssignedRole()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("instructor@example.com", "INSTRUCTOR@EXAMPLE.COM", "hash", "Teacher One");
        user.AssignRole(new Role(Role.LearnerId, "Learner"));
        user.AssignRole(new Role(Role.InstructorId, "Instructor"));

        var (accessToken, _) = generator.Generate(user, Guid.NewGuid());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        var roleClaimValues = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
        Assert.Equal(2, roleClaimValues.Length);
        Assert.Contains("Learner", roleClaimValues);
        Assert.Contains("Instructor", roleClaimValues);
    }

    [Fact]
    public void Generate_UserWithNoRoles_TokenHasNoRoleClaims()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");

        var (accessToken, _) = generator.Generate(user, Guid.NewGuid());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public void Generate_TokenCarriesConfiguredIssuerAndAudience()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");

        var (accessToken, _) = generator.Generate(user, Guid.NewGuid());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Equal(TestJwtOptions.Issuer, jwt.Issuer);
        Assert.Contains(TestJwtOptions.Audience, jwt.Audiences);
    }

    [Fact]
    public void Generate_CalledTwice_ProducesDifferentTokens()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");
        var sessionId = Guid.NewGuid();

        var (first, _) = generator.Generate(user, sessionId);
        var (second, _) = generator.Generate(user, sessionId);

        Assert.NotEqual(first, second); // distinct jti per call, even for the identical user/session
    }

    /// <summary>P0-18's own addition — the "sid" claim <c>Infrastructure.Endpoints.CurrentSessionClaim</c>
    /// reads back out on a later request to determine "is this the current session".</summary>
    [Fact]
    public void Generate_TokenContainsSessionIdAsSidClaim()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");
        var sessionId = Guid.NewGuid();

        var (accessToken, _) = generator.Generate(user, sessionId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sid && c.Value == sessionId.ToString());
    }

    [Fact]
    public void Generate_CalledWithDifferentSessionIds_SidClaimReflectsEachOne()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var generator = CreateGenerator(clock);
        var user = User.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hash", "Student One");
        var sessionIdA = Guid.NewGuid();
        var sessionIdB = Guid.NewGuid();

        var (accessTokenA, _) = generator.Generate(user, sessionIdA);
        var (accessTokenB, _) = generator.Generate(user, sessionIdB);

        var jwtA = new JwtSecurityTokenHandler().ReadJwtToken(accessTokenA);
        var jwtB = new JwtSecurityTokenHandler().ReadJwtToken(accessTokenB);

        Assert.Equal(sessionIdA.ToString(), jwtA.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sid).Value);
        Assert.Equal(sessionIdB.ToString(), jwtB.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sid).Value);
    }
}
