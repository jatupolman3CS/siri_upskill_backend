using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;

namespace Siri.UnitTests.Live;

/// <summary>
/// Proves that every EF query the Live module (and Catalog's new <c>LiveScheduleReader</c> context queries) runs can actually be TRANSLATED to SQL —
/// without a database. EF translates a query before it opens a connection, so against an unreachable server a translatable query fails with a
/// connection error (<see cref="DbException"/>), while an untranslatable one fails with an <see cref="InvalidOperationException"/> that says so.
/// The behaviour of the SQL (window semantics, paging, soft-delete) is covered by the Testcontainers integration tests; this guards the part a
/// machine without Docker can still check.
/// </summary>
public class LiveQueryTranslationTests
{
    private static readonly Guid SomeId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

    /// <summary>Thrown by <see cref="StopAtConnectionOpen"/> the moment EF is ready to talk to the database — i.e. translation succeeded.</summary>
    private sealed class ReachedDatabaseException : Exception;

    private sealed class StopAtConnectionOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ReachedDatabaseException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ReachedDatabaseException();
    }

    private static async Task AssertTranslatesAsync(Func<AppDbContext, Task> query)
    {
        using var context = Context();

        var exception = await Record.ExceptionAsync(() => query(context));

        // There is no database, so the call must fail — and the only acceptable failure is "EF reached the point of opening a connection", which
        // happens only after the LINQ was translated and compiled. Anything else (typically "could not be translated") is a real bug.
        Assert.NotNull(exception);
        Assert.True(Chain(exception).Any(e => e is ReachedDatabaseException), exception.ToString());
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    // ---- Catalog.LiveScheduleReader (P11-03 contract 4.1) ---------------------------------------------

    [Fact]
    public Task GetSessionContexts_Translates() =>
        AssertTranslatesAsync(c => new LiveScheduleReader(c).GetSessionContextsAsync([SomeId, Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public Task GetSessionContextsInWindow_Translates() =>
        AssertTranslatesAsync(c => new LiveScheduleReader(c).GetSessionContextsInWindowAsync(Now, Now.AddDays(30), includeCancelled: false, CancellationToken.None));

    [Fact]
    public Task GetSessionContextsInWindow_WithCancelled_Translates() =>
        AssertTranslatesAsync(c => new LiveScheduleReader(c).GetSessionContextsInWindowAsync(Now, Now.AddDays(30), includeCancelled: true, CancellationToken.None));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task GetSessionContextsForCourses_Translates_ForEveryBoundCombination(bool hasFrom, bool hasTo) =>
        AssertTranslatesAsync(c => new LiveScheduleReader(c).GetSessionContextsForCoursesAsync(
            [SomeId], hasFrom ? Now : null, hasTo ? Now.AddDays(7) : null, includeCancelled: true, CancellationToken.None));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GetInstructorSessionContexts_Count_Translates(bool newestFirst) =>
        AssertTranslatesAsync(c => new LiveScheduleReader(c).GetInstructorSessionContextsAsync(
            SomeId, Now, Now.AddDays(30), includeCancelled: false, newestFirst, skip: 0, take: 20, CancellationToken.None));

    // ---- Live repositories --------------------------------------------------------------------------------

    [Fact]
    public Task SessionMeetings_GetBySessionId_Translates() =>
        AssertTranslatesAsync(c => new SessionMeetingRepository(c).GetBySessionIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task SessionMeetings_GetBySessionIds_Translates() =>
        AssertTranslatesAsync(c => new SessionMeetingRepository(c).GetBySessionIdsAsync([SomeId, Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public Task SessionMeetings_GetExistingSessionIds_Translates() =>
        AssertTranslatesAsync(c => new SessionMeetingRepository(c).GetExistingSessionIdsAsync([SomeId, Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public Task SessionMeetings_GetDueSessionIds_Translates() =>
        AssertTranslatesAsync(c => new SessionMeetingRepository(c).GetDueSessionIdsAsync(Now, 50, CancellationToken.None));

    [Fact]
    public Task SessionMeetings_GetResettableByInstructor_Translates() =>
        AssertTranslatesAsync(c => new SessionMeetingRepository(c).GetResettableByInstructorAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task InstructorGoogleAccounts_GetByInstructorUserId_Translates() =>
        AssertTranslatesAsync(c => new InstructorGoogleAccountRepository(c).GetByInstructorUserIdAsync(SomeId, CancellationToken.None));

    // ---- SessionRecordingImportRepository (P11-13) -------------------------------------------------------

    [Fact]
    public Task RecordingImports_GetBySessionId_Translates() =>
        AssertTranslatesAsync(c => new SessionRecordingImportRepository(c).GetBySessionIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task RecordingImports_GetById_Translates() =>
        AssertTranslatesAsync(c => new SessionRecordingImportRepository(c).GetByIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task RecordingImports_GetBySessionIds_Translates() =>
        AssertTranslatesAsync(c => new SessionRecordingImportRepository(c).GetBySessionIdsAsync([SomeId, Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public Task RecordingImports_GetExistingSessionIds_Translates() =>
        AssertTranslatesAsync(c => new SessionRecordingImportRepository(c).GetExistingSessionIdsAsync([SomeId, Guid.NewGuid()], CancellationToken.None));

    [Fact]
    public Task RecordingImports_GetDueIds_Translates() =>
        AssertTranslatesAsync(c => new SessionRecordingImportRepository(c).GetDueIdsAsync(Now, 5, CancellationToken.None));

    [Fact]
    public Task InstructorGoogleAccounts_GetRecordingCandidateInstructorIds_Translates() =>
        AssertTranslatesAsync(c => new InstructorGoogleAccountRepository(c).GetRecordingCandidateInstructorIdsAsync(CancellationToken.None));

    [Fact]
    public Task InstructorGoogleAccounts_RecordValidation_TranslatesAsASetBasedUpdate() =>
        AssertTranslatesAsync(c => new InstructorGoogleAccountRepository(c).RecordValidationAsync(
            INSTRUCTOR_GOOGLE_ACCOUNT.Connect(SomeId, "sub", "t@school.example.test", "ciphertext", "scope", new FakeClock(Now), "school.example.test"),
            Now,
            CancellationToken.None));

    [Fact]
    public Task CatalogAttacher_ListEnded_Translates() =>
        AssertTranslatesAsync(c => new Siri.Modules.Catalog.Infrastructure.Contracts.LiveRecordingAttacher(c, null!).ListEndedAsync(Now.AddHours(-48), Now, 200, CancellationToken.None));

    [Fact]
    public Task CatalogAttacher_ListEndedByInstructors_Translates_WithTheFilterInsideTheQuery() =>
        AssertTranslatesAsync(c => new Siri.Modules.Catalog.Infrastructure.Contracts.LiveRecordingAttacher(c, null!)
            .ListEndedByInstructorsAsync([SomeId, Guid.NewGuid()], Now.AddHours(-48), Now, 200, CancellationToken.None));

    [Fact]
    public async Task CatalogAttacher_ListEndedByInstructors_AnEmptySetOrRangeNeverTouchesTheDatabase()
    {
        using var context = Context();
        var attacher = new Siri.Modules.Catalog.Infrastructure.Contracts.LiveRecordingAttacher(context, null!);

        Assert.Empty(await attacher.ListEndedByInstructorsAsync([], Now.AddHours(-48), Now, 200, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedByInstructorsAsync([SomeId], Now, Now.AddHours(-1), 200, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedByInstructorsAsync([SomeId], Now.AddHours(-48), Now, 0, CancellationToken.None));
    }

    [Fact]
    public async Task RecordingImports_GetBySessionId_FindsARowAddedInTheSameUnitOfWork_WithoutTouchingTheDatabase()
    {
        using var context = Context();
        var repository = new SessionRecordingImportRepository(context);
        var import = Siri.Modules.Live.Domain.SESSION_RECORDING_IMPORT.Create(SomeId, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddHours(12));
        repository.Add(import);

        Assert.Same(import, await repository.GetBySessionIdAsync(SomeId, CancellationToken.None));
    }

    [Fact]
    public async Task SessionMeetings_GetBySessionId_FindsAMeetingStagedInTheSameUnitOfWork_WithoutTouchingTheDatabase()
    {
        using var context = Context();
        var repository = new SessionMeetingRepository(context);
        var meeting = Siri.Modules.Live.Domain.SESSION_MEETING.Stage(SomeId);
        repository.Add(meeting);

        // Would fail with a connection error if it queried the database: the tracker is consulted first.
        var found = await repository.GetBySessionIdAsync(SomeId, CancellationToken.None);

        Assert.Same(meeting, found);
    }
}
