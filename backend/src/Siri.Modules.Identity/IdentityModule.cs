using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Features.ConfirmEmail;
using Siri.Modules.Identity.Features.ForgotPassword;
using Siri.Modules.Identity.Features.ListSessions;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Features.ResetPassword;
using Siri.Modules.Identity.Features.RevokeAllSessions;
using Siri.Modules.Identity.Features.RevokeOtherSessions;
using Siri.Modules.Identity.Features.RevokeSession;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.Modules.Identity;

/// <summary>
/// Composition root for the Identity module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Domain entities + EF configuration landed with P0-14; P0-15 added the first real feature
/// handlers/endpoints (Register, ConfirmEmail); P0-16 adds Login + refresh-token rotation, real JWT
/// authentication (<see cref="JwtOptions"/>/<see cref="AccessTokenGenerator"/>), and the real,
/// JWT-backed <see cref="IUserContext"/> (<see cref="JwtUserContext"/>) that takes over from
/// <c>Siri.Persistence</c>'s anonymous placeholder; P0-17 adds SE-03 concurrent-session enforcement
/// (<see cref="ConcurrentSessionOptions"/>, the Redis mirror behind <see cref="ISessionRegistry"/>) into
/// Login's success path; P0-22 adds the real, JWT-backed AdminOnly/InstructorOnly authorization
/// policies + default-deny at this module's route group; P0-21 adds the "forgot password" flow
/// (ForgotPassword issues a <see cref="Domain.UserSecurityTokenPurpose.PasswordReset"/> token,
/// ResetPassword redeems it — reusing the exact <see cref="Domain.UserSecurityToken"/>/token-generator
/// mechanism ConfirmEmail already established, not a second one); P0-18 (this task) adds the
/// device-management API (ListSessions, RevokeSession, RevokeOtherSessions, RevokeAllSessions) — the
/// backend for docs/SECURITY.md §2's "ผู้ใช้ดู/ถอดอุปกรณ์เองได้ที่หน้า 'อุปกรณ์ที่เข้าสู่ระบบ'" — and, to
/// make that possible, adds a "sid" (session id) claim to every access token
/// <see cref="Infrastructure.AccessTokenGenerator"/> issues (see that class's own doc comment for why);
/// P0-37 adds <see cref="Infrastructure.Seeding.IdentitySeeder"/>, registered here the same way every
/// other handler is but invoked from <c>Siri.Api/Program.cs</c>'s <c>--seed</c> CLI flag rather than
/// an HTTP endpoint — see that class's own doc comment.
/// </summary>
public static class IdentityModule
{
    /// <summary>Registers the Identity module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Stateless/thread-safe wrappers — singleton is fine, same reasoning as
        // UserPasswordHasher's own doc comment.
        services.AddSingleton<IUserPasswordHasher, UserPasswordHasher>();
        services.AddSingleton<ISecurityTokenGenerator, SecurityTokenGenerator>();
        services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();

