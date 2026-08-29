namespace Siri.Modules.Payout.Domain;

/// <summary>
/// Tax payer category for withholding tax reporting (ภ.ง.ด.3 for Individual, ภ.ง.ด.53 for Corporate)
/// per docs/DECISIONS.md Q4.
/// </summary>
public enum TaxPayerType
{
    Individual,
    Corporate,
}
