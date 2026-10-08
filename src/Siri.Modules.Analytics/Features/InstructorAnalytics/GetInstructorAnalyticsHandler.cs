using System.Globalization;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics.Features.InstructorAnalytics;

public sealed record InstructorDropOffStat(
    int EpisodeNumber,
    string Title,
    int ViewersCount,
    decimal CompletionRate);

/// <summary>
/// One learner row. Real data only — when a value cannot be resolved it is <c>null</c>, never replaced by a made-up display text or address; the UI
/// decides how to present "unknown" (docs/contracts/P11-10-instructor-dashboard-summary.md §4). A row whose course can no longer be resolved (the course
/// was deleted) is left out instead of being shown under an invented title.
/// </summary>
/// <param name="Name">The learner's real display name, or <c>null</c> when the account cannot be resolved.</param>
/// <param name="Email">The learner's real e-mail address, or <c>null</c> when the account cannot be resolved.</param>
/// <param name="CourseTitle">The course's real title.</param>
/// <param name="CompletedEpisodes">Episodes the learner has actually finished (counted, not estimated from the percentage).</param>
/// <param name="TotalEpisodes">Real number of episodes in the course (0 when the course has none on record).</param>
/// <param name="LastActiveAtUtc">The learner's real last activity, or <c>null</c> when they never opened the course.</param>
public sealed record InstructorStudentProgress(
    string Id,
    string? Name,
    string? Email,
    string CourseTitle,
    decimal ProgressPercent,
    int CompletedEpisodes,
    int TotalEpisodes,
    DateTime? LastActiveAtUtc);

public sealed record InstructorCourseOption(
    Guid Id,
    string Title);

/// <summary>Instructor-wide headline numbers (not bound to the selected range/course). Every value is read from stored data — nothing is estimated.</summary>
/// <param name="PeriodKey">The current revenue period (<c>yyyy-MM</c>, UTC month — the same key <c>REVENUE_SPLITS.PERIOD_KEY</c> uses).</param>
/// <param name="PreviousPeriodKey">The month before <paramref name="PeriodKey"/>.</param>
/// <param name="NetRevenueThisMonth">Σ instructor amount of the period's non-reversed splits, refund adjustments included.</param>
/// <param name="NetRevenueChangePercent">Change vs. the previous month in percent (1 decimal), <c>null</c> when the previous month earned nothing (nothing to compare with).</param>
/// <param name="TotalStudents">Different learners with an active or expired enrollment in any of the instructor's courses.</param>
/// <param name="NewStudentsLast7Days">The subset of them who enrolled during the last 7 days.</param>
/// <param name="RatingAverage">Rating average of all courses weighted by their rating count (2 decimals), <c>null</c> when nothing has been rated yet.</param>
public sealed record InstructorDashboardKpis(
    string PeriodKey,
    string PreviousPeriodKey,
    decimal NetRevenueThisMonth,
    decimal NetRevenuePreviousMonth,
    decimal? NetRevenueChangePercent,
    int TotalStudents,
    int NewStudentsLast7Days,
    int PublishedCourseCount,
    int TotalCourseCount,
    decimal? RatingAverage,
    int RatingCount);

/// <param name="Status">The course status name (<c>Draft</c>, <c>InReview</c>, <c>Published</c>, ...), as stored.</param>
/// <param name="DeliveryFormat">The course delivery format name (<c>OnDemand</c>, <c>Live</c>, <c>Hybrid</c>).</param>
/// <param name="RatingAverage"><c>null</c> when the course has no rating yet.</param>
public sealed record InstructorCourseSummaryItem(
    Guid CourseId,
    string Title,
    string Slug,
    string Status,
    string DeliveryFormat,
    decimal Price,
    int EnrollmentCount,
    decimal? RatingAverage,
    int RatingCount,
    string? ThumbnailUrl);

/// <summary>The instructor's next live session that has not ended yet (a session in progress counts).</summary>
/// <param name="ExpectedLearners">Learners the platform invited to the session.</param>
/// <param name="MeetingUsable">The session has a room that can be entered right now.</param>
public sealed record InstructorNextSession(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    int ExpectedLearners,
    bool MeetingUsable);

