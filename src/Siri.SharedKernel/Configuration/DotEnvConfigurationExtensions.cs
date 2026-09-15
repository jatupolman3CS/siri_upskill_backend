using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Siri.SharedKernel.Configuration;

/// <summary>Loads optional local settings without replacing operator overrides or changing process state.</summary>
public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddSiriDotEnvDefaults(
        this IConfigurationBuilder configuration,
        IHostEnvironment environment,
        params string[] searchRoots) =>
        DotEnvLoader.AddDefaults(configuration, environment, searchRoots);

}
