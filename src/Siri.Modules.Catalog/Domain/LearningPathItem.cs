using Siri.Persistence.Conventions;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// Join entity between <see cref="LearningPath"/> and <see cref="Course"/>.
/// </summary>
public sealed class LearningPathItem : IAuditable
{
    private LearningPathItem()
    {
    }

    public Guid PathId { get; private set; }
    public Guid CourseId { get; private set; }
    public int SortOrder { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc { get => CreatedAtUtc; set => CreatedAtUtc = value; }
    Guid? IAuditable.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditable.UpdatedAtUtc { get => UpdatedAtUtc; set => UpdatedAtUtc = value; }
    Guid? IAuditable.UpdatedBy { get => UpdatedBy; set => UpdatedBy = value; }

    public static LearningPathItem Create(Guid pathId, Guid courseId, int sortOrder)
    {
        if (pathId == Guid.Empty) throw new ArgumentException("Path ID cannot be empty.", nameof(pathId));
        if (courseId == Guid.Empty) throw new ArgumentException("Course ID cannot be empty.", nameof(courseId));

        return new LearningPathItem
        {
            PathId = pathId,
            CourseId = courseId,
            SortOrder = sortOrder,
        };
    }
}
