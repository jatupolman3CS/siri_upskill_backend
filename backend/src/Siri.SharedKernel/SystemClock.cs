namespace Siri.SharedKernel;

/// <summary>Production <see cref="IClock"/> backed by the real system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
