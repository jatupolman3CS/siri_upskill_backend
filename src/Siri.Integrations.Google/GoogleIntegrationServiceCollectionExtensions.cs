using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google;

public static class GoogleIntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Google integration: <see cref="GoogleOAuthOptions"/> (section <c>Integrations:Google</c>, validated on start),
    /// the two named <see cref="HttpClient"/>s, and either the real implementations or - only when <paramref name="useLogging"/> is
    /// true - the development-only fakes.
    /// <para>
    /// <paramref name="useLogging"/> is decided by the caller (the Live module reads <c>Live:Provider == "Logging"</c> at its own
    /// registration point, the same shape as <c>Email:Provider</c>). This method never selects the fakes on its own, and it
    /// never falls back to them: with no <c>ClientId</c> the real services stay registered and report <c>IsConfigured == false</c>.
    /// </para>
    /// <para>
    /// Services are singletons (stateless; <see cref="IHttpClientFactory"/> owns connection lifetime), so both the API
    /// request path and the Hangfire job in <c>Siri.Workers</c> can use them.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGoogleIntegration(this IServiceCollection services, IConfiguration configuration, bool useLogging = false)
    {
        services.TryAddSingleton<IClock, SystemClock>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GoogleOAuthOptions>, GoogleOAuthOptionsValidator>());
        var options = services
            .AddOptions<GoogleOAuthOptions>()
            .Bind(configuration.GetSection(GoogleOAuthOptions.SectionName));

        if (useLogging)
        {
            // The fake "consent screen" redirects straight back to our own callback, so it needs a callback URL.
            options.Validate(
                o => GoogleOAuthOptions.IsAcceptableRedirectUri(o.RedirectUri),
                $"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.RedirectUri)} is required when Live:Provider=Logging (the local callback URL).");
        }

        options.ValidateOnStart();

        AddClient(services, GoogleOAuthService.HttpClientName);
        AddClient(services, GoogleCalendarProvider.HttpClientName);

        if (useLogging)
        {
            services.AddSingleton<IGoogleOAuthService, LoggingGoogleOAuthService>();
            services.AddSingleton<ICalendarProvider, LoggingCalendarProvider>();
        }
        else
        {
            services.AddSingleton<IGoogleOAuthService, GoogleOAuthService>();
            services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
        }

        return services;
    }

    private static void AddClient(IServiceCollection services, string name) =>
        services
            .AddHttpClient(name)
            .ConfigureHttpClient((serviceProvider, client) =>
                client.Timeout = TimeSpan.FromSeconds(serviceProvider.GetRequiredService<IOptions<GoogleOAuthOptions>>().Value.HttpTimeoutSeconds))
            // Google's endpoints never redirect an API call; refusing redirects keeps a bearer token from ever following one.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
}
