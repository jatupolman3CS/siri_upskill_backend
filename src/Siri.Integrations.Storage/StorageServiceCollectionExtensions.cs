using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Siri.Integrations.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFileStorage"/> backed by Cloudflare R2. Safe to call from more than one module:
    /// the options and the singleton are registered with <c>TryAdd</c>/<c>AddOptions</c> semantics, so there is
    /// exactly one S3 client for the whole host.
    /// </summary>
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<R2StorageOptions>()
            .Bind(configuration.GetSection(R2StorageOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IFileStorage, R2FileStorage>();

        return services;
    }
}
