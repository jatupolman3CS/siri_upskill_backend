using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>One prerequisite bullet on a <see cref="COURSE"/> (e.g. "ควรมีพื้นฐาน HTML/CSS มาก่อน") —
/// structurally identical to <see cref="COURSE_OUTCOME"/>, kept as its own type/table rather than a
/// shared "CourseBullet" abstraction since the two are independent lists with independent sort orders
/// (database.md's schema names them as separate tables). Construction is <c>internal</c>, reachable
/// only through <see cref="COURSE.AddRequirement"/>.</summary>
public sealed class COURSE_REQUIREMENT : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private COURSE_REQUIREMENT()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Owned by <see cref="COURSE.ReorderRequirements"/> only.</summary>
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

    internal static COURSE_REQUIREMENT Create(Guid courseId, string text, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new COURSE_REQUIREMENT
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

    /// <summary>Called only by <see cref="COURSE.ReorderRequirements"/>, which already validated the full set.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }
}
