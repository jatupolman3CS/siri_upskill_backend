using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.Persistence;
using Siri.UnitTests.Live;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// D2 (integrator-qa): the single writer of <c>COURSES.ENROLLMENT_COUNT</c> and the hourly recount that heals it. What the SQL does under concurrency and across the two
/// modules is proven by the Testcontainers integration tests; this file covers what a machine without Docker can still check: the pure drift comparison, that every
/// statement the updater and Learning's counter issue TRANSLATES (EF translates before it opens a connection, so against an unreachable server a translatable query ends in
/// the connection-opening interceptor — the technique of <c>LiveJoinQueryTranslationTests</c>), the guard clauses that must not touch the database, and the job's wiring.
/// </summary>
public class CourseEnrollmentCountUpdaterTests
{
    private static readonly Guid SomeId = Guid.NewGuid();

    private sealed class ReachedDatabaseException : Exception;

    private sealed class StopAtConnectionOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ReachedDatabaseException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ReachedDatabaseException();
    }

    private sealed class NoCounter : ILearningEnrollmentCounter
    {
        public Task<IReadOnlyDictionary<Guid, int>> CountEnrollmentsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the source of truth must not be asked in this test");
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

    private static async Task AssertTranslatesAsync(Func<AppDbContext, Task> query)
    {
        using var context = Context();

        var exception = await Record.ExceptionAsync(() => query(context));

        Assert.NotNull(exception);
        Assert.True(Chain(exception).Any(e => e is ReachedDatabaseException), exception.ToString());
    }

    private static async Task AssertTouchesNoDatabaseAsync(Func<AppDbContext, Task> query)
    {
        using var context = Context();

        var exception = await Record.ExceptionAsync(() => query(context));

        Assert.Null(exception); // a connection attempt would have thrown ReachedDatabaseException
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static CourseEnrollmentCountUpdater Updater(AppDbContext context) => new(context, new NoCounter());

    // ---- Drift (pure) ---------------------------------------------------------------------------------------

    [Fact]
    public void FindDrifted_ReturnsOnlyTheCoursesWhoseStoredCountDiffers()
    {
        var right = Guid.NewGuid();
        var tooLow = Guid.NewGuid();
        var tooHigh = Guid.NewGuid();
        var neverCounted = Guid.NewGuid(); // the source of truth does not mention it: zero enrollments
        var zeroAndRight = Guid.NewGuid();

        var drifted = CourseEnrollmentCountDrift.FindDrifted(
            [(right, 5), (tooLow, 2), (tooHigh, 9), (neverCounted, 3), (zeroAndRight, 0)],
            new Dictionary<Guid, int> { [right] = 5, [tooLow] = 4, [tooHigh] = 1 });

        Assert.Equal([tooLow, tooHigh, neverCounted], drifted);
    }

    [Fact]
    public void FindDrifted_NothingStored_OrAllCorrect_IsEmpty()
    {
        var id = Guid.NewGuid();

        Assert.Empty(CourseEnrollmentCountDrift.FindDrifted([], new Dictionary<Guid, int>()));
        Assert.Empty(CourseEnrollmentCountDrift.FindDrifted([(id, 7)], new Dictionary<Guid, int> { [id] = 7 }));
    }

    // ---- The statements translate -----------------------------------------------------------------------------

    [Fact]
    public Task Adjust_Increment_Translates() =>
        AssertTranslatesAsync(c => Updater(c).AdjustAsync(SomeId, 1, CancellationToken.None));

    [Fact]
    public Task Adjust_Decrement_Translates() =>
        AssertTranslatesAsync(c => Updater(c).AdjustAsync(SomeId, -1, CancellationToken.None));

    [Fact]
    public Task Recount_LockStatement_Translates() =>
        AssertTranslatesAsync(c => Updater(c).LockCourseRowAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task Recount_StoreStatement_Translates() =>
        AssertTranslatesAsync(c => Updater(c).StoreCountAsync(SomeId, 12, CancellationToken.None));

    [Fact]
    public Task RecountDrifted_FirstQuery_Translates() =>
        AssertTranslatesAsync(c => Updater(c).RecountDriftedAsync(CourseEnrollmentCountUpdater.DefaultPageSize, CancellationToken.None));

    [Fact]
    public Task Learning_CountEnrollments_GroupedQuery_Translates() =>
        AssertTranslatesAsync(c => new LearningEnrollmentCounter(c).CountEnrollmentsAsync([SomeId, Guid.NewGuid(), SomeId], CancellationToken.None));

    // ---- Guard clauses (no database touched) ------------------------------------------------------------------

    [Fact]
    public Task Adjust_ZeroDelta_TouchesNothing() =>
        AssertTouchesNoDatabaseAsync(c => Updater(c).AdjustAsync(SomeId, 0, CancellationToken.None));

    [Fact]
    public Task Adjust_EmptyCourseId_TouchesNothing() =>
        AssertTouchesNoDatabaseAsync(c => Updater(c).AdjustAsync(Guid.Empty, 1, CancellationToken.None));

    [Fact]
    public async Task Recount_EmptyCourseId_IsZero_AndTouchesNothing()
    {
        using var context = Context();

        Assert.Equal(0, await Updater(context).RecountAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task Learning_CountEnrollments_NoUsableIds_AsksNothing_AndMapsNothing()
    {
        using var context = Context();

        var counts = await new LearningEnrollmentCounter(context).CountEnrollmentsAsync([Guid.Empty], CancellationToken.None);

        Assert.Empty(counts);
    }

    // ---- The hourly job ---------------------------------------------------------------------------------------

    private sealed class RecordingUpdater(int corrected) : ICourseEnrollmentCountUpdater
    {
        public List<int> PageSizes { get; } = [];

        public Task AdjustAsync(Guid courseId, int delta, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RecountAsync(Guid courseId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RecountDriftedAsync(int pageSize, CancellationToken cancellationToken)
        {
            PageSizes.Add(pageSize);
            return Task.FromResult(corrected);
        }
    }

    [Fact]
    public async Task Job_RunsTheDriftRecountInBoundedPages_AndReportsWhatItFixed()
    {
        var updater = new RecordingUpdater(corrected: 3);
        var logger = new ListLogger<CourseEnrollmentRecountJob>();

        await new CourseEnrollmentRecountJob(updater, logger).RunAsync(CancellationToken.None);

        Assert.Equal([CourseEnrollmentRecountJob.PageSize], updater.PageSizes);
        Assert.InRange(CourseEnrollmentRecountJob.PageSize, 1, 1000);
        Assert.Contains("3 course", logger.All);
    }

    [Fact]
    public async Task Job_WhenNothingDrifted_IsSilent()
    {
        var logger = new ListLogger<CourseEnrollmentRecountJob>();

        await new CourseEnrollmentRecountJob(new RecordingUpdater(corrected: 0), logger).RunAsync(CancellationToken.None);

        Assert.Empty(logger.Messages);
    }
}
