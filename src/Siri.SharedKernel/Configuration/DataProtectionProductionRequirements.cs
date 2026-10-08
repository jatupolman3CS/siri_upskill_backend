using Microsoft.Extensions.Configuration;

namespace Siri.SharedKernel.Configuration;

/// <summary>
/// What a Production host must have configured for <see cref="SensitiveDataProtector"/> to be safe. Shared by every process that loads the
/// protector — the API's <c>ProductionConfigurationGuard</c> and the Workers host (which decrypts stored Google refresh tokens in the
/// live-meeting-sync job) — so neither can boot in Production with the placeholder or the dev key that ships in <c>appsettings.json</c>.
/// <para>
/// Why the Workers host matters: it has its own <c>appsettings.json</c> carrying the same dev key. A Workers container started without the
/// real key would boot happily, then be unable to read anything the API encrypted with the real one.
/// </para>
/// </summary>
public static class DataProtectionProductionRequirements
{
    /// <summary>The configuration key checked (<c>DataProtection:EncryptionKeyBase64</c>).</summary>
    public const string ConfigurationKey = "DataProtection:EncryptionKeyBase64";

    /// <summary>The "fill me in" placeholder an operator may have left in a deploy file.</summary>
    public const string DevKeyPlaceholder = "CHANGE_ME_IN_PRODUCTION_BASE64_32_BYTES_KEY==";

    /// <summary>The randomly generated dev key committed to <c>appsettings.json</c> of the API and Workers (not a secret — it is in the repository).</summary>
    public const string ShippedDevelopmentKeyBase64 = "wb8jlEpV/0t7ptEyiSdtRQOh5MXY4lde5L5WYxNfyIM=";

    /// <summary>Human-readable problems (never including the key itself); empty when the key is acceptable for production.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        var encKey = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(encKey) || string.Equals(encKey, DevKeyPlaceholder, StringComparison.Ordinal))
        {
            problems.Add($"{ConfigurationKey} must be set to a secure, real key in production (not default placeholder).");
            return problems;
        }

        try
        {
            var bytes = Convert.FromBase64String(encKey);
            if (bytes.Length != 32)
            {
                problems.Add($"{ConfigurationKey} must decode to exactly 32 bytes (256-bit key). Got {bytes.Length} bytes.");
            }
            else if (bytes.AsSpan().SequenceEqual(Convert.FromBase64String(ShippedDevelopmentKeyBase64)))
            {
                problems.Add($"{ConfigurationKey} must be replaced with a secure production key; the shipped development key is not allowed.");
            }
        }
        catch (FormatException)
        {
            problems.Add($"{ConfigurationKey} is not a valid Base64 string.");
        }

        return problems;
    }
}
