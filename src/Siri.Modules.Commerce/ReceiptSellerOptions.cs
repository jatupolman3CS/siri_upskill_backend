using Microsoft.Extensions.Configuration;

namespace Siri.Modules.Commerce;

/// <summary>
/// The legal entity that SELLS on the receipts / tax invoices the platform issues. Bound from
/// configuration section <see cref="SectionName"/> (<c>Commerce:Seller</c>).
/// <para>
/// Real data only: a receipt/tax invoice carries the seller's registered name, tax id and address, so
/// there is deliberately no built-in default company. The platform company is the same legal entity the
/// payout module already needs (<c>Payout:PayerCompanyName</c> / <c>PayerTaxId</c> / <c>PayerAddress</c>), so
/// when a <c>Commerce:Seller</c> value is not set it falls back to that one (see <see cref="Resolve"/>) — the
/// company's identity is configured once. While <see cref="CompanyName"/>, <see cref="TaxId"/> or
/// <see cref="Address"/> is still missing/placeholder after that, generating a receipt PDF fails with
/// <see cref="ReceiptErrors.SellerNotConfiguredCode"/> (HTTP 503) rather than printing an invented seller;
/// <c>ProductionConfigurationGuard</c> refuses to boot Production in that state. <see cref="BranchLabel"/>,
/// <see cref="ContactEmail"/> and <see cref="Website"/> are optional — when blank they are simply left off
/// the document, never made up.
/// </para>
/// <para>
/// No <c>ValidateOnStart</c> data-annotation rules on purpose (same shape as <c>StripeOptions</c>): a
/// missing value must not stop non-Production hosts from booting; it surfaces as a 503 on the receipt
/// endpoints instead.
/// </para>
/// </summary>
public sealed class ReceiptSellerOptions
{
    public const string SectionName = "Commerce:Seller";

    /// <summary>The payout module's payer identity — the same legal entity — used when a seller value is unset.</summary>
    public const string FallbackCompanyNameKey = "Payout:PayerCompanyName";

    /// <inheritdoc cref="FallbackCompanyNameKey"/>
    public const string FallbackTaxIdKey = "Payout:PayerTaxId";

    /// <inheritdoc cref="FallbackCompanyNameKey"/>
    public const string FallbackAddressKey = "Payout:PayerAddress";

    /// <summary>Registered legal name, e.g. the company name exactly as on the VAT registration.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Branch designation printed after the name (e.g. head office / "สาขาที่ 00001"); optional.</summary>
    public string BranchLabel { get; set; } = string.Empty;

    /// <summary>13-digit Thai tax identification number of the seller.</summary>
    public string TaxId { get; set; } = string.Empty;

    /// <summary>Registered address printed on the document.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Customer-facing contact email; optional.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Public website; optional.</summary>
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Binds <see cref="SectionName"/> into <paramref name="options"/>, then fills any REQUIRED value that is
    /// missing/placeholder from the payout module's payer identity (the same company). Values are never
    /// invented: if the fallback is missing too the field stays as configured and
    /// <see cref="GetMissingSettings"/> reports it.
    /// </summary>
    public static void Apply(ReceiptSellerOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.GetSection(SectionName).Bind(options);

        if (IsMissing(options.CompanyName) && !IsMissing(configuration[FallbackCompanyNameKey]))
        {
            options.CompanyName = configuration[FallbackCompanyNameKey]!;
        }

        if (!IsValidTaxId(options.TaxId) && IsValidTaxId(configuration[FallbackTaxIdKey]))
        {
            options.TaxId = configuration[FallbackTaxIdKey]!;
        }

        if (IsMissing(options.Address) && !IsMissing(configuration[FallbackAddressKey]))
        {
            options.Address = configuration[FallbackAddressKey]!;
        }
    }

    /// <summary>The effective seller identity for <paramref name="configuration"/> (see <see cref="Apply"/>).</summary>
    public static ReceiptSellerOptions Resolve(IConfiguration configuration)
    {
        var options = new ReceiptSellerOptions();
        Apply(options, configuration);
        return options;
    }

    /// <summary>
    /// Names of the REQUIRED settings that are blank, still a <c>CHANGE_ME…</c> placeholder, or (for the
    /// tax id) not 13 digits. Empty when the seller identity is complete. Never contains values.
    /// </summary>
    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();

        if (IsMissing(CompanyName))
        {
            missing.Add($"{SectionName}:{nameof(CompanyName)}");
        }

        if (!IsValidTaxId(TaxId))
        {
            missing.Add($"{SectionName}:{nameof(TaxId)}");
        }

        if (IsMissing(Address))
        {
            missing.Add($"{SectionName}:{nameof(Address)}");
        }

        return missing;
    }

    private static bool IsValidTaxId(string? value) =>
        !IsMissing(value) && value!.Trim().Length == 13 && value.Trim().All(char.IsAsciiDigit)
        && value.Trim() != "0000000000000";

    private static bool IsMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
}
