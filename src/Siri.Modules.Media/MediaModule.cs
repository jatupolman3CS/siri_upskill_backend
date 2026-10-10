using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Video;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Media;

/// <summary>
/// Composition root for the Media module. Everything the module exposes to <c>Siri.Api</c> goes through
/// these two extension methods — no other public surface is wired into the host.
/// <para>
/// This module (docs/DECISIONS.md D-17) uses the Repository+Service pattern and UPPERCASE entity naming,
/// unlike Identity/Catalog/Notification's vertical-slice/PascalCase — see <c>.claude/rules/backend.md</c>
/// and each <c>Domain/*.cs</c> entity's own doc comment for the full reasoning. Every
/// <c>Application/*Service.cs</c> method is implemented for real as of 2026-08-24 and the module is wired
/// into the host — the old scaffold note was stale. What this module still lacks is tracked in
/// <c>docs/TASKS.md</c> (P2-05 DRM license proxy, P2-06 anomaly job), not here.
/// </para>
/// <para>
/// No Hangfire recurring job is registered here for <c>MEDIA_UPLOAD_SESSIONS</c> expiry sweeping
/// (<c>MEDIA_UPLOAD_SESSION.MarkExpired</c> exists on the entity but nothing calls it yet) — out of scope
/// for this scaffold pass, left for a later task alongside the real upload-session lifecycle.
/// </para>
/// </summary>
public static class MediaModule
{
    /// <summary>Registers the Media module's services (repositories, application services, validators,
    /// video provider integration) into the container.</summary>
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IMediaUploadSessionRepository, MediaUploadSessionRepository>();
        services.AddScoped<IPlaybackSessionRepository, PlaybackSessionRepository>();

        // Scoped: matches every other module's handler/service lifetime in this codebase.
        services.AddScoped<MediaAssetService>();
        services.AddScoped<MediaUploadSessionService>();
        services.AddScoped<PlaybackSessionService>();
        services.AddScoped<Siri.SharedKernel.Contracts.IMediaAssetContract, MediaAssetContractService>();
        services.AddScoped<Siri.SharedKernel.Contracts.IMediaIngestContract, MediaIngestContractService>(); // P11-13
        services.AddScoped<BunnyWebhookHandler>();
        services.AddScoped<PlaybackAnomalyDetectionJob>();
        services.AddScoped<Infrastructure.Seeding.MediaSeeder>();

        // P2-02: Bunny Stream video provider (IVideoProvider implementation).
        services.AddOptions<VideoProviderOptions>()
            .Bind(configuration.GetSection(VideoProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(BunnyVideoProvider.HttpClientName);

        // P11-13: server-side uploads (a Google Meet recording) run as long as the file takes, so this client has no timeout of its own - the caller's
        // cancellation token bounds it - and it never follows a redirect (a consumed request stream cannot be replayed).
        services.AddHttpClient(BunnyVideoProvider.UploadHttpClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
        services.AddScoped<IVideoProvider, BunnyVideoProvider>();

        // FluentValidation validators, resolved by ValidationEndpointFilter<T> per endpoint.
        // UpdateMediaAssetStatusCommand's validator is registered even though no endpoint binds it yet
        // (see that command's own doc comment) — cheap to have ready for whichever later task wires it to
        // an actual webhook/polling endpoint. No validator for the upload-session create action — it takes
        // no bindable command (see MediaUploadSessionService.CreateAsync's own doc comment).
        services.AddScoped<IValidator<CreateMediaAssetCommand>, CreateMediaAssetValidator>();
        services.AddScoped<IValidator<UpdateMediaAssetStatusCommand>, UpdateMediaAssetStatusValidator>();

        return services;
    }

    /// <summary>
    /// Maps the Media module's minimal API endpoints onto the host's route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/media").WithTags("Media");

        // Public webhook endpoint for Bunny Stream (AllowAnonymous + rate limited)
        group.MapBunnyWebhookEndpoints();

        // Authenticated groups
        var authGroup = group.RequireAuthorization();
        authGroup.MapPlaybackSessionEndpoints();

        var assetGroup = authGroup.MapGroup("/assets").RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);
        assetGroup.MapMediaAssetEndpoints();
        assetGroup.MapCreateMediaUploadSessionEndpoint();

        var uploadSessionGroup = authGroup.MapGroup("/upload-sessions").RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);
        uploadSessionGroup.MapMediaUploadSessionEndpoints();

        return endpoints;
    }
}
