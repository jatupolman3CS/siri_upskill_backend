using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Identity.Features.ConfirmEmail;
using Siri.Modules.Identity.Features.Admin;
using Siri.Modules.Identity.Features.ForgotPassword;
using Siri.Modules.Identity.Features.ListSessions;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Features.ResetPassword;
using Siri.Modules.Identity.Features.RevokeAllSessions;
using Siri.Modules.Identity.Features.RevokeOtherSessions;
using Siri.Modules.Identity.Features.RevokeSession;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.SharedKernel;

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
/// an HTTP endpoint — see that class's own doc comment; P1-03 adds this module's first real
/// <c>Contracts</c> surface (<see cref="IInstructorRoleGrantor"/>), so Catalog's
/// ApproveInstructorApplication handler can grant the Instructor role without reaching into this
/// module's Domain/Infrastructure directly.
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

        // P0-17: SE-03 session mirror. IConnectionMultiplexer itself is registered centrally by
        // Siri.Persistence.DependencyInjection.SharedRedisServiceCollectionExtensions.AddSharedRedis
        // (Program.cs calls it before AddIdentityModule) — relocated there for P1-01 once Catalog
        // became a second Redis consumer, see that method's own doc comment. Nothing here changes:
        // same "Redis" config section, same fail-open semantics, this module just resolves the
        // already-registered singleton instead of creating its own.
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

        // Admin ops handlers (P6-06)
        services.AddScoped<Features.Admin.GetAdminUsers.GetAdminUsersHandler>();
        services.AddScoped<Features.Admin.SuspendUser.SuspendUserHandler>();
        services.AddScoped<Features.Admin.ReactivateUser.ReactivateUserHandler>();
        services.AddScoped<Features.Admin.UpdateUserRoles.UpdateUserRolesHandler>();
        services.AddScoped<Features.Admin.GetAdminAuditLogs.GetAdminAuditLogsHandler>();

        // P1-03: this module's first real Contracts/ surface — Catalog's ApproveInstructorApplication
        // handler resolves this to grant the Instructor role, since Roles/UserRoles are this module's
        // data (see IInstructorRoleGrantor's own doc comment). Scoped, not singleton, like every other
        // handler above — it depends on the same scoped AppDbContext.
        services.AddScoped<IInstructorRoleGrantor, InstructorRoleGrantor>();

        // Second Contracts/ surface — Commerce resolves this to find the real email address to send a
        // paid order's receipt to (see IUserContactReader's own doc comment for why relying on Stripe's
        // PaymentIntent.ReceiptEmail alone was silently sending every receipt to a fabricated address).
        services.AddScoped<IUserContactReader, UserContactReader>();

        // Admin stats contract (P6-07)
        services.AddScoped<IIdentityStatsContract, Infrastructure.Contracts.IdentityStatsContract>();

        return services;
    }

    /// <summary>
    /// Maps the Identity module's minimal API endpoints onto the host's route builder.
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

        // Admin ops endpoints (P6-06)
        endpoints.MapAdminUserEndpoints();

        return endpoints;
    }
}