/// <param name="ExpectedLearners">Learners invited to the (finished) session.</param>
/// <param name="JoinedLearners">Distinct learners who were handed the room link.</param>
/// <param name="AttendanceRatePercent"><c>JoinedLearners * 100 / ExpectedLearners</c> (1 decimal), <c>null</c> when nobody was invited. Not capped: a learner who joined
/// without an invitation counts as joined, so the rate can exceed 100.</param>
public sealed record InstructorLiveAttendanceItem(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string Title,
    DateTime StartsAtUtc,
    int ExpectedLearners,
    int JoinedLearners,
    decimal? AttendanceRatePercent);

/// <param name="TotalWatchMinutes">Real watch time: the recorded watched seconds (<c>EPISODE_PROGRESS.WATCHED_SECONDS</c>) of the learner progress rows active in
/// the selected range, in whole minutes. Only the cumulative per-learner/per-episode total is stored, not seconds-per-day, so it can include watching from before
/// the range for learners who were active inside it. Never an estimate from view counts.</param>
/// <param name="Kpis">Headline numbers for the whole instructor account (not bound to range/course).</param>
/// <param name="CourseSummaries">The instructor's own courses (≤ 50, newest first) with their real status — never the public catalog.</param>
/// <param name="NextSession">The next live session not yet ended, or <c>null</c>.</param>
/// <param name="LiveAttendance">Attendance of the latest (≤ 5) finished, non-cancelled sessions, newest first.</param>
public sealed record InstructorAnalyticsResponse(
    int TotalViews,
    int TotalWatchMinutes,
    decimal AvgCompletionRate,
    decimal TotalRevenue,
    int TotalEnrollments,
    IReadOnlyList<InstructorDropOffStat> DropOffStats,
    IReadOnlyList<InstructorStudentProgress> Students,
    IReadOnlyList<InstructorCourseOption> Courses,
    InstructorDashboardKpis? Kpis = null,
    IReadOnlyList<InstructorCourseSummaryItem>? CourseSummaries = null,
    InstructorNextSession? NextSession = null,
    IReadOnlyList<InstructorLiveAttendanceItem>? LiveAttendance = null);

