using Siri.Persistence.Conventions;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// Join entity between <see cref="LEARNING_PATH"/> and <see cref="COURSE"/>.
/// </summary>
public sealed class LEARNING_PATH_ITEM : IAuditable
{
    private LEARNING_PATH_ITEM()
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

    public static LEARNING_PATH_ITEM Create(Guid pathId, Guid courseId, int sortOrder)
    {
        if (pathId == Guid.Empty) throw new ArgumentException("Path ID cannot be empty.", nameof(pathId));
        if (courseId == Guid.Empty) throw new ArgumentException("COURSE ID cannot be empty.", nameof(courseId));

        return new LEARNING_PATH_ITEM
        {
            PathId = pathId,
            CourseId = courseId,
            SortOrder = sortOrder,
        };
    }
}
