using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>Controllable <see cref="IClock"/> for the Live module's tests — domain code reads time only
/// through <see cref="IClock"/> (database.md).</summary>
internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
