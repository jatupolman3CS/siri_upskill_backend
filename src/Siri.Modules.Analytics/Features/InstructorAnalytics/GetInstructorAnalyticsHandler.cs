using Siri.Modules.Analytics.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics.Features.InstructorAnalytics;

public sealed record InstructorDropOffStat(
    int EpisodeNumber,
    string Title,
    int ViewersCount,
    decimal CompletionRate);

public sealed record InstructorStudentProgress(
    string Id,
    string Name,
    string Email,
    string CourseTitle,
    decimal ProgressPercent,
    int CompletedEpisodes,
    int TotalEpisodes,
    DateTime LastActiveAtUtc);

public sealed record InstructorCourseOption(
    Guid Id,
    string Title);

public sealed record InstructorAnalyticsResponse(
    int TotalViews,
    int TotalWatchMinutes,
    decimal AvgCompletionRate,
    decimal TotalRevenue,
    int TotalEnrollments,
    IReadOnlyList<InstructorDropOffStat> DropOffStats,
    IReadOnlyList<InstructorStudentProgress> Students,
    IReadOnlyList<InstructorCourseOption> Courses);

public sealed class GetInstructorAnalyticsHandler(
    ICatalogPriceContract catalogContract,
    ILearningAnalyticsContract learningContract,
    IUserContactReader userContactReader,
    IDailyCourseStatRepository statsRepo,
    IEpisodeDropOffRepository dropOffRepo,
    IClock clock)
{
    public async Task<Result<InstructorAnalyticsResponse>> HandleAsync(
        Guid instructorUserId,
        string? range,
        Guid? courseIdFilter,
        CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty)
        {
            return Result.Failure<InstructorAnalyticsResponse>(DomainError.Forbidden("ไม่พบข้อมูลผู้ใช้"));
        }

        var allCourseIds = await catalogContract.GetCourseIdsByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (allCourseIds.Count == 0)
        {
            return Result.Success(new InstructorAnalyticsResponse(0, 0, 0m, 0m, 0, [], [], []));
        }

        var titles = await catalogContract.GetCourseTitlesAsync(allCourseIds, cancellationToken).ConfigureAwait(false);
        var courseOptions = allCourseIds.Select(id => new InstructorCourseOption(id, titles.TryGetValue(id, out var t) ? t : "Unknown Course")).ToList();

        var targetCourseIds = courseIdFilter.HasValue && allCourseIds.Contains(courseIdFilter.Value)
            ? new List<Guid> { courseIdFilter.Value }
            : allCourseIds;

        var days = range switch
        {
            "7d" => 7,
            "90d" => 90,
            _ => 30,
        };

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var fromDate = today.AddDays(-days);

        // 1. Rollup daily course stats
        var totalViews = 0;
        var totalEnrollments = 0;
        var totalRevenue = 0m;
        var completionRates = new List<decimal>();

        foreach (var cId in targetCourseIds)
        {
            var stats = await statsRepo.GetForCourseAsync(cId, fromDate, today, cancellationToken).ConfigureAwait(false);
            foreach (var s in stats)
            {
                totalViews += s.VIEWS;
                totalEnrollments += s.ENROLLMENTS;
                totalRevenue += s.REVENUE;
                if (s.COMPLETION_RATE > 0)
                {
                    completionRates.Add(s.COMPLETION_RATE);
                }
            }
        }

        var avgCompletionRate = completionRates.Count > 0
            ? Math.Round(completionRates.Average(), 1)
            : 0m;

        var totalWatchMinutes = totalViews * 6; // Average 6 minutes per view estimate

        // 2. Drop-off stats
        var episodes = await catalogContract.GetEpisodesForCoursesAsync(targetCourseIds, cancellationToken).ConfigureAwait(false);
        var dropOffList = new List<InstructorDropOffStat>();

        for (var i = 0; i < episodes.Count; i++)
        {
            var ep = episodes[i];
            var dropOffs = await dropOffRepo.GetForEpisodeAsync(ep.EpisodeId, fromDate, today, cancellationToken).ConfigureAwait(false);
            var viewers = dropOffs.Sum(d => d.START_COUNT);
            var completed = dropOffs.Sum(d => d.COMPLETE_COUNT);
            var avgPct = dropOffs.Count > 0 ? dropOffs.Average(d => d.AVG_WATCH_PERCENT) : 0m;
            var compRate = viewers > 0 ? Math.Round((decimal)completed * 100m / viewers, 1) : Math.Round(avgPct, 1);

            dropOffList.Add(new InstructorDropOffStat(
                i + 1,
                ep.Title,
                viewers,
                compRate));
        }

        // 3. Students progress
        var progressRecords = await learningContract.GetStudentProgressByCoursesAsync(targetCourseIds, 50, cancellationToken).ConfigureAwait(false);
        var userIds = progressRecords.Select(p => p.UserId).Distinct().ToList();
        var userContacts = await userContactReader.GetUsersContactInfoAsync(userIds, cancellationToken).ConfigureAwait(false);

        var totalEpisodeCountByCourse = episodes.GroupBy(e => e.CourseId).ToDictionary(g => g.Key, g => g.Count());

        var students = progressRecords.Select(p =>
        {
            userContacts.TryGetValue(p.UserId, out var contact);
            titles.TryGetValue(p.CourseId, out var courseTitle);
            var totalEps = totalEpisodeCountByCourse.TryGetValue(p.CourseId, out var count) ? Math.Max(1, count) : 1;
            var completedEps = (int)Math.Round((double)p.ProgressPercent / 100.0 * totalEps);

            return new InstructorStudentProgress(
                p.UserId.ToString(),
                contact.DisplayName ?? "ผู้เรียน",
                contact.Email ?? "student@example.com",
                courseTitle ?? "คอร์สเรียน",
                p.ProgressPercent,
                completedEps,
                totalEps,
                p.LastActiveAtUtc ?? clock.UtcNow);
        }).ToList();

        return Result.Success(new InstructorAnalyticsResponse(
            totalViews,
            totalWatchMinutes,
            avgCompletionRate,
            totalRevenue,
            totalEnrollments,
            dropOffList,
            students,
            courseOptions));
    }
}
