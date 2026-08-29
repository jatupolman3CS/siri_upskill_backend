using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>One "สิ่งที่จะได้เรียนรู้" (learning outcome) bullet on a <see cref="COURSE"/> — required for
/// docs/REQUIREMENTS.md's LX-02 course-detail acceptance criterion. Construction is <c>internal</c>,
/// reachable only through <see cref="COURSE.AddOutcome"/>.</summary>
public sealed class COURSE_OUTCOME : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private COURSE_OUTCOME()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Owned by <see cref="COURSE.ReorderOutcomes"/> only.</summary>
    public int SortOrder { get; private set; }

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

    internal static COURSE_OUTCOME Create(Guid courseId, string text, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new COURSE_OUTCOME
        {
            Id = UuidV7.NewId(),
            CourseId = courseId,
            Text = text,
            SortOrder = sortOrder,
        };
    }

    public void UpdateText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Text = text;
    }

    /// <summary>Called only by <see cref="COURSE.ReorderOutcomes"/>, which already validated the full set.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}