        services.AddOptions<EmailConfirmationOptions>()
            .Bind(configuration.GetSection(EmailConfirmationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // P0-21: same options-pattern shape as EmailConfirmationOptions above, for the "forgot
        // password" flow's reset-link base URL.
        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // P0-37 dev/test bootstrap. Deliberately NOT .ValidateDataAnnotations().ValidateOnStart()
        // like every Options type above — see SeedOptions' own doc comment for why: this only backs
        // the opt-in `--seed` CLI action, not the normal web-server startup path every other Options
        // type here participates in unconditionally. SeedOptionsGuard validates it the moment
        // IdentitySeeder actually runs instead.
        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<IdentitySeeder>();

        // P0-17 (SE-03): system-wide default concurrent-session limit — see ConcurrentSessionOptions'
        // own doc comment for how this combines with User.MaxConcurrentSessionsOverride.
        services.AddOptions<ConcurrentSessionOptions>()
            .Bind(configuration.GetSection(ConcurrentSessionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // P0-17: Redis connection for the SE-03 session mirror (see ISessionRegistry's "phased
        // design" doc comment). ValidateOnStart() only checks the *configuration value* is present —
        // it does not attempt a real TCP connection at host startup (that only happens lazily, the
        // first time IConnectionMultiplexer below is actually resolved), so a missing/malformed
        // config value fails fast at boot the same way ConnectionStrings:Default/Jwt:SigningKey do,
        // but an unreachable Redis *server* does not block startup.
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: IConnectionMultiplexer is explicitly documented by StackExchange.Redis as safe
        // and intended to be shared/reused across the app's lifetime, never created per-request.
        // AbortOnConnectFail = false is the load-bearing part of this task's fail-open design — it
        // means Connect() below returns a (possibly not-yet-connected) multiplexer instead of
        // throwing when Redis is temporarily unreachable, so neither this factory nor anything that
        // resolves ISessionRegistry from it can turn "Redis is down" into "login/refresh throws".
        // RedisSessionRegistry's per-call try/catch is what handles the still-possible
        // command-level failures (e.g. a connection that is mid-retry) once resolution succeeds.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var configurationOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configurationOptions);
        });
        services.AddSingleton<ISessionRegistry, RedisSessionRegistry>();

        // Real, JWT-backed IUserContext — registered with a plain AddScoped (not TryAdd) specifically
        // so it takes over from Siri.Persistence's AnonymousUserContext placeholder, which is
        // registered with TryAddScoped for exactly this reason (see that class's own doc comment).
        // Must run after AddPersistence in Program.cs's DI chain for that override to win — it does.
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, JwtUserContext>();

        // FluentValidation validators, resolved by ValidationEndpointFilter<T> per endpoint. Refresh
        // has none: its command is never bound from a client-supplied JSON body (see
        // Features/Refresh/Endpoint.cs's own doc comment), so there is nothing for that filter to run
        // against.
        services.AddScoped<IValidator<RegisterCommand>, RegisterValidator>();
        services.AddScoped<IValidator<ConfirmEmailCommand>, ConfirmEmailValidator>();
        services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
        services.AddScoped<IValidator<ForgotPasswordCommand>, ForgotPasswordValidator>();
        services.AddScoped<IValidator<ResetPasswordCommand>, ResetPasswordValidator>();

        // P0-18's four device-management handlers have no FluentValidation validators registered here,
        // unlike every handler above — none of their commands are bound from a client-supplied JSON
        // body (ListSessions/RevokeOtherSessions/RevokeAllSessions take no client input at all;
        // RevokeSession's only client input is a route-bound Guid ASP.NET Core's routing already
        // validates the shape of), so there is nothing for ValidationEndpointFilter<T> to run against —
        // same reasoning RefreshEndpoint's own doc comment gives for Refresh having none either.

        // Scoped: all depend on the scoped AppDbContext, same lifetime story as
        // Notification's EmailOutboxSenderJob.
        services.AddScoped<RegisterHandler>();
        services.AddScoped<ConfirmEmailHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<ListSessionsHandler>();
        services.AddScoped<RevokeSessionHandler>();
        services.AddScoped<RevokeOtherSessionsHandler>();
        services.AddScoped<RevokeAllSessionsHandler>();

        return services;
    }

    /// <summary>
    /// Maps the Identity module's minimal API endpoints onto the host's route builder.
    /// <para>
    /// <b>Default-deny at the group level (task P0-22):</b> <c>.RequireAuthorization()</c> on the
    /// group applies to every endpoint mapped through it — backend.md: "ตั้ง .RequireAuthorization()
    /// ที่ระดับ group แล้วค่อย .AllowAnonymous() เป็นราย endpoint (default deny)". Register/
    /// ConfirmEmail/Login/Refresh were the four genuinely public actions in this module when this was
    /// retrofitted (P0-22); P0-21 added two more of the same kind — ForgotPassword/ResetPassword — each
    /// opting back out individually via its own <c>.AllowAnonymous()</c> (see each Feature's
    /// Endpoint.cs), for the same reason: neither can require an already-authenticated caller without
    /// defeating its own purpose. P0-18 (this task) adds the opposite case, proving the "default deny"
    /// half of this design actually works: ListSessions/RevokeSession/RevokeOtherSessions/
    /// RevokeAllSessions call <em>no</em> <c>.AllowAnonymous()</c> at all — managing your own devices
    /// requires being someone, so they simply inherit the group's default and need nothing extra to be
    /// correctly protected (verified concretely, not just assumed, by
    /// <c>tests/Siri.IntegrationTests/DeviceManagementTests.cs</c>'s 401-for-no-token tests). Any
    /// endpoint added to this group later that does
    /// <em>not</em> call <c>.AllowAnonymous()</c> requires a valid, authenticated caller by default.
    /// </para>
    /// <para>
    /// This is meant as the reference implementation of backend.md's rule, not an Identity-specific
    /// one — future modules' own <c>*Module.cs</c> (Catalog, Learning, Commerce, ...) should map their
    /// endpoints through a group with the exact same
    /// <c>MapGroup(prefix).RequireAuthorization()</c> + per-endpoint <c>.AllowAnonymous()</c> shape
    /// shown here, rather than each module inventing its own way to get default-deny.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity").WithTags("Identity").RequireAuthorization();

        group.MapRegisterEndpoint();
        group.MapConfirmEmailEndpoint();
        group.MapLoginEndpoint();
        group.MapRefreshEndpoint();
        group.MapForgotPasswordEndpoint();
        group.MapResetPasswordEndpoint();
        group.MapListSessionsEndpoint();
        group.MapRevokeSessionEndpoint();
        group.MapRevokeOtherSessionsEndpoint();
        group.MapRevokeAllSessionsEndpoint();

        return endpoints;
    }
}
