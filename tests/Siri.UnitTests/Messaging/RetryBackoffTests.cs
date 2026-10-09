using Siri.Integrations.Messaging;

namespace Siri.UnitTests.Messaging;

public class RetryBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public void For_GrowingAttempt_DoublesTheDelayEachTime(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryBackoff.For(attempt, Base, Max));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void For_LargeAttempt_IsCappedAtTheMaximumAndNeverOverflows(int attempt)
    {
        Assert.Equal(Max, RetryBackoff.For(attempt, Base, Max));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void For_AttemptBelowOne_IsTreatedAsTheFirstAttempt(int attempt)
    {
        Assert.Equal(Base, RetryBackoff.For(attempt, Base, Max));
    }

    [Fact]
    public void For_MaximumBelowBase_NeverReturnsLessThanTheBase()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RetryBackoff.For(3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));
    }
}