/// <summary>
/// Builds the instructor dashboard/analytics summary from stored data only (docs/contracts/P11-10-instructor-dashboard-summary.md). Everything is scoped to the
/// caller: the course ids, the instructor profile (money), the live sessions and the learners all derive from <c>instructorUserId</c> (from the access token),
/// and a <c>courseId</c> filter that is not the caller's own is ignored.
/// </summary>
public sealed class GetInstructorAnalyticsHandler(
    ICatalogPriceContract catalogContract,
    IInstructorCourseStatsReader courseStatsReader,
    IInstructorRevenueReader revenueReader,
    ILearningAnalyticsContract learningContract,
    ILiveScheduleReader liveSchedule,
    ILiveAttendanceReader liveAttendance,
    IUserContactReader userContactReader,
    IDailyCourseStatRepository statsRepo,
    IEpisodeDropOffRepository dropOffRepo,
    IClock clock)
{
    /// <summary>Most course rows the dashboard table receives.</summary>
    public const int MaxCourseSummaries = 50;

    /// <summary>Number of finished live sessions reported in <see cref="InstructorAnalyticsResponse.LiveAttendance"/>.</summary>
    public const int MaxLiveAttendanceItems = 5;

    /// <summary>How many past sessions are read to find <see cref="MaxLiveAttendanceItems"/> that really ended (the newest by start time can still be running).</summary>
    private const int PastSessionLookback = 20;

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

        var now = clock.UtcNow;

        var allCourseIds = await catalogContract.GetCourseIdsByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        var courseStats = await courseStatsReader.GetByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);

        var kpis = await BuildKpisAsync(courseStats, allCourseIds, now, cancellationToken).ConfigureAwait(false);
        var courseSummaries = courseStats.Courses
            .Take(MaxCourseSummaries)
            .Select(c => new InstructorCourseSummaryItem(
                c.CourseId,
                c.Title,
                c.Slug,
                c.Status,
                c.DeliveryFormat,
                c.Price,
                c.EnrollmentCount,
                c.RatingCount > 0 ? c.RatingAverage : null,
                c.RatingCount,
                c.ThumbnailUrl))
            .ToList();

        if (allCourseIds.Count == 0)
        {
            // Nothing to analyse (no profile, or no live course): the original all-zero body, with the real (possibly zero) headline numbers.
            return Result.Success(new InstructorAnalyticsResponse(0, 0, 0m, 0m, 0, [], [], [], kpis, courseSummaries, null, []));
        }

        var titles = await catalogContract.GetCourseTitlesAsync(allCourseIds, cancellationToken).ConfigureAwait(false);

        // A course whose title cannot be resolved is left out of the filter list instead of being shown under an
        // invented name ("Unknown Course").
        var courseOptions = allCourseIds
            .Where(id => titles.TryGetValue(id, out var title) && !string.IsNullOrWhiteSpace(title))
            .Select(id => new InstructorCourseOption(id, titles[id]))
            .ToList();

        var targetCourseIds = courseIdFilter.HasValue && allCourseIds.Contains(courseIdFilter.Value)
            ? new List<Guid> { courseIdFilter.Value }
            : allCourseIds;

        var days = range switch
        {
            "7d" => 7,
            "90d" => 90,
            _ => 30,
        };

        var today = DateOnly.FromDateTime(now);
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

        // 2. Drop-off stats
        var episodes = await catalogContract.GetEpisodesForCoursesAsync(targetCourseIds, cancellationToken).ConfigureAwait(false);

        // Real watch time: the recorded watched seconds of the progress rows active since the start of the range, in whole minutes — not a views multiplier.
        var watchedSeconds = await learningContract
            .GetWatchedSecondsAsync(targetCourseIds, fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken)
            .ConfigureAwait(false);
        var totalWatchMinutes = (int)Math.Min(int.MaxValue, Math.Max(0L, watchedSeconds) / 60L);

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

        var students = new List<InstructorStudentProgress>(progressRecords.Count);
        foreach (var p in progressRecords)
        {
            // Real data only: a row whose course cannot be resolved (deleted) is left out rather than shown under an invented title,
            // and an unresolvable learner is reported as null name/e-mail — never as a made-up value.
            if (!titles.TryGetValue(p.CourseId, out var courseTitle) || string.IsNullOrWhiteSpace(courseTitle))
            {
                continue;
            }

            var hasContact = userContacts.TryGetValue(p.UserId, out var contact);

            // Real counts: the course's actual episode total, and the episodes this learner actually finished
            // (never more than the course has).
            var totalEps = totalEpisodeCountByCourse.TryGetValue(p.CourseId, out var count) ? count : 0;
            var completedEps = Math.Min(Math.Max(0, p.CompletedEpisodes), totalEps);

            students.Add(new InstructorStudentProgress(
                p.UserId.ToString(),
                hasContact && !string.IsNullOrWhiteSpace(contact.DisplayName) ? contact.DisplayName : null,
                hasContact && !string.IsNullOrWhiteSpace(contact.Email) ? contact.Email : null,
                courseTitle,
                p.ProgressPercent,
                completedEps,
                totalEps,
                p.LastActiveAtUtc));
        }

        // 4. Live: the next session not yet ended + attendance of the latest finished ones.
        var (nextSession, liveAttendanceItems) = await BuildLiveSectionAsync(instructorUserId, now, cancellationToken).ConfigureAwait(false);

        return Result.Success(new InstructorAnalyticsResponse(
            totalViews,
            totalWatchMinutes,
            avgCompletionRate,
            totalRevenue,
            totalEnrollments,
            dropOffList,
            students,
            courseOptions,
            kpis,
            courseSummaries,
            nextSession,
            liveAttendanceItems));
    }

    private async Task<InstructorDashboardKpis> BuildKpisAsync(
        InstructorCourseStatsInfo courseStats,
        IReadOnlyList<Guid> allCourseIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Revenue periods are UTC months — the same key REVENUE_SPLITS.PERIOD_KEY is written with at the moment a split is created.
        var periodKey = ToPeriodKey(now);
        var previousPeriodKey = ToPeriodKey(now.AddMonths(-1));

        // Money is keyed by the instructor PROFILE id (never the user id). It comes from the Catalog stats read, which derives it from the caller's own user
        // id, so the instructor only ever reads the revenue of their own profile. No profile = no revenue.
        var thisMonth = 0m;
        var previousMonth = 0m;
        if (courseStats.InstructorProfileId is { } profileId)
        {
            thisMonth = await revenueReader.GetNetRevenueForPeriodAsync(profileId, periodKey, cancellationToken).ConfigureAwait(false);
            previousMonth = await revenueReader.GetNetRevenueForPeriodAsync(profileId, previousPeriodKey, cancellationToken).ConfigureAwait(false);
        }

        // Nothing to compare with when the previous month earned nothing. The base is the absolute value so a (rare) net-negative month still gives the
        // right sign: going from -100 to 0 is an improvement.
        decimal? changePercent = previousMonth == 0m
            ? null
            : Math.Round((thisMonth - previousMonth) / Math.Abs(previousMonth) * 100m, 1, MidpointRounding.AwayFromZero);

        var learners = allCourseIds.Count == 0
            ? new LearnerCounts(0, 0)
            : await learningContract.GetLearnerCountsAsync(allCourseIds, now.AddDays(-7), cancellationToken).ConfigureAwait(false);

        var ratedCourses = courseStats.Courses.Where(c => c.RatingCount > 0).ToList();
        var ratingCount = ratedCourses.Sum(c => c.RatingCount);
        decimal? ratingAverage = ratingCount > 0
            ? Math.Round(ratedCourses.Sum(c => c.RatingAverage * c.RatingCount) / ratingCount, 2, MidpointRounding.AwayFromZero)
            : null;

        return new InstructorDashboardKpis(
            periodKey,
            previousPeriodKey,
            thisMonth,
            previousMonth,
            changePercent,
            learners.DistinctLearners,
            learners.DistinctLearnersSince,
            courseStats.Courses.Count(c => c.Status == InstructorCourseStatValues.PublishedStatus),
            courseStats.Courses.Count,
            ratingAverage,
            ratingCount);
    }

    private async Task<(InstructorNextSession? Next, IReadOnlyList<InstructorLiveAttendanceItem> Attendance)> BuildLiveSectionAsync(
        Guid instructorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Both reads are filtered by the caller's user id inside the schedule reader (instructor -> profile -> courses -> sessions).
        // "Next" = overlapping [now, +inf): the earliest session that has not ended, so a session being taught right now counts.
        var nextPage = await liveSchedule
            .GetInstructorSessionContextsAsync(instructorUserId, now, null, includeCancelled: false, newestFirst: false, skip: 0, take: 1, cancellationToken)
            .ConfigureAwait(false);
        var pastPage = await liveSchedule
            .GetInstructorSessionContextsAsync(instructorUserId, null, now, includeCancelled: false, newestFirst: true, skip: 0, take: PastSessionLookback, cancellationToken)
            .ConfigureAwait(false);

        var next = nextPage.Items.FirstOrDefault();
        var finished = pastPage.Items
            .Where(session => session.EndsAtUtc <= now)
            .Take(MaxLiveAttendanceItems)
            .ToList();

        if (next is null && finished.Count == 0)
        {
            return (null, []);
        }

        // One batch for every session shown (never a call per session).
        var sessionIds = finished.Select(session => session.SessionId).ToList();
        if (next is not null)
        {
            sessionIds.Add(next.SessionId);
        }

        var statsBySession = await liveAttendance.GetSessionStatsAsync(sessionIds.Distinct().ToList(), cancellationToken).ConfigureAwait(false);

        InstructorNextSession? nextSession = null;
        if (next is not null)
        {
            statsBySession.TryGetValue(next.SessionId, out var nextStats);
            nextSession = new InstructorNextSession(
                next.SessionId,
                next.CourseId,
                next.CourseTitle,
                next.Title,
                next.StartsAtUtc,
                next.EndsAtUtc,
                LiveSessionDisplayStateCalculator.Compute(next.Status, next.StartsAtUtc, next.EndsAtUtc, now),
                nextStats?.ExpectedLearners ?? 0,
                nextStats?.MeetingUsable ?? false);
        }

        var attendance = finished
            .Select(session =>
            {
                statsBySession.TryGetValue(session.SessionId, out var sessionStats);
                var expected = sessionStats?.ExpectedLearners ?? 0;
                var joined = sessionStats?.JoinedLearners ?? 0;
                return new InstructorLiveAttendanceItem(
                    session.SessionId,
                    session.CourseId,
                    session.CourseTitle,
                    session.Title,
                    session.StartsAtUtc,
                    expected,
                    joined,
                    expected > 0 ? Math.Round(joined * 100m / expected, 1, MidpointRounding.AwayFromZero) : null);
            })
            .ToList();

        return (nextSession, attendance);
    }

    /// <summary>The revenue period key (<c>yyyy-MM</c>) of a UTC instant. Culture-invariant on purpose: a Thai-Buddhist-calendar culture would otherwise write 2569 for 2026.</summary>
    private static string ToPeriodKey(DateTime utc) => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
