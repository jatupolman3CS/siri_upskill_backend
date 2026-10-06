using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure.Contracts;

public sealed class CatalogPriceContract(AppDbContext dbContext) : ICatalogPriceContract, ICourseOwnershipVerifier, ICourseSummaryReader
{
    public async Task<IReadOnlyDictionary<Guid, CourseSummaryInfo>> GetCourseSummariesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var ids = courseIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, CourseSummaryInfo>();
        }

        var summaries = await (
            from course in dbContext.Courses().AsNoTracking()
            where ids.Contains(course.Id)
            join instructor in dbContext.InstructorProfiles().AsNoTracking()
                on course.InstructorId equals instructor.Id into instructors
            from instructor in instructors.DefaultIfEmpty()
            select new CourseSummaryInfo(
                course.Id, course.Slug, course.Title, course.ThumbnailUrl,
                instructor == null ? null : instructor.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return summaries.ToDictionary(course => course.CourseId);
    }

    public async Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, CoursePriceInfo>();
        }

        var courses = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => idList.Contains(c.Id) && c.Status == CourseStatus.Published)
            .Select(c => new CoursePriceInfo(c.Id, c.Title, c.Price, c.InstructorId, c.AccessDurationDays))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return courses.ToDictionary(c => c.CourseId);
    }

    public async Task<bool> IsInstructorOwnerOfEpisodeAsync(
        Guid episodeId,
        Guid instructorUserId,
        CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty)
        {
            return false;
        }

        var episodeCourseId = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Select(e => (Guid?)e.CourseId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (episodeCourseId is null)
        {
            return false;
        }

        return await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Id == episodeCourseId.Value)
            .Join(
                dbContext.InstructorProfiles().AsNoTracking(),
                course => course.InstructorId,
                profile => profile.Id,
                (course, profile) => profile.UserId)
            .AnyAsync(ownerUserId => ownerUserId == instructorUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsInstructorOwnerOfCourseAsync(
        Guid courseId,
        Guid instructorUserId,
        CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty)
        {
            return false;
        }

        return await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Join(
                dbContext.InstructorProfiles().AsNoTracking(),
                course => course.InstructorId,
                profile => profile.Id,
                (course, profile) => profile.UserId)
            .AnyAsync(ownerUserId => ownerUserId == instructorUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsEpisodeFreePreviewAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CourseEpisodes()
            .AsNoTracking()
            .AnyAsync(e => e.Id == episodeId && e.IsFreePreview &&
                dbContext.Courses().Any(c => c.Id == e.CourseId && c.Status == CourseStatus.Published), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid?> GetCourseIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Select(e => new { e.CourseId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return episode?.CourseId;
    }

    public async Task<Guid?> GetMediaAssetIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Select(e => new { e.MediaAssetId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return episode?.MediaAssetId;
    }

    public async Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Status == CourseStatus.InReview)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var courses = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => idList.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return courses.ToDictionary(c => c.Id, c => c.Title);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(
        IEnumerable<Guid> instructorIds,
        CancellationToken cancellationToken)
    {
        var idList = instructorIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var profiles = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => idList.Contains(p.Id))
            .Select(p => new { p.Id, p.RevenueSharePercent })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return profiles.ToDictionary(p => p.Id, p => p.RevenueSharePercent);
    }

    public async Task<IReadOnlyList<Guid>> GetCourseIdsByInstructorUserIdAsync(
        Guid instructorUserId,
        CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty)
        {
            return Array.Empty<Guid>();
        }

        return await dbContext.Courses()
            .AsNoTracking()
            .Join(
                dbContext.InstructorProfiles().AsNoTracking(),
                course => course.InstructorId,
                profile => profile.Id,
                (course, profile) => new { course.Id, profile.UserId })
            .Where(x => x.UserId == instructorUserId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CourseEpisodeInfo>> GetEpisodesForCoursesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return Array.Empty<CourseEpisodeInfo>();
        }

        var episodes = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .Where(e => idList.Contains(e.CourseId))
            .OrderBy(e => e.SortOrder)
            .Select(e => new CourseEpisodeInfo(e.Id, e.CourseId, e.Title, e.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return episodes;
    }

    public async Task<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>> GetEnrollmentPoliciesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, CourseEnrollmentPolicyInfo>();
        }

        var policies = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => idList.Contains(c.Id))
            .Select(c => new CourseEnrollmentPolicyInfo(c.Id, c.EnrollmentDeadlineUtc, c.MaxSeats, c.SeatsUsed))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return policies.ToDictionary(p => p.CourseId);
    }

    // ExecuteUpdateAsync writes straight to the DB, bypassing the change tracker/interceptors entirely
    // (RowVersion/UpdatedAtUtc/UpdatedBy are not touched) — the same behavior PROMO_CODE.REDEEMED_COUNT
    // already has (PromoCodeRepository.TryRedeemAsync), not a new bug. Safe because Commerce never
    // tracks a COURSE entity via EF (no reference to Siri.Modules.Catalog.Domain at all — only through
    // Contracts/), so there is no tracked entity in the same DbContext that could see a stale value and
    // overwrite it on a later SaveChangesAsync.
    public async Task<bool> TryReserveSeatAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var affected = await dbContext.Courses()
            .Where(c => c.Id == courseId && (c.MaxSeats == null || c.SeatsUsed < c.MaxSeats))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SeatsUsed, c => c.SeatsUsed + 1), cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    public async Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await dbContext.Courses()
            .Where(c => c.Id == courseId && c.SeatsUsed > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SeatsUsed, c => c.SeatsUsed - 1), cancellationToken)
            .ConfigureAwait(false);
    }
}
