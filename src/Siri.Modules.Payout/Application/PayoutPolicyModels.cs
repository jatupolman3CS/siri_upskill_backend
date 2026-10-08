namespace Siri.Modules.Payout.Application;

/// <summary>
/// The revenue/payout rules a signed-in user is subject to, exactly as the server applies them — the single source the UI quotes instead of hardcoding numbers.
/// Percentages are plain percent values (<c>70.00</c> means 70%), amounts are THB, <c>HoldDays</c> is whole days. Read-only: nothing here is computed from a request.
/// </summary>
/// <param name="RevenueSharePercent">The caller's instructor share of net revenue — their own profile's rate when they have an instructor profile, otherwise the system default.</param>
/// <param name="PlatformSharePercent"><c>100 − RevenueSharePercent</c>.</param>
/// <param name="WithholdingTaxPercent">Withholding tax deducted from payouts (<c>PayoutOptions.WithholdingTaxPercent</c>).</param>
/// <param name="MinimumPayoutAmount">Minimum accumulated earnings, in THB, before an instructor is included in a payout batch (<c>PayoutOptions.MinimumPayoutAmount</c>).</param>
/// <param name="HoldDays">Days a paid order's split is held before it becomes eligible for payout (<c>PayoutOptions.HoldDays</c>).</param>
public sealed record PayoutPolicyResponse(
    decimal RevenueSharePercent,
    decimal PlatformSharePercent,
    decimal WithholdingTaxPercent,
    decimal MinimumPayoutAmount,
    int HoldDays);
