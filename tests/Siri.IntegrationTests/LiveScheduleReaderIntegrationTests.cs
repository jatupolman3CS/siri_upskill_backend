using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// The new <see cref="ILiveScheduleReader"/> context queries (P11-03 contract section 4.1) against a real PostgreSQL: the joins session → course →
/// instructor profile, the exact window semantics (start window vs overlap), cancelled handling, paging, and soft-deleted courses. These
/// are the EF translations a fake cannot prove, so they are covered here rather than in the unit suite. Requires Docker like every test in this
/// collection.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveScheduleReaderIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LiveScheduleReaderIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private sealed record Seeded(TestInstructor Instructor, Guid CourseId, string Slug, Guid FirstSessionId, Guid SecondSessionId, Guid CancelledSessionId);

    /// <summary>One Live course with three sessions: day+1 (2 h), day+2 (1 h) and a cancelled one on day+3.</summary>
    private async Task<Seeded> SeedAsync()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        db.Categories().Add(category);
        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "คอร์สตารางสอน", instructor.ProfileId, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 500m);
        course.SetDeliveryFormat(DeliveryFormat.Live);

        var now = clock.UtcNow;
        var first = course.AddLiveSession("คาบแรก", "รายละเอียด", now.AddDays(1), now.AddDays(1).AddHours(2), clock);
        var second = course.AddLiveSession("คาบสอง", null, now.AddDays(2), now.AddDays(2).AddHours(1), clock);
        var cancelled = course.AddLiveSession("คาบที่ถูกยกเลิก", null, now.AddDays(3), now.AddDays(3).AddHours(1), clock);
        course.CancelLiveSession(cancelled.Id, "ผู้สอนไม่ว่าง", clock);

        db.Courses().Add(course);
        await db.SaveChangesAsync();

        return new Seeded(instructor, course.Id, course.Slug, first.Id, second.Id, cancelled.Id);
    }

    private async Task<T> WithReaderAsync<T>(Func<ILiveScheduleReader, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILiveScheduleReader>());
    }

    [Fact]
    public async Task GetSessionContexts_ReturnsTheJoinedFacts_IncludingCancelled_AndSkipsUnknownIds()
    {
        var seeded = await SeedAsync();

        var contexts = await WithReaderAsync(reader => reader.GetSessionContextsAsync(
            [seeded.FirstSessionId, seeded.CancelledSessionId, Guid.NewGuid()], CancellationToken.None));

        Assert.Equal(2, contexts.Count);
        var first = contexts.Single(c => c.SessionId == seeded.FirstSessionId);
        Assert.Equal(seeded.CourseId, first.CourseId);
        Assert.Equal("คอร์สตารางสอน", first.CourseTitle);
        Assert.Equal(seeded.Slug, first.CourseSlug);
        Assert.Equal("คาบแรก", first.Title);
        Assert.Equal("รายละเอียด", first.Description);
        Assert.Equal(LiveSessionStatus.Scheduled, first.Status);
        Assert.Equal(seeded.Instructor.ProfileId, first.InstructorProfileId);
        Assert.Equal(seeded.Instructor.UserId, first.InstructorUserId);
        Assert.Equal("Test Instructor", first.InstructorDisplayName);
        Assert.False(first.GoogleAttendeeSyncEnabled);

        var cancelled = contexts.Single(c => c.SessionId == seeded.CancelledSessionId);
        Assert.Equal(LiveSessionStatus.Cancelled, cancelled.Status);
        Assert.Equal("ผู้สอนไม่ว่าง", cancelled.CancelReason);
    }

    [Fact]
    public async Task GetSessionContexts_EmptyInput_IsEmpty()
    {
        var contexts = await WithReaderAsync(reader => reader.GetSessionContextsAsync([], CancellationToken.None));

        Assert.Empty(contexts);
    }

    [Fact]
    public async Task GetSessionContexts_ReflectsTheCoursesAttendeeSyncOptIn()
    {
        var seeded = await SeedAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses().SingleAsync(c => c.Id == seeded.CourseId)).SetGoogleAttendeeSync(true);
            await db.SaveChangesAsync();
        }

        var contexts = await WithReaderAsync(reader => reader.GetSessionContextsAsync([seeded.FirstSessionId], CancellationToken.None));

        Assert.True(Assert.Single(contexts).GoogleAttendeeSyncEnabled);
    }

    [Fact]
    public async Task InWindow_IsAStartWindow_HalfOpen_AndExcludesCancelledUnlessAsked()
    {
        var seeded = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var first = await WithReaderAsync(r => r.GetSessionsForCourseAsync(seeded.CourseId, CancellationToken.None));
        var firstStart = first.Single(s => s.SessionId == seeded.FirstSessionId).StartsAtUtc;

        // [firstStart, firstStart + 1 day): includes the first session (inclusive start), excludes the second (starts exactly at the end of the window).
        var narrow = await WithReaderAsync(r => r.GetSessionContextsInWindowAsync(firstStart, firstStart.AddDays(1), includeCancelled: false, CancellationToken.None));
        var mine = narrow.Where(c => c.CourseId == seeded.CourseId).Select(c => c.SessionId).ToArray();
        Assert.Contains(seeded.FirstSessionId, mine);
        Assert.DoesNotContain(seeded.SecondSessionId, mine);

        // A window that starts after the first session started does not return it (start window, not overlap).
        var late = await WithReaderAsync(r => r.GetSessionContextsInWindowAsync(firstStart.AddMinutes(30), now.AddDays(10), includeCancelled: false, CancellationToken.None));
        Assert.DoesNotContain(late, c => c.SessionId == seeded.FirstSessionId);

        var withoutCancelled = await WithReaderAsync(r => r.GetSessionContextsInWindowAsync(now, now.AddDays(10), includeCancelled: false, CancellationToken.None));
        var withCancelled = await WithReaderAsync(r => r.GetSessionContextsInWindowAsync(now, now.AddDays(10), includeCancelled: true, CancellationToken.None));
        Assert.DoesNotContain(withoutCancelled, c => c.SessionId == seeded.CancelledSessionId);
        Assert.Contains(withCancelled, c => c.SessionId == seeded.CancelledSessionId);
    }

    [Fact]
    public async Task ForCourses_UsesOverlap_SoASessionInProgressIsIncluded_AndNullBoundsAreOpen()
    {
        var seeded = await SeedAsync();
        var sessions = await WithReaderAsync(r => r.GetSessionsForCourseAsync(seeded.CourseId, CancellationToken.None));
        var firstStart = sessions.Single(s => s.SessionId == seeded.FirstSessionId).StartsAtUtc;

        // The window opens 30 minutes INTO the first session (which runs 2 h) and closes before the second one starts.
        var overlapping = await WithReaderAsync(r => r.GetSessionContextsForCoursesAsync(
            [seeded.CourseId], firstStart.AddMinutes(30), firstStart.AddHours(5), includeCancelled: false, CancellationToken.None));
        Assert.Equal([seeded.FirstSessionId], overlapping.Select(c => c.SessionId).ToArray());

        // A window that opens after the first session ended no longer includes it.
        var afterEnd = await WithReaderAsync(r => r.GetSessionContextsForCoursesAsync(
            [seeded.CourseId], firstStart.AddHours(3), firstStart.AddDays(5), includeCancelled: false, CancellationToken.None));
        Assert.DoesNotContain(afterEnd, c => c.SessionId == seeded.FirstSessionId);
        Assert.Contains(afterEnd, c => c.SessionId == seeded.SecondSessionId);

        var everything = await WithReaderAsync(r => r.GetSessionContextsForCoursesAsync(
            [seeded.CourseId], null, null, includeCancelled: true, CancellationToken.None));
        Assert.Equal(
            [seeded.FirstSessionId, seeded.SecondSessionId, seeded.CancelledSessionId],
            everything.Select(c => c.SessionId).ToArray()); // ordered by start time
    }

    [Fact]
    public async Task ForCourses_NoCourseIds_IsEmpty()
    {
        var contexts = await WithReaderAsync(r => r.GetSessionContextsForCoursesAsync([], null, null, includeCancelled: true, CancellationToken.None));

        Assert.Empty(contexts);
    }

    [Fact]
    public async Task InstructorPage_PagesWithATotal_OrdersBothWays_AndOnlyReturnsThatInstructorsSessions()
    {
        var seeded = await SeedAsync();
        var other = await SeedAsync(); // another instructor with their own three sessions

        var page1 = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: 0, take: 2, CancellationToken.None));
        var page2 = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: 2, take: 2, CancellationToken.None));
        var newest = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: true, skip: 0, take: 10, CancellationToken.None));
        var scheduledOnly = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: false, newestFirst: false, skip: 0, take: 10, CancellationToken.None));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([seeded.FirstSessionId, seeded.SecondSessionId], page1.Items.Select(c => c.SessionId).ToArray());
        Assert.Equal([seeded.CancelledSessionId], page2.Items.Select(c => c.SessionId).ToArray());
        Assert.Equal([seeded.CancelledSessionId, seeded.SecondSessionId, seeded.FirstSessionId], newest.Items.Select(c => c.SessionId).ToArray());
        Assert.Equal(2, scheduledOnly.TotalCount);
        Assert.DoesNotContain(page1.Items.Concat(page2.Items), c => c.InstructorUserId == other.Instructor.UserId);
    }

    [Fact]
    public async Task InstructorPage_TakeIsClamped_AndASkipBeyondTheEndIsAnEmptyPageWithTheTotal()
    {
        var seeded = await SeedAsync();

        var beyond = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: 50, take: 10, CancellationToken.None));
        var hugeTake = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: 0, take: 1_000_000, CancellationToken.None));
        var negative = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: -5, take: -5, CancellationToken.None));

        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Equal(3, hugeTake.Items.Count); // clamped to MaxPageSize (200), well above 3
        Assert.True(negative.Items.Count >= 1);
    }

    [Fact]
    public async Task InstructorPage_OverlapWindow_IncludesASessionThatIsStillRunningAtTheWindowStart()
    {
        var seeded = await SeedAsync();
        var sessions = await WithReaderAsync(r => r.GetSessionsForCourseAsync(seeded.CourseId, CancellationToken.None));
        var firstStart = sessions.Single(s => s.SessionId == seeded.FirstSessionId).StartsAtUtc;

        var page = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, firstStart.AddMinutes(30), firstStart.AddHours(5), includeCancelled: false, newestFirst: false, skip: 0, take: 10, CancellationToken.None));

        Assert.Equal([seeded.FirstSessionId], page.Items.Select(c => c.SessionId).ToArray());
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task SoftDeletedCourse_DropsOutOfEveryContextQuery()
    {
        var seeded = await SeedAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Courses().Remove(await db.Courses().SingleAsync(c => c.Id == seeded.CourseId)); // interceptor turns this into a soft delete
            await db.SaveChangesAsync();
        }

        var byIds = await WithReaderAsync(r => r.GetSessionContextsAsync([seeded.FirstSessionId], CancellationToken.None));
        var byCourse = await WithReaderAsync(r => r.GetSessionContextsForCoursesAsync([seeded.CourseId], null, null, includeCancelled: true, CancellationToken.None));
        var page = await WithReaderAsync(r => r.GetInstructorSessionContextsAsync(
            seeded.Instructor.UserId, null, null, includeCancelled: true, newestFirst: false, skip: 0, take: 10, CancellationToken.None));

        Assert.Empty(byIds);
        Assert.Empty(byCourse);
        Assert.Equal(0, page.TotalCount);
    }
}
