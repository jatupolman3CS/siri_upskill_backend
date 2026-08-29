using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="IAccessTokenGenerator"/> implementation backed by <see cref="JwtSecurityTokenHandler"/>
/// (HS256, symmetric key from <see cref="JwtOptions.SigningKey"/> — matches
/// <c>Siri.Api/Program.cs</c>'s <c>AddJwtBearer</c> validation, which reads the exact same
/// configuration section, see <see cref="JwtOptions"/>'s own doc comment for why that's guaranteed to
/// stay in sync).
/// <para>
/// <b>Claim shape</b> (task P0-16: "get the claim shape right and reasonably conventional ... rather
/// than inventing a bespoke shape" — this is what a later authorization-policy task, P0-22, builds
/// on):
/// <list type="bullet">
/// <item><see cref="JwtRegisteredClaimNames.Sub"/> ("sub") — the JWT-standard subject claim, and
/// <see cref="ClaimTypes.NameIdentifier"/> — the ASP.NET Core-idiomatic one
/// (<c>HttpContext.USER.FindFirstValue(ClaimTypes.NameIdentifier)</c>/<c>USER.Identity.Name</c>-style
/// lookups). Both carry the same <see cref="USER.Id"/> value; writing both means the token is legible
/// to generic JWT tooling *and* falls out of ASP.NET Core's own conventions for free — see
/// <see cref="JwtUserContext"/>, which reads it back via <see cref="ClaimTypes.NameIdentifier"/>.</item>
/// <item><see cref="ClaimTypes.ROLE"/> — one claim per assigned <see cref="ROLE"/>'s
/// <see cref="ROLE.Name"/>. Deliberately <see cref="ClaimTypes.ROLE"/> (not a custom "roles" claim
/// type): ASP.NET Core's built-in role-based authorization
/// (<c>[Authorize(Roles = "...")]</c>/<c>ClaimsPrincipal.IsInRole</c>) specifically looks for this
/// claim type by default (<c>TokenValidationParameters.RoleClaimType</c>'s default), so role checks
/// "just work" without any extra configuration once P0-22 adds real policies.</item>
/// <item><see cref="JwtRegisteredClaimNames.Jti"/> — a random per-token id. Not read by anything yet
/// in this task, but a near-zero-cost, conventional thing to include on every JWT (RFC 7519 §4.1.7)
/// that gives any future revocation-logging/tracing need a stable per-token handle to key off, rather
/// than retrofitting it once something needs it.</item>
/// <item><see cref="JwtRegisteredClaimNames.Sid"/> ("sid", the OpenID Connect Front-Channel Logout
/// spec's standard "session id" claim — task P0-18 adds this, it did not exist before). <b>Why this had
/// to be added, not merely reused</b>: P0-18's device-management API needs to answer "is the session
/// making THIS request the same row shown in the caller's own device list" — and nothing before this
/// task carried a <see cref="Domain.USER_SESSION.Id"/> anywhere an authenticated request could read it
/// back from. The refresh-token cookie does carry it indirectly (<see cref="Domain.REFRESH_TOKEN.SessionId"/>),
/// but that cookie is httpOnly (unreadable by the frontend that would need to know "is this my current
/// device"), is not guaranteed to be sent on every request that carries the access token (a non-browser
/// client, or a request replayed with only the bearer token), and would cost an extra DB round-trip
/// (hash + lookup) on every single request just to answer a question a JWT claim answers for free. So
/// this claim, not an alternate lookup, is P0-18's actual mechanism —
/// <see cref="Login.LoginHandler"/>/<see cref="Refresh.RefreshHandler"/> both pass the
/// <see cref="Domain.USER_SESSION.Id"/> the token is being minted for into <see cref="Generate"/> below,
/// and <c>Infrastructure.Endpoints.CurrentSessionClaim.Read</c> is the one place that reads it back out.
/// A schema-free change (no new column anywhere — this is purely a JWT claim), which is why P0-18 needs
/// no EF migration despite this design point.</item>
/// </list>
/// Deliberately does <b>not</b> include email/display name/other PII in the token — the token sits in
/// browser memory for its whole (short) lifetime, and callers that need that data can ask
/// dedicated "who am I" endpoints for it later; keeping the JWT itself minimal limits what a stolen
/// token actually exposes if the access-token-in-memory guarantee is ever broken by an XSS bug
/// elsewhere in the frontend.
/// </para>
/// <para>
/// Stateless aside from its injected <see cref="IOptions{TOptions}"/>/<see cref="IClock"/> (both
/// singleton-safe), so — same lifetime story as <see cref="SecurityTokenGenerator"/>/
/// <see cref="UserPasswordHasher"/> — safe to register as a singleton.
/// </para>
/// </summary>
public sealed class AccessTokenGenerator(IOptions<JwtOptions> options, IClock clock) : IAccessTokenGenerator
{
    public (string AccessToken, DateTime ExpiresAtUtc) Generate(USER user, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jwtOptions = options.Value;
        var now = clock.UtcNow;
        var expiresAtUtc = now.AddMinutes(jwtOptions.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.Name)));

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, expiresAtUtc);
    }
}
