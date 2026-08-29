namespace Siri.Modules.Catalog.Contracts;

public interface ICourseStatsUpdater
{
    Task ReconcileCourseStatsAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task ReconcileAllCoursesStatsAsync(CancellationToken cancellationToken = default);
}
