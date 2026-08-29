using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// A downloadable file attachment for a <see cref="COURSE_EPISODE"/> (e.g. slides, source code, worksheets).
/// </summary>
public sealed class EPISODE_ATTACHMENT : IAuditable
{
    private EPISODE_ATTACHMENT()
    {
    }

    public Guid Id { get; private set; }
    public Guid EpisodeId { get; private set; }
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

    public static EPISODE_ATTACHMENT Create(Guid episodeId, string fileName, string storageKey, string contentType, long sizeBytes)
    {
        if (episodeId == Guid.Empty) throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "sizeBytes must be positive.");

        return new EPISODE_ATTACHMENT
        {
            Id = UuidV7.NewId(),
            EpisodeId = episodeId,
            FileName = fileName.Trim(),
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
        };
    }
}
