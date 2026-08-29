using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

public sealed class WISHLIST_ITEM
{
    public Guid UserId { get; private set; }
    public Guid CourseId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private WISHLIST_ITEM() { }

    public static WISHLIST_ITEM Create(Guid userId, Guid courseId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new WISHLIST_ITEM
        {
            UserId = userId,
            CourseId = courseId,
            CreatedAtUtc = clock.UtcNow,
        };
    }
}
