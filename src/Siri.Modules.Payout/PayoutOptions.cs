using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Payout;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Payout").
/// Configures revenue split fee estimation, payout batch generation rules (14-day hold, min payout ฿500,
/// withholding tax 3%), and platform payer info for 50 ทวิ tax certificate reporting per docs/DECISIONS.md Q4.
/// </summary>
public sealed class PayoutOptions
{
    public const string SectionName = "Payout";

    /// <summary>
    /// Fallback / estimated payment fee percentage applied when balance transaction fee is not directly provided.
    /// Default is 0.00m (or configured Stripe rate e.g. 3.30m).
    /// </summary>
    [Range(0, 100, ErrorMessage = "EstimatedPaymentFeePercent must be between 0 and 100.")]
    public decimal EstimatedPaymentFeePercent { get; set; } = 0.00m;

    /// <summary>
    /// Withholding tax rate deducted on instructor payouts (3.00% standard for services).
    /// </summary>
    [Range(0, 100, ErrorMessage = "WithholdingTaxPercent must be between 0 and 100.")]
    public decimal WithholdingTaxPercent { get; set; } = 3.00m;

    /// <summary>
    /// Minimum threshold for an instructor's earnings in THB to be included in a payout batch (฿500).
    /// </summary>
    [Range(0, (double)decimal.MaxValue, ErrorMessage = "MinimumPayoutAmount must be non-negative.")]
    public decimal MinimumPayoutAmount { get; set; } = 500.00m;

    /// <summary>
    /// Hold period in days before a paid order's revenue split becomes eligible for payout (14 days).
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "HoldDays must be non-negative.")]
    public int HoldDays { get; set; } = 14;

    /// <summary>
    /// Platform payer company name for withholding tax (50 ทวิ) certificates.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayerCompanyName is required for tax certificates.")]
    public string PayerCompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Platform payer 13-digit Tax ID for withholding tax (50 ทวิ) certificates.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayerTaxId is required for tax certificates.")]
    public string PayerTaxId { get; set; } = string.Empty;

    /// <summary>
    /// Platform payer company address for withholding tax (50 ทวิ) certificates.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayerAddress is required for tax certificates.")]
    public string PayerAddress { get; set; } = string.Empty;
}
