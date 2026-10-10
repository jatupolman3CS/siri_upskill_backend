namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Pure money helper for orders paid through an admin amount override: the order lines still carry their list
/// price, but only a fraction of it was really collected. Revenue split must run on what was collected
/// (docs: owner decision 2026-10-10 — "split by the amount actually charged"), so each line is scaled by
/// <c>paid / original</c>.
/// </summary>
public static class PaidAmountAllocator
{
    /// <summary>
    /// Scales every line total by <paramref name="paidAmount"/> / <paramref name="originalAmount"/>, rounded to
    /// 2 decimals (away from zero). The last line absorbs the rounding remainder so the lines always add up to
    /// exactly <c>Round(sum(lineTotals) × scale, 2)</c> — never "close", which matters because the result is
    /// credited to instructors.
    /// </summary>
    public static IReadOnlyList<decimal> ScaleToPaidAmount(IReadOnlyList<decimal> lineTotals, decimal originalAmount, decimal paidAmount)
    {
        ArgumentNullException.ThrowIfNull(lineTotals);
        if (originalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(originalAmount), originalAmount, "originalAmount must be positive.");
        }

        if (paidAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(paidAmount), paidAmount, "paidAmount must not be negative.");
        }

        if (lineTotals.Count == 0)
        {
            return [];
        }

        var scale = paidAmount / originalAmount;
        var target = Math.Round(lineTotals.Sum() * scale, 2, MidpointRounding.AwayFromZero);

        var result = new decimal[lineTotals.Count];
        var allocated = 0m;
        for (var i = 0; i < lineTotals.Count - 1; i++)
        {
            result[i] = Math.Round(lineTotals[i] * scale, 2, MidpointRounding.AwayFromZero);
            allocated += result[i];
        }

        result[^1] = Math.Max(0m, target - allocated);
        return result;
    }
}
