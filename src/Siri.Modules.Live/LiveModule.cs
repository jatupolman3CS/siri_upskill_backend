using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Infrastructure;

namespace Siri.Modules.Live;

/// <summary>
/// Composition root for the Live module (docs/contracts/P11-03-live-module-google-meetings.md §3).
/// Repository+Service pattern with UPPERCASE entities (docs/DECISIONS.md D-17); endpoints are MVC
/// controllers hosted in <c>Siri.Api</c> (D-19), so there is no <c>MapLiveEndpoints</c>.
/// <para>
/// Both hosts call <see cref="AddLiveModule"/> (<c>Siri.Api</c> for the HTTP surface, <c>Siri.Workers</c> for the
/// <c>live-meeting-sync</c> job), so both need the same Google/Live configuration. Besides its own services it replaces
/// Catalog's two <em>null</em> seams with the real implementations — <see cref="ILiveMeetingSink"/> and
/// <see cref="ILiveMeetingReadinessReader"/> — and the schema is wired through <c>AppDbContext</c>'s scan of the loaded
/// <c>Siri.Modules.*</c> assemblies, which only sees this assembly once a host references a type from it.
/// </para>
/// </summary>
public static class LiveModule
{
    /// <summary>Registers the Live module's services into the container.</summary>
    public static IServiceCollection AddLiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---- Options -----------------------------------------------------------------------------
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LiveOptions>, LiveOptionsValidator>());
        services.AddOptions<LiveOptions>()
            .Bind(configuration.GetSection(LiveOptions.SectionName))
            // PublicBaseUrl/OrganizerEmail default from other configuration; PostConfigure runs before validation.
            .PostConfigure(options => LiveOptions.ApplyDefaults(options, configuration[LiveOptions.FallbackPublicBaseUrlKey]))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ---- Google integration ------------------------------------------------------------------
        // Selecting the fake Google/Meet providers is an explicit, development-only act (Live:Provider=Logging),
        // read here — once, at the registration point — the same way Email:Provider selects the e-mail sender.
        // It is never a default and ProductionConfigurationGuard refuses it in Production.
        var useLogging = string.Equals(
            configuration[$"{LiveOptions.SectionName}:Provider"]?.Trim(),
            nameof(LiveProviderMode.Logging),
            StringComparison.OrdinalIgnoreCase);
        services.AddGoogleIntegration(configuration, useLogging);

        // ---- Persistence / application services (Repository + Service, one service per aggregate) ----
        services.AddScoped<IInstructorGoogleAccountRepository, InstructorGoogleAccountRepository>();
        services.AddScoped<ISessionMeetingRepository, SessionMeetingRepository>();
        services.AddSingleton<IGoogleOAuthStateStore, RedisGoogleOAuthStateStore>();

        services.AddScoped<MeetingLinkValidator>();
        services.AddScoped<IInstructorAlertSender, InstructorAlertSender>();
        services.AddScoped<InstructorGoogleAccountService>();
        services.AddScoped<SessionMeetingService>();

        services.AddScoped<IValidator<GoogleConnectCommand>, GoogleConnectCommandValidator>();
        services.AddScoped<IValidator<SetMeetingLinkCommand>, SetMeetingLinkCommandValidator>();

        // ---- Invites, calendar files and reminders (P11-04) --------------------------------------------
        services.AddScoped<ISessionInviteRepository, SessionInviteRepository>();
        services.AddScoped<SessionInviteService>();
        services.AddScoped<GoogleAttendeeSyncService>();

        // ---- Learner/instructor API + join gate (P11-05) -----------------------------------------------
        services.AddScoped<ISessionJoinLogRepository, SessionJoinLogRepository>();
        services.AddScoped<ISessionInviteReader, SessionInviteReader>();
        services.AddScoped<SessionJoinService>();
        services.AddScoped<LiveLearnerQueries>();
        services.AddScoped<LiveInstructorQueries>();

        // The attendance facts Live publishes to Commerce (refund rule, P11-12) and Analytics (dashboard, P11-10).
        services.AddScoped<ILiveAttendanceReader, LiveAttendanceReader>();

        // ---- Cross-module implementations (Catalog declares the contracts; a plain AddScoped here wins over its
        // TryAdd* null defaults whatever the registration order is) -----------------------------------------
        services.AddScoped<ILiveMeetingSink, LiveMeetingSink>();
        services.AddScoped<ILiveMeetingReadinessReader, LiveMeetingReadinessReader>();

        // ---- Jobs (scheduled in Siri.Workers' RecurringJobsRegistration) ---------------------------
        services.AddScoped<LiveMeetingSyncJob>();
        services.AddScoped<LiveInviteReconcileJob>();
        services.AddScoped<LiveSessionRemindersJob>();

        return services;
    }
}
