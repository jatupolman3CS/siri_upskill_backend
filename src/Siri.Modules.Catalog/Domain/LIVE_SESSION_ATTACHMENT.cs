using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// A teaching-material file (slides, handout, worksheet) an instructor attaches to one
/// <see cref="COURSE_LIVE_SESSION"/> — the live-class counterpart of <see cref="EPISODE_ATTACHMENT"/>
/// (task P4-03c). Visible to the course's enrolled learners and its instructor/admins; the bytes live in
/// private Cloudflare R2 under <see cref="StorageKey"/>, which is never sent to a client.
/// <para>
/// Not a child of <see cref="COURSE"/> in the aggregate sense (the instructor adds/removes files
/// independently of editing the schedule, which would otherwise force a COURSE concurrency-token round trip
/// on every upload) — same reasoning <see cref="EPISODE_ATTACHMENT"/> is a separate table keyed by episode id.
/// No <see cref="ISoftDelete"/>: a file is plain content, not money or an access right, so deleting the row
/// (and the R2 object) is final.
/// </para>
/// </summary>
public sealed class LIVE_SESSION_ATTACHMENT : IAuditable
{
    private LIVE_SESSION_ATTACHMENT()
    {
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc { get => CreatedAtUtc; set => CreatedAtUtc = value; }
    Guid? IAuditable.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditable.UpdatedAtUtc { get => UpdatedAtUtc; set => UpdatedAtUtc = value; }
    Guid? IAuditable.UpdatedBy { get => UpdatedBy; set => UpdatedBy = value; }

    public static LIVE_SESSION_ATTACHMENT Create(Guid sessionId, string fileName, string storageKey, string contentType, long sizeBytes)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "sizeBytes must be positive.");

        return new LIVE_SESSION_ATTACHMENT
        {
            Id = UuidV7.NewId(),
            SessionId = sessionId,
            FileName = fileName.Trim(),
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
        };
    }
}
