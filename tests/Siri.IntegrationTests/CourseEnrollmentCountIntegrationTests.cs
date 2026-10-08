using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// D2 (integrator-qa) against the production composition root (real PostgreSQL + Redis from Testcontainers): <c>COURSES.ENROLLMENT_COUNT</c> used to have no writer, so the
/// public course page and the instructor dashboard's per-course table showed 0 learners while the dashboard's total-students KPI was right. Now Learning reports every
/// enrollment transition to Catalog's single updater, and the hourly recount heals drift and backfills history.
/// <para>
/// The number means "enrollments that are Active or Expired" (everyone not revoked) — the dashboard KPI's own rule. The public course detail is output-cached for five
/// minutes (tag <c>courses</c>) and is deliberately NOT evicted per enrollment (that would empty the cache on every purchase); this file therefore reads the public detail
/// once per course, right after the enrollments, and checks the stored value and the (uncached) dashboard for everything else.
/// </para>
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseEnrollmentCountIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public CourseEnrollmentCountIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Migrations are applied by the test, never by the app (database.md: no Database.Migrate() in Program.cs).
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---- Arrange helpers ----------------------------------------------------------------------------------

    private sealed record Instructor(Guid UserId, Guid ProfileId, string Token);

    private async Task<Instructor> CreateInstructorAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await new TestUserBuilder().WithRole(ROLE.InstructorName).BuildAsync(scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Enrollment Count Instructor", "Headline", "Bio");
        profile.Approve(scope.ServiceProvider.GetRequiredService<IClock>());
        db.InstructorProfiles().Add(profile);
        await db.SaveChangesAsync();

        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
        return new Instructor(user.Id, profile.Id, token);
    }

    private async Task<(Guid CourseId, string Slug)> CreatePublishedCourseAsync(Instructor instructor)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        db.Categories().Add(category);

        var slug = $"count-course-{Guid.NewGuid():N}";
        var course = COURSE.Create(slug, "คอร์สนับผู้เรียน", instructor.ProfileId, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        course.AddSection("Section 1").AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        return (course.Id, slug);
    }

    private async Task<Guid> NewLearnerAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return (await new TestUserBuilder().WithRole(ROLE.LearnerName).BuildAsync(scope.ServiceProvider)).Id;
    }

    /// <summary>Grants access exactly as a payment fulfilment does: through the Learning contract, in its own scope (own context and connection).</summary>
    private async Task EnrollAsync(Guid learnerId, Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILearningAccessContract>()
            .EnrollUserAsync(learnerId, courseId, Guid.NewGuid(), "Purchase", null, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    private async Task RevokeAsync(Guid learnerId, Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.Enrollments().AsNoTracking().SingleAsync(e => e.USER_ID == learnerId && e.COURSE_ID == courseId);

        var result = await scope.ServiceProvider.GetRequiredService<EnrollmentService>().RevokeAsync(enrollment.ENROLLMENT_ID, CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    private async Task<int> StoredCountAsync(Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Courses().AsNoTracking().Where(c => c.Id == courseId).Select(c => c.EnrollmentCount).SingleAsync();
    }

    /// <summary>The source of truth, read straight from the enrollments with the documented rule (Active or Expired).</summary>
    private async Task<int> TruthAsync(Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Enrollments().AsNoTracking()
            .CountAsync(e => e.COURSE_ID == courseId && (e.STATUS == EnrollmentStatus.Active || e.STATUS == EnrollmentStatus.Expired));
    }

    private async Task SetStoredCountAsync(Guid courseId, int value)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses().SingleAsync(c => c.Id == courseId);
        db.Entry(course).Property(c => c.EnrollmentCount).CurrentValue = value; // simulates drift / history the updater never saw
        await db.SaveChangesAsync();
    }

    private async Task<JsonDocument> GetAsync(string url, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    // ---- Enroll / revoke --------------------------------------------------------------------------------------

    [Fact]
    public async Task Enrolling_RaisesTheCount_AndThePublicDetailAndTheDashboardShowTheSameNumber()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, slug) = await CreatePublishedCourseAsync(instructor);
        Assert.Equal(0, await StoredCountAsync(courseId));

        for (var i = 0; i < 3; i++)
        {
            await EnrollAsync(await NewLearnerAsync(), courseId);
        }

        Assert.Equal(3, await StoredCountAsync(courseId));

        // The public course page (anonymous) ...
        using var detail = await GetAsync($"/api/catalog/courses/{slug}");
        Assert.Equal(3, detail.RootElement.GetProperty("enrollmentCount").GetInt32());

        // ... and the instructor dashboard read the very same column, so they cannot disagree (and the dashboard's own total-students KPI agrees).
        using var dashboard = await GetAsync("/api/analytics/instructor/dashboard/summary", instructor.Token);
        var row = dashboard.RootElement.GetProperty("courseSummaries").EnumerateArray().Single(c => c.GetProperty("courseId").GetGuid() == courseId);
        Assert.Equal(3, row.GetProperty("enrollmentCount").GetInt32());
        Assert.Equal(3, dashboard.RootElement.GetProperty("kpis").GetProperty("totalStudents").GetInt32());
    }

    [Fact]
    public async Task Revoking_LowersTheCountOnce_AndABuyerWhoComesBackCountsAgain()
    {
        var (courseId, learners) = await CourseWithLearnersAsync(learnerCount: 3);

        await RevokeAsync(learners[0], courseId);
        Assert.Equal(2, await StoredCountAsync(courseId));

        await RevokeAsync(learners[0], courseId); // revoking twice must not subtract twice
        Assert.Equal(2, await StoredCountAsync(courseId));

        await EnrollAsync(learners[0], courseId); // buys again: the revoked row is reactivated and counts once more
        Assert.Equal(3, await StoredCountAsync(courseId));
        Assert.Equal(3, await TruthAsync(courseId));

        await EnrollAsync(learners[0], courseId); // a retried delivery of the same purchase: an idempotent no-op
        Assert.Equal(3, await StoredCountAsync(courseId));
    }

    [Fact]
    public async Task AnExpiredEnrollment_StillCounts_AndReactivatingItDoesNotCountAgain()
    {
        var (courseId, learners) = await CourseWithLearnersAsync(learnerCount: 2);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = await db.Enrollments().SingleAsync(e => e.USER_ID == learners[0] && e.COURSE_ID == courseId);
            enrollment.Expire(); // what the (future) expiry job does
            await db.SaveChangesAsync();
        }

        await RecountDriftedAsync();
        Assert.Equal(2, await StoredCountAsync(courseId)); // Expired is not Revoked: they did learn here

        await EnrollAsync(learners[0], courseId); // repurchase: Expired -> Active
        Assert.Equal(2, await StoredCountAsync(courseId));
    }

    [Fact]
    public async Task ManyConcurrentEnrollments_AreAllCounted_NoLostUpdate()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await CreatePublishedCourseAsync(instructor);
        var learners = new List<Guid>();
        for (var i = 0; i < 16; i++)
        {
            learners.Add(await NewLearnerAsync());
        }

        await Task.WhenAll(learners.Select(learner => EnrollAsync(learner, courseId)));

        Assert.Equal(16, await TruthAsync(courseId));
        Assert.Equal(16, await StoredCountAsync(courseId)); // 16 atomic increments from 16 connections
    }

    // ---- Recount: history, drift, concurrency ---------------------------------------------------------------------

    [Fact]
    public async Task Recount_BackfillsHistoryThatPredatesTheCounter_AndIgnoresRevokedEnrollments()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await CreatePublishedCourseAsync(instructor);

        // Enrollments written straight to the table (as production had before the counter had a writer): the stored count is still 0.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            for (var i = 0; i < 4; i++)
            {
                db.Enrollments().Add(ENROLLMENT.Create(await NewLearnerAsync(), courseId, null, EnrollmentSource.Purchase, null, clock));
            }

            var revoked = ENROLLMENT.Create(await NewLearnerAsync(), courseId, null, EnrollmentSource.Purchase, null, clock);
            revoked.Revoke();
            db.Enrollments().Add(revoked);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await StoredCountAsync(courseId));

        var corrected = await RecountDriftedAsync();

        Assert.True(corrected >= 1);
        Assert.Equal(4, await StoredCountAsync(courseId)); // the revoked one is not a learner
        Assert.Equal(await TruthAsync(courseId), await StoredCountAsync(courseId));
    }

    [Fact]
    public async Task Recount_RepairsADriftedCounterInBothDirections_AndLeavesACorrectOneAlone()
    {
        var (tooHigh, _) = await CourseWithLearnersAsync(learnerCount: 2);
        var (tooLow, _) = await CourseWithLearnersAsync(learnerCount: 5);
        var (right, _) = await CourseWithLearnersAsync(learnerCount: 3);
        await SetStoredCountAsync(tooHigh, 40);
        await SetStoredCountAsync(tooLow, 0);

        await RecountDriftedAsync(pageSize: 2); // tiny pages: the walk must cross page boundaries

        Assert.Equal(2, await StoredCountAsync(tooHigh));
        Assert.Equal(5, await StoredCountAsync(tooLow));
        Assert.Equal(3, await StoredCountAsync(right));

        Assert.Equal(0, await RecountDriftedAsync(pageSize: 2)); // idempotent: a second run finds nothing to fix
    }

    [Fact]
    public async Task TheHourlyJob_DoesTheSameRepair()
    {
        var (courseId, _) = await CourseWithLearnersAsync(learnerCount: 2);
        await SetStoredCountAsync(courseId, 77);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CourseEnrollmentRecountJob>().RunAsync(CancellationToken.None);
        }

        Assert.Equal(2, await StoredCountAsync(courseId));
    }

    [Fact]
    public async Task Adjust_NeverTakesTheCounterBelowZero()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await CreatePublishedCourseAsync(instructor);

        await using var scope = _factory.Services.CreateAsyncScope();
        var updater = scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCountUpdater>();
        await updater.AdjustAsync(courseId, -1, CancellationToken.None);
        await updater.AdjustAsync(courseId, 0, CancellationToken.None);
        await updater.AdjustAsync(Guid.NewGuid(), 1, CancellationToken.None); // unknown course: a no-op, not an error

        Assert.Equal(0, await StoredCountAsync(courseId));
    }

    [Fact]
    public async Task Recount_OfASingleCourse_ReturnsAndStoresTheTruth_AndZeroForAnUnknownCourse()
    {
        var (courseId, _) = await CourseWithLearnersAsync(learnerCount: 3);
        await SetStoredCountAsync(courseId, 9);

        await using var scope = _factory.Services.CreateAsyncScope();
        var updater = scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCountUpdater>();

        Assert.Equal(3, await updater.RecountAsync(courseId, CancellationToken.None));
        Assert.Equal(3, await StoredCountAsync(courseId));
        Assert.Equal(0, await updater.RecountAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RecountsRunningAlongsideEnrollments_ConvergeOnTheTruth()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await CreatePublishedCourseAsync(instructor);
        var learners = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            learners.Add(await NewLearnerAsync());
        }

        var recounts = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCountUpdater>().RecountAsync(courseId, CancellationToken.None);
        });

        await Task.WhenAll(learners.Select(learner => EnrollAsync(learner, courseId)).Concat(recounts));

        // A recount that overlapped the (autocommit) gap between an enrollment and its +1 may have seen it twice; the next recount settles it — which is exactly what
        // the hourly job is for. Inside a payment transaction the row lock makes even that gap impossible.
        await RecountDriftedAsync();

        Assert.Equal(12, await TruthAsync(courseId));
        Assert.Equal(12, await StoredCountAsync(courseId));
    }

    // ---- helpers shared by the scenarios above ----------------------------------------------------------------------

    private async Task<(Guid CourseId, List<Guid> Learners)> CourseWithLearnersAsync(int learnerCount)
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await CreatePublishedCourseAsync(instructor);

        var learners = new List<Guid>();
        for (var i = 0; i < learnerCount; i++)
        {
            var learner = await NewLearnerAsync();
            learners.Add(learner);
            await EnrollAsync(learner, courseId);
        }

        Assert.Equal(learnerCount, await StoredCountAsync(courseId));
        return (courseId, learners);
    }

    private async Task<int> RecountDriftedAsync(int pageSize = 200)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICourseEnrollmentCountUpdater>().RecountDriftedAsync(pageSize, CancellationToken.None);
    }
}
