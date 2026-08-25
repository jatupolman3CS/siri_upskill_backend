namespace Siri.SharedKernel;

/// <summary>
/// Generates UUIDv7 primary keys (time-ordered GUIDs), per database.md: "PK = uniqueidentifier
/// ค่า UUIDv7 (Guid.CreateVersion7() ใน .NET 9+) ห้าม NEWID() (ทำ index กระจาย)".
/// Confirmed against the installed .NET 10 SDK (10.0.400): <see cref="Guid.CreateVersion7"/> is a
/// real BCL API (added in .NET 9, System.Guid) — not guessed.
/// Entities should call <see cref="NewId"/> instead of <c>Guid.NewGuid()</c> or <c>Guid.CreateVersion7()</c>
/// directly, so the one place that picks the id strategy stays swappable.
/// </summary>
public static class UuidV7
{
    /// <summary>Creates a new time-ordered UUIDv7 suitable for use as a clustered primary key.</summary>
    public static Guid NewId() => Guid.CreateVersion7();
}
