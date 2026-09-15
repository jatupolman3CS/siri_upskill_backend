using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace Siri.SharedKernel.Configuration;

/// <summary>Finds and parses dotenv files for the application's configuration pipeline.</summary>
public static class DotEnvLoader
{
    private const int MaxParentLevels = 8;

    public static IConfigurationBuilder AddDefaults(
        IConfigurationBuilder configuration,
        IHostEnvironment environment,
        params string[] searchRoots)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

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
            for (var directory = SafeDirectory(root); directory is not null; directory = directory.Parent)
            {
                if (!visited.Add(directory.FullName))
                {
                    continue;
                }

                var values = LoadDefaults(directory.FullName, fileNames, stripInlineComments: true);
                if (values.Count == 0)
                {
                    continue;
                }

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

    public static IReadOnlyDictionary<string, string?> LoadDefaults(
        string startDirectory,
        IReadOnlyCollection<string> fileNames,
        bool stripInlineComments)
    {
        var files = FindFiles(startDirectory, fileNames);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            LoadFromFile(file, values, stripInlineComments);
        }

        return values;
    }

    public static IReadOnlyList<string> FindFiles(string startDirectory, IReadOnlyCollection<string> fileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentNullException.ThrowIfNull(fileNames);

        var dir = SafeDirectory(startDirectory);
        for (var level = 0; dir is not null && level <= MaxParentLevels; level++, dir = dir.Parent)
        {
            var files = fileNames
                .Select(fileName => Path.Combine(dir.FullName, fileName))
                .Where(File.Exists)
                .ToArray();

            if (files.Length > 0)
            {
                return files;
            }
        }

        return [];
    }

    private static void LoadFromFile(
        string filePath,
        IDictionary<string, string?> values,
        bool stripInlineComments)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            if (TryParse(line, stripInlineComments, out var key, out var value))
            {
                values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
            }
        }
    }

    private static DirectoryInfo? SafeDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    private static bool TryParse(string line, bool stripInlineComments, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return false;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed["export ".Length..].TrimStart();
        }

        var separator = trimmed.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        key = trimmed[..separator].Trim();
        if (!IsValidKey(key))
        {
            return false;
        }

        value = trimmed[(separator + 1)..].Trim();
        if (value.Length > 0 && value[0] is '"' or '\'')
        {
            var closingQuote = value.LastIndexOf(value[0]);
            if (closingQuote > 0)
            {
                value = value[1..closingQuote];
            }
        }
        else if (stripInlineComments)
        {
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0)
            {
                value = value[..comment].TrimEnd();
            }
        }

        return true;
    }

    private static bool IsValidKey(string key)
    {
        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.' && c != ':')
            {
                return false;
            }
        }

        return key.Length > 0;
    }
}
