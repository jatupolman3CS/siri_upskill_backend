using Siri.Modules.Payout;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// Startup / runtime guard validating <see cref="PayoutOptions"/> corporate payer info (P6-04, X-9).
/// Rejects placeholder values in environments where real 50 ทวิ tax certificates or production
/// payouts would be generated — at startup in Production (<see cref="EnsureRealPayerInfoConfigured"/>) and
/// at the moment a certificate is requested in every environment (<see cref="GetPayerInfoProblems"/>), so a
/// certificate is never printed with a placeholder payer.
/// </summary>
public static class PayoutOptionsGuard
{
    public const string PlaceholderPayerCompanyName = "CHANGE_ME_DEV_ONLY";
    public const string PlaceholderPayerTaxId = "0000000000000";

    /// <summary>
    /// Human-readable problems (never including values) with the configured payer identity; empty when the
    /// payer company name, 13-digit tax id and address are all real.
    /// </summary>
    public static IReadOnlyList<string> GetPayerInfoProblems(PayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.PayerCompanyName) ||
            string.Equals(options.PayerCompanyName, PlaceholderPayerCompanyName, StringComparison.Ordinal) ||
            options.PayerCompanyName.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Missing or invalid '{PayoutOptions.SectionName}:PayerCompanyName'. The placeholder ('{PlaceholderPayerCompanyName}') " +
                "is not valid for production tax certificates. Configure real corporate entity information.");
        }

        if (string.IsNullOrWhiteSpace(options.PayerTaxId) ||
            string.Equals(options.PayerTaxId, PlaceholderPayerTaxId, StringComparison.Ordinal) ||
            options.PayerTaxId.Length != 13 ||
            !options.PayerTaxId.All(char.IsDigit))
        {
            errors.Add(
                $"'{PayoutOptions.SectionName}:PayerTaxId' must be a valid 13-digit Thai corporate Tax ID (cannot be '{PlaceholderPayerTaxId}').");
        }

        if (string.IsNullOrWhiteSpace(options.PayerAddress) ||
            options.PayerAddress.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Missing or invalid '{PayoutOptions.SectionName}:PayerAddress'. Configure real corporate address information for tax certificates.");
        }

        return errors;
    }

    public static void EnsureRealPayerInfoConfigured(PayoutOptions options)
    {
        var errors = GetPayerInfoProblems(options);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", errors));
        }
    }
}
