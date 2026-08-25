namespace Siri.SharedKernel;

/// <summary>
/// Source of the current UTC time. Inject this instead of calling <see cref="DateTime.UtcNow"/> or
/// <see cref="DateTime.Now"/> directly, so time-dependent logic stays testable
/// (.claude/rules/database.md: "อ่านเวลาผ่าน IClock เท่านั้น").
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC (<see cref="DateTime.Kind"/> is always <see cref="DateTimeKind.Utc"/>),
    /// matching the <c>datetime2(3)</c> / <c>*AtUtc</c> column convention in database.md.</summary>
    DateTime UtcNow { get; }
}
