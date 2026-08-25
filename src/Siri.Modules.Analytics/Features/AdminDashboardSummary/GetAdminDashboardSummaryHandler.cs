using Siri.Modules.Analytics.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics.Features.AdminDashboardSummary;

public sealed record TopCourseSummary(
    Guid CourseId,
    string CourseTitle,
    decimal TotalRevenue,
    int TotalEnrollments,
    int TotalViews);

public sealed record AdminDashboardSummaryResponse(
    decimal TodaySales,
    int TodayNewLearners,
    int TotalLearners,
    int PendingCourseReviewsCount,
    int PendingRefundsCount,
    IReadOnlyList<TopCourseSummary> TopCourses30Days);

public sealed class GetAdminDashboardSummaryHandler(
    IIdentityStatsContract identityContract,
    ICatalogPriceContract catalogContract,
    ICommerceStatsContract commerceContract,
    IDailyCourseStatRepository analyticsRepo,
    IClock clock)
{
    public async Task<AdminDashboardSummaryResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);
        var thirtyDaysAgo = today.AddDays(-30);

        var learnerStatsTask = identityContract.GetLearnerStatsAsync(nowUtc, cancellationToken);
        var pendingReviewsTask = catalogContract.GetPendingReviewsCountAsync(cancellationToken);
        var commerceStatsTask = commerceContract.GetCommerceDashboardStatsAsync(nowUtc, cancellationToken);
        var topCoursesStatsTask = analyticsRepo.GetTopCoursesAsync(thirtyDaysAgo, today, 10, cancellationToken);

        await Task.WhenAll(learnerStatsTask, pendingReviewsTask, commerceStatsTask, topCoursesStatsTask).ConfigureAwait(false);

        var learnerStats = await learnerStatsTask.ConfigureAwait(false);
        var pendingReviews = await pendingReviewsTask.ConfigureAwait(false);
        var commerceStats = await commerceStatsTask.ConfigureAwait(false);
        var topCoursesStats = await topCoursesStatsTask.ConfigureAwait(false);

        var courseIds = topCoursesStats.Select(s => s.CourseId).ToList();
        var titles = await catalogContract.GetCourseTitlesAsync(courseIds, cancellationToken).ConfigureAwait(false);

        var topCourses = topCoursesStats.Select(s => new TopCourseSummary(
            s.CourseId,
            titles.TryGetValue(s.CourseId, out var title) ? title : "Unknown Course",
            s.TotalRevenue,
            s.TotalEnrollments,
            s.TotalViews)).ToList();

        return new AdminDashboardSummaryResponse(
            commerceStats.TodaySales,
            learnerStats.TodayNewLearners,
            learnerStats.TotalLearners,
            pendingReviews,
            commerceStats.PendingRefundsCount,
            topCourses);
    }
}
