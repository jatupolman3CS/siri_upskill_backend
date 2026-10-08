using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure.Contracts;
using Siri.Persistence;
using Xunit;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// <see cref="InstructorCourseStatsReader"/> / <see cref="InstructorProfileReader"/> (docs/contracts/P11-10-instructor-dashboard-summary.md §2.1 and the
/// user-id to profile-id resolution): without a database. EF translates a query before it opens a connection, so against an unreachable server a translatable
/// query ends with the connection-opening interceptor firing, while an untranslatable one fails with an <see cref="InvalidOperationException"/> that says so (the
/// same technique as <c>LiveJoinQueryTranslationTests</c>). What the rows contain (soft-delete, the 200 cap, ordering) is covered by the Testcontainers integration tests.
/// </summary>
public sealed class InstructorStatsReadersTests
{
    private static readonly Guid SomeId = Guid.NewGuid();

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

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

    [Fact]
    public Task CourseStatsReader_ProfileLookup_Translates() =>
        AssertTranslatesAsync(c => new InstructorCourseStatsReader(c).GetByInstructorUserIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task CourseStatsReader_CourseQuery_Translates() =>
        AssertTranslatesAsync(c => new InstructorCourseStatsReader(c).ReadCoursesAsync(SomeId, CancellationToken.None));

    [Fact]
    public async Task CourseStatsReader_EmptyUserId_HasNoProfileAndNoCourses_WithoutTouchingTheDatabase()
    {
        using var context = Context();

        var info = await new InstructorCourseStatsReader(context).GetByInstructorUserIdAsync(Guid.Empty, CancellationToken.None);

        Assert.Null(info.InstructorProfileId);
        Assert.Empty(info.Courses);
    }

    [Fact]
    public Task ProfileReader_Lookup_Translates() =>
        AssertTranslatesAsync(c => new InstructorProfileReader(c).GetProfileIdByUserIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public async Task ProfileReader_EmptyUserId_HasNoProfile_WithoutTouchingTheDatabase()
    {
        using var context = Context();

        Assert.Null(await new InstructorProfileReader(context).GetProfileIdByUserIdAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public void StatValues_PublishedStatus_IsTheDomainEnumMemberName()
    {
        // The reader hands the status over as the enum member name; consumers compare against this constant, so a renamed member must fail here.
        Assert.Equal(InstructorCourseStatValues.PublishedStatus, nameof(CourseStatus.Published));
        Assert.Equal(InstructorCourseStatValues.PublishedStatus, CourseStatus.Published.ToString());
    }

    [Fact]
    public void CourseStatsReader_Cap_IsTwoHundred() => Assert.Equal(200, InstructorCourseStatsReader.MaxCourses);
}
