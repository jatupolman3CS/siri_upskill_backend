using Siri.Modules.Commerce.Application;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PaidAmountAllocatorTests
{
    [Fact]
    public void ScaleToPaidAmount_SingleLine_ReturnsPaidAmount()
    {
        var result = PaidAmountAllocator.ScaleToPaidAmount([1890m], 1890m, 20m);

        Assert.Equal(new[] { 20m }, result.ToArray());
    }

    [Fact]
    public void ScaleToPaidAmount_ProportionalLines_KeepsRatios()
    {
        var result = PaidAmountAllocator.ScaleToPaidAmount([700m, 300m], 1000m, 20m);

        Assert.Equal(new[] { 14m, 6m }, result.ToArray());
    }

    [Fact]
    public void ScaleToPaidAmount_ThreeEqualLines_SumsExactlyToPaidAmountNoRoundingDrift()
    {
        // 20 / 3 = 6.666… — naive per-line rounding gives 6.67 × 3 = 20.01 (money invented out of rounding).
        var result = PaidAmountAllocator.ScaleToPaidAmount([630m, 630m, 630m], 1890m, 20m);

        Assert.Equal(20m, result.Sum());
        Assert.Equal(new[] { 6.67m, 6.67m, 6.66m }, result.ToArray());
    }

    [Theory]
    [InlineData(1890.00, 20.00)]
    [InlineData(999.99, 20.00)]
    [InlineData(2490.00, 35.55)]
    [InlineData(333.33, 0.01)]
    public void ScaleToPaidAmount_AnyLineSplit_AlwaysSumsToRoundedScaledTotal(double originalD, double paidD)
    {
        var original = (decimal)originalD;
        var paid = (decimal)paidD;
        // Three uneven lines that add up to the original.
        var a = Math.Round(original * 0.5m, 2);
        var b = Math.Round(original * 0.3m, 2);
        var c = original - a - b;

        var result = PaidAmountAllocator.ScaleToPaidAmount([a, b, c], original, paid);

        Assert.Equal(paid, result.Sum());
        Assert.All(result, v => Assert.True(v >= 0m));
    }

    [Fact]
    public void ScaleToPaidAmount_SubsetOfOrderLines_ScalesTheSubsetByTheOrderLevelRatio()
    {
        // Only one of two order lines qualifies for a split: it keeps its share of what was really paid.
        var result = PaidAmountAllocator.ScaleToPaidAmount([700m], 1000m, 20m);

        Assert.Equal(new[] { 14m }, result.ToArray());
    }

    [Fact]
    public void ScaleToPaidAmount_NoLines_ReturnsEmpty() =>
        Assert.Empty(PaidAmountAllocator.ScaleToPaidAmount([], 1000m, 20m));

    [Fact]
    public void ScaleToPaidAmount_NonPositiveOriginal_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaidAmountAllocator.ScaleToPaidAmount([100m], 0m, 20m));

    [Fact]
    public void ScaleToPaidAmount_NegativePaid_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaidAmountAllocator.ScaleToPaidAmount([100m], 100m, -1m));
}
