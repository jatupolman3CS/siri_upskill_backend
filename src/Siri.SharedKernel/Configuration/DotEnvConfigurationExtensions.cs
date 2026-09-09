using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace Siri.SharedKernel.Configuration;

/// <summary>Loads optional local settings without replacing operator overrides or changing process state.</summary>
public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddSiriDotEnvDefaults(
        this IConfigurationBuilder configuration,
        IHostEnvironment environment,
        params string[] searchRoots)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Test and staging hosts must never discover a developer's or production's secret files.
        string[] fileNames;
        if (environment.IsDevelopment())
        {
            fileNames = [".env", ".env.local", ".env.development", ".env.development.local"];
        }
        else if (environment.IsProduction())
        {
            fileNames = [".env_prd", ".env.production"];
        }
        else
        {
            return configuration;
        }

        if (searchRoots.Length == 0)
        {
            searchRoots = [environment.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in searchRoots)
        {
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            {
                if (!visited.Add(directory.FullName))
                {
                    continue;
                }

                var files = fileNames.Select(name => Path.Combine(directory.FullName, name)).Where(File.Exists).ToArray();
                if (files.Length == 0)
                {
                    continue;
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    ReadValues(file, values);
                }

                // Default host providers contain appsettings, user-secrets, environment, then CLI.
                // Insert after appsettings only: all explicit secret/env/CLI providers retain priority.
                var insertIndex = 0;
                for (var index = 0; index < configuration.Sources.Count; index++)
                {
                    if (configuration.Sources[index] is JsonConfigurationSource { Path: { } path }
                        && Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                    {
                        insertIndex = index + 1;
                    }
                }

                configuration.Sources.Insert(insertIndex, new MemoryConfigurationSource { InitialData = values });
                return configuration;
            }
        }

        return configuration;
    }

    private static void ReadValues(string path, Dictionary<string, string?> values)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim().Replace("__", ":", StringComparison.Ordinal);
            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0 && value[0] is '"' or '\'')
            {
                var closingQuote = value.LastIndexOf(value[0]);
                if (closingQuote > 0)
                {
                    value = value[1..closingQuote];
                }
            }
            else
            {
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    value = value[..comment].TrimEnd();
                }
            }

            // Keep values literal: no shell interpolation, and never write environment variables.
            values[key] = value;
        }
    }
}
