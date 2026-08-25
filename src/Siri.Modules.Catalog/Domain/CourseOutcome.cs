using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>One "สิ่งที่จะได้เรียนรู้" (learning outcome) bullet on a <see cref="Course"/> — required for
/// docs/REQUIREMENTS.md's LX-02 course-detail acceptance criterion. Construction is <c>internal</c>,
/// reachable only through <see cref="Course.AddOutcome"/>.</summary>
public sealed class CourseOutcome : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private CourseOutcome()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Owned by <see cref="Course.ReorderOutcomes"/> only.</summary>
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

    internal static CourseOutcome Create(Guid courseId, string text, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new CourseOutcome
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

    /// <summary>Called only by <see cref="Course.ReorderOutcomes"/>, which already validated the full set.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}
