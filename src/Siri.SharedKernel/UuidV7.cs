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

    /// <summary>
    /// The creation time embedded in a UUIDv7 (its first 48 bits are Unix milliseconds, big-endian). <c>false</c> when
    /// <paramref name="id"/> is not a version-7 UUID (for example a random v4 id), in which case no time can be derived.
    /// Lets a table whose key is a UUIDv7 report "how old is the oldest row" without a dedicated created-at column.
    /// </summary>
    public static bool TryGetTimestampUtc(Guid id, out DateTime timestampUtc)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            timestampUtc = default;
            return false;
        }

        // RFC 9562: the version lives in the high nibble of byte 6.
        if ((bytes[6] >> 4) != 7)
        {
            timestampUtc = default;
            return false;
        }

        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        timestampUtc = DateTime.UnixEpoch.AddMilliseconds(milliseconds);
        return true;
    }
}
