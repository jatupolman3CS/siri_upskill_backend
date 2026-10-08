using Siri.Modules.Analytics.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics.Features.AdminDashboardSummary;

public sealed record TopCourseSummary(
    Guid CourseId,
    string? CourseTitle,
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

        // Sequential on purpose: every contract/repository below runs on the same scoped AppDbContext,
        // which does not support concurrent operations (Task.WhenAll threw "A second operation was
        // started on this context instance" and made this endpoint always return 500).
        var learnerStats = await identityContract.GetLearnerStatsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        var pendingReviews = await catalogContract.GetPendingReviewsCountAsync(cancellationToken).ConfigureAwait(false);
        var commerceStats = await commerceContract.GetCommerceDashboardStatsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        var topCoursesStats = await analyticsRepo.GetTopCoursesAsync(thirtyDaysAgo, today, 10, cancellationToken).ConfigureAwait(false);

        var courseIds = topCoursesStats.Select(s => s.CourseId).ToList();
        var titles = await catalogContract.GetCourseTitlesAsync(courseIds, cancellationToken).ConfigureAwait(false);

        var topCourses = topCoursesStats.Select(s => new TopCourseSummary(
            s.CourseId,
            titles.TryGetValue(s.CourseId, out var title) && !string.IsNullOrWhiteSpace(title) ? title : null,
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
