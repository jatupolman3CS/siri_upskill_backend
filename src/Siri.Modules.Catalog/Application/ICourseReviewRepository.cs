using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface ICourseReviewRepository
{
    Task<COURSE_REVIEW?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<COURSE_REVIEW?> GetUserReviewForCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<COURSE_REVIEW> Items, int TotalCount)> GetByCourseIdPagedAsync(Guid courseId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<COURSE_REVIEW>> GetPendingReviewsAsync(CancellationToken cancellationToken);

    Task AddAsync(COURSE_REVIEW review, CancellationToken cancellationToken);

    Task UpdateAsync(COURSE_REVIEW review, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
