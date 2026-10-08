using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Application;

/// <summary>
/// The only code that writes <c>COURSES.ENROLLMENT_COUNT</c> (see <see cref="ICourseEnrollmentCountUpdater"/> for the contract and the definition of the count).
/// Every write is a set-based <c>ExecuteUpdate</c> — never the tracked <c>COURSE</c> entity — so a concurrent instructor edit neither overwrites nor invalidates it.
/// </summary>
public sealed class CourseEnrollmentCountUpdater(AppDbContext dbContext, ILearningEnrollmentCounter enrollmentCounter) : ICourseEnrollmentCountUpdater
{
    /// <summary>Default page size of <see cref="RecountDriftedAsync"/> (and of the hourly job).</summary>
    public const int DefaultPageSize = 200;

    public async Task AdjustAsync(Guid courseId, int delta, CancellationToken cancellationToken)
    {
        if (courseId == Guid.Empty || delta == 0)
        {
            return;
        }

        // One atomic statement (same pattern as SeatsUsed in CatalogPriceContract). A negative delta never takes the counter below zero: the WHERE clause makes
        // it a no-op instead — a count that is already too low is for the recount to repair, not for a negative number to expose.
        var courses = delta > 0
            ? dbContext.Courses().Where(c => c.Id == courseId)
            : dbContext.Courses().Where(c => c.Id == courseId && c.EnrollmentCount >= -delta);

        await courses
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.EnrollmentCount, c => c.EnrollmentCount + delta), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RecountAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (courseId == Guid.Empty)
        {
            return 0;
        }

        // Join the caller's transaction when there is one (the row lock then lasts until it ends), otherwise own a short one.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // 1. Lock the course row (see LockCourseRowAsync): concurrent AdjustAsync calls and other recounts wait here until this transaction ends. Zero rows =
        //    unknown or soft-deleted course — nothing to maintain.
        if (await LockCourseRowAsync(courseId, cancellationToken).ConfigureAwait(false) == 0)
        {
            return 0;
        }

        // 2. Count AFTER the lock is held (a new statement, so it sees everything committed so far); an adjuster that was waiting applies its delta on top of the
        //    value stored here once this transaction commits — and that enrollment was not yet committed when we counted, so nothing is counted twice or lost.
        var counts = await enrollmentCounter.CountEnrollmentsAsync([courseId], cancellationToken).ConfigureAwait(false);
        var count = counts.TryGetValue(courseId, out var counted) ? counted : 0;

        await StoreCountAsync(courseId, count, cancellationToken).ConfigureAwait(false);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>Takes the course row's lock by "touching" it (<c>SET x = x</c>) — held until the surrounding transaction ends. Returns 0 for an unknown or soft-deleted course.</summary>
    internal Task<int> LockCourseRowAsync(Guid courseId, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .Where(c => c.Id == courseId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.EnrollmentCount, c => c.EnrollmentCount), cancellationToken);

    /// <summary>Stores the recounted value (an absolute set, only ever reached while <see cref="LockCourseRowAsync"/>'s lock is held).</summary>
    internal Task<int> StoreCountAsync(Guid courseId, int count, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .Where(c => c.Id == courseId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.EnrollmentCount, count), cancellationToken);

    public async Task<int> RecountDriftedAsync(int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, 1000);

        // Ids only (bounded memory), in a stable order; everything else is read one page at a time.
        var courseIds = await dbContext.Courses()
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var corrected = 0;
        foreach (var page in courseIds.Chunk(size))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stored = await dbContext.Courses()
                .AsNoTracking()
                .Where(c => page.Contains(c.Id))
                .Select(c => new { c.Id, c.EnrollmentCount })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var truth = await enrollmentCounter.CountEnrollmentsAsync(page, cancellationToken).ConfigureAwait(false);

            foreach (var row in CourseEnrollmentCountDrift.FindDrifted(stored.Select(s => (s.Id, s.EnrollmentCount)), truth))
            {
                // The cheap comparison only nominates candidates; the locked recount decides (a concurrent enrollment may already have fixed it).
                await RecountAsync(row, cancellationToken).ConfigureAwait(false);
                corrected++;
            }
        }

        return corrected;
    }
}

/// <summary>The pure part of the recount: which courses need correcting.</summary>
internal static class CourseEnrollmentCountDrift
{
    /// <summary>Ids of the courses whose stored counter differs from the source of truth (a course the source does not mention counts as 0).</summary>
    public static IReadOnlyList<Guid> FindDrifted(IEnumerable<(Guid CourseId, int StoredCount)> stored, IReadOnlyDictionary<Guid, int> truth)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(truth);

        return stored
            .Where(row => row.StoredCount != (truth.TryGetValue(row.CourseId, out var count) ? count : 0))
            .Select(row => row.CourseId)
            .ToList();
    }
}
