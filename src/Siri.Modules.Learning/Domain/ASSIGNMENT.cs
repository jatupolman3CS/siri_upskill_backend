using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// A file-upload assignment attached to a course episode.
/// </summary>
public sealed class ASSIGNMENT : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private ASSIGNMENT()
    {
    }

    public Guid ASSIGNMENT_ID { get; private set; }

    public Guid EPISODE_ID { get; private set; }

    public string TITLE { get; private set; } = string.Empty;

    public string INSTRUCTIONS { get; private set; } = string.Empty;

    public int? DUE_DAYS { get; private set; }

    public int MAX_FILE_SIZE_MB { get; private set; }

    public string ALLOWED_EXTENSIONS { get; private set; } = string.Empty;

    // ---- IAuditable -----------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>Creates a new assignment for an episode.</summary>
    public static ASSIGNMENT Create(Guid episodeId, string title, string instructions, int? dueDays, int maxFileSizeMb, string allowedExtensions)
    {
        if (episodeId == Guid.Empty)
        {
            throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new ASSIGNMENT
        {
            ASSIGNMENT_ID = UuidV7.NewId(),
            EPISODE_ID = episodeId,
            TITLE = title.Trim(),
            INSTRUCTIONS = instructions?.Trim() ?? string.Empty,
            DUE_DAYS = dueDays,
            MAX_FILE_SIZE_MB = Math.Max(1, maxFileSizeMb),
            ALLOWED_EXTENSIONS = allowedExtensions?.Trim().ToLowerInvariant() ?? string.Empty,
        };
    }

    /// <summary>Updates every mutable field at once.</summary>
    public void UpdateDetails(string title, string instructions, int? dueDays, int maxFileSizeMb, string allowedExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        TITLE = title.Trim();
        INSTRUCTIONS = instructions?.Trim() ?? string.Empty;
        DUE_DAYS = dueDays;
        MAX_FILE_SIZE_MB = Math.Max(1, maxFileSizeMb);
        ALLOWED_EXTENSIONS = allowedExtensions?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
