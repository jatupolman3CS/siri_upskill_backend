using Siri.SharedKernel;

namespace Siri.UnitTests.Notification;

/// <summary>Controllable <see cref="IClock"/> for tests — real domain code must read time only
/// through <see cref="IClock"/> (database.md: "อ่านเวลาผ่าน IClock เท่านั้น"), so tests supply this
/// instead of relying on the real system clock. Mirrors Siri.UnitTests.Identity.FakeClock — kept as
/// its own small copy so this folder doesn't reach across module test folders.</summary>
internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
