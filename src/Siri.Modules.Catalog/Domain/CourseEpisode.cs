using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// One video lesson inside a <see cref="CourseSection"/>. Leaf of the Course aggregate — construction
/// is <c>internal</c>, reachable only through <see cref="CourseSection.AddEpisode"/> (which already
/// knows its own <see cref="CourseId"/>/<see cref="Domain.CourseSection.Id"/> and sets both
/// consistently by construction, so nothing outside the aggregate can create an orphaned or
/// inconsistently-linked episode).
/// </summary>
public sealed class CourseEpisode : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private CourseEpisode()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Denormalized copy of the owning <see cref="CourseSection"/>'s <see cref="Domain.CourseSection.CourseId"/>
    /// — kept for query convenience (list every episode of a course without joining through sections).
    /// Not the ownership edge; see <c>CourseEpisodeConfiguration</c>'s doc comment for why its FK is
    /// <c>Restrict</c>, not <c>Cascade</c>.</summary>
    public Guid CourseId { get; private set; }

    public Guid SectionId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Display order among sibling episodes within the same <see cref="SectionId"/>. Owned by
    /// <see cref="CourseSection.ReorderEpisodes"/> only.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Set only via <see cref="AttachMedia"/> — <c>null</c> means this episode has no video yet.
    /// FKs to <c>media.MediaAssets</c> conceptually, but deliberately has no database-level FK constraint
    /// (cross-module/cross-schema — see <c>CourseEpisodeConfiguration</c>'s doc comment).</summary>
    public Guid? MediaAssetId { get; private set; }

    /// <summary>Known only once media is attached (mirrors <see cref="MediaAssetId"/>'s lifecycle).</summary>
    public int? DurationSeconds { get; private set; }

    /// <summary>Whether this episode can be watched without enrolling/purchasing.</summary>
    public bool IsFreePreview { get; private set; }

    public CourseEpisodeStatus Status { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
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

    internal static CourseEpisode Create(Guid courseId, Guid sectionId, string title, string? description, int sortOrder, bool isFreePreview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new CourseEpisode
        {
            Id = UuidV7.NewId(),
            CourseId = courseId,
            SectionId = sectionId,
            Title = title,
            Description = description,
            SortOrder = sortOrder,
            IsFreePreview = isFreePreview,
            Status = CourseEpisodeStatus.Draft,
        };
    }

    public void UpdateDetails(string title, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
        Description = description;
    }

    /// <summary>Called only by <see cref="CourseSection.ReorderEpisodes"/>, which already validated the
    /// full sibling set — no independent validation here.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    public void MarkFreePreview()
    {
        IsFreePreview = true;
    }

    public void UnmarkFreePreview()
    {
        IsFreePreview = false;
    }

    /// <summary>Attaches (or replaces) this episode's video. <see cref="Status"/> becomes
    /// <see cref="CourseEpisodeStatus.Ready"/> — this task has no Phase-2 webhook/processing pipeline
    /// yet, so status stays entirely derived from media presence (see <see cref="CourseEpisodeStatus"/>'s
    /// own doc comment).</summary>
    public void AttachMedia(Guid mediaAssetId, int durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "durationSeconds must be positive.");
        }

        MediaAssetId = mediaAssetId;
        DurationSeconds = durationSeconds;
        Status = CourseEpisodeStatus.Ready;
    }

    public void RemoveMedia()
    {
        MediaAssetId = null;
        DurationSeconds = null;
        Status = CourseEpisodeStatus.Draft;
    }
}
