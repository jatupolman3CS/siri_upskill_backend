using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// Curated career/skill track composed of multiple <see cref="Course"/> items in sequential order.
/// </summary>
public sealed class LearningPath : IAuditable
{
    private readonly List<LearningPathItem> _items = [];

    private LearningPath()
    {
    }

    public Guid Id { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public IReadOnlyCollection<LearningPathItem> Items => _items.AsReadOnly();

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc { get => CreatedAtUtc; set => CreatedAtUtc = value; }
    Guid? IAuditable.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditable.UpdatedAtUtc { get => UpdatedAtUtc; set => UpdatedAtUtc = value; }
    Guid? IAuditable.UpdatedBy { get => UpdatedBy; set => UpdatedBy = value; }

    public static LearningPath Create(string slug, string title, string? description, int sortOrder, bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new LearningPath
        {
            Id = UuidV7.NewId(),
            Slug = slug.Trim().ToLowerInvariant(),
            Title = title.Trim(),
            Description = description?.Trim(),
            SortOrder = sortOrder,
            IsActive = isActive,
        };
    }

    public void Update(string slug, string title, string? description, int sortOrder, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Slug = slug.Trim().ToLowerInvariant();
        Title = title.Trim();
        Description = description?.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void SetCourses(IEnumerable<Guid> courseIds)
    {
        _items.Clear();
        int order = 1;
        foreach (var courseId in courseIds.Distinct())
        {
            _items.Add(LearningPathItem.Create(Id, courseId, order++));
        }
    }
}
