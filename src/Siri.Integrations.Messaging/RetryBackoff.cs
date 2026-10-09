namespace Siri.Integrations.Messaging;

/// <summary>Exponential backoff shared by the consumer's in-place retry and the relays' broker-outage back-off.</summary>
public static class RetryBackoff
{
    /// <summary>
    /// <c>baseDelay · 2^(attempt-1)</c>, capped at <paramref name="maxDelay"/>. <paramref name="attempt"/> is 1-based
    /// (the delay to wait after the Nth failure); values below 1 are treated as 1.
    /// </summary>
    public static TimeSpan For(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 30); // 2^30 already dwarfs any sensible cap; avoids double overflow
        var delay = TimeSpan.FromTicks((long)Math.Min(baseDelay.Ticks * Math.Pow(2, exponent), maxDelay.Ticks));
        return delay < baseDelay ? baseDelay : delay;
    }
}
