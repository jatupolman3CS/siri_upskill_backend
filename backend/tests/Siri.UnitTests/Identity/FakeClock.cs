using Siri.SharedKernel;

namespace Siri.UnitTests.Identity;

/// <summary>Controllable <see cref="IClock"/> for tests — real domain code must read time only
/// through <see cref="IClock"/> (database.md: "อ่านเวลาผ่าน IClock เท่านั้น"), so tests supply this
/// instead of relying on the real system clock.</summary>
internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
