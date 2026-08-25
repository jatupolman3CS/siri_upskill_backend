using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>One prerequisite bullet on a <see cref="Course"/> (e.g. "ควรมีพื้นฐาน HTML/CSS มาก่อน") —
/// structurally identical to <see cref="CourseOutcome"/>, kept as its own type/table rather than a
/// shared "CourseBullet" abstraction since the two are independent lists with independent sort orders
/// (database.md's schema names them as separate tables). Construction is <c>internal</c>, reachable
/// only through <see cref="Course.AddRequirement"/>.</summary>
public sealed class CourseRequirement : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private CourseRequirement()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Owned by <see cref="Course.ReorderRequirements"/> only.</summary>
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

    internal static CourseRequirement Create(Guid courseId, string text, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new CourseRequirement
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

    /// <summary>Called only by <see cref="Course.ReorderRequirements"/>, which already validated the full set.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}
