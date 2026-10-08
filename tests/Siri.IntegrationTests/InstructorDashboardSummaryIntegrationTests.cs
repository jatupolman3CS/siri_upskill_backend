using System.Globalization;
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
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Payout.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-10 over real HTTP against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers): the instructor dashboard
/// summary reads real KPIs, the instructor's own courses with their real status, the next live session and the attendance of the finished ones — and one instructor never
/// sees another's. The data is created through the domain / the same contracts production uses (revenue splits go through <see cref="IRevenueSplitContract"/>, sessions
/// through the real endpoint), so a number in the response proves the whole read path, not a mock.
/// <para>
/// Access tokens are minted directly with the real <see cref="IAccessTokenGenerator"/> (the host's own signing key) because the "auth" limiter allows only five logins a minute.
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class InstructorDashboardSummaryIntegrationTests : IAsyncLifetime
{
    private const string SummaryUrl = "/api/analytics/instructor/dashboard/summary";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public InstructorDashboardSummaryIntegrationTests(ContainersFixture containers)
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

    // ---- Arrange helpers ------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token, Guid ProfileId);

    private async Task<Actor> CreateActorAsync(string roleName, bool withInstructorProfile = false)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        var profileId = Guid.Empty;
        if (withInstructorProfile)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Dashboard Instructor", "Headline", "Bio");
            profile.Approve(clock);
            db.InstructorProfiles().Add(profile);
            await db.SaveChangesAsync();
            profileId = profile.Id;
        }

        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
        return new Actor(user.Id, token, profileId);
    }

    private Task<Actor> CreateInstructorAsync() => CreateActorAsync(ROLE.InstructorName, withInstructorProfile: true);

    /// <summary>A course of the instructor in the given status with real denormalized counters (rating average/count, enrollment count).</summary>
    private async Task<Guid> CreateCourseAsync(
        Actor instructor,
        string title,
        bool published,
        decimal ratingAverage = 0m,
        int ratingCount = 0,
        int enrollmentCount = 0,
        DeliveryFormat format = DeliveryFormat.OnDemand)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        db.Categories().Add(category);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", title, instructor.ProfileId, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        if (format != DeliveryFormat.OnDemand)
        {
            course.SetDeliveryFormat(format);
        }

        course.AddSection("Section 1").AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        if (published)
        {
            course.Publish(clock);
        }

        course.UpdateRatingStats(ratingAverage, ratingCount);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        // The denormalized enrollment counter has no public setter (Catalog's stats updater owns it) — write it the way a reconcile would.
        db.Entry(course).Property(c => c.EnrollmentCount).CurrentValue = enrollmentCount;
        await db.SaveChangesAsync();

        return course.Id;
    }

    private async Task<Guid> EnrollNewLearnerAsync(Guid courseId, DateTime? enrolledAtUtc = null, Action<ENROLLMENT>? shape = null, Guid? existingLearnerId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var learnerId = existingLearnerId ?? (await new TestUserBuilder().WithRole(ROLE.LearnerName).BuildAsync(scope.ServiceProvider)).Id;

        var enrollment = ENROLLMENT.Create(learnerId, courseId, null, EnrollmentSource.Purchase, null, clock);
        shape?.Invoke(enrollment);
        db.Enrollments().Add(enrollment);
        if (enrolledAtUtc is { } at)
        {
            db.Entry(enrollment).Property(e => e.ENROLLED_AT_UTC).CurrentValue = at;
        }

        await db.SaveChangesAsync();
        return learnerId;
    }

    /// <summary>Records a revenue split exactly as the payment fulfilment does (through the Payout contract) and returns the order item id.</summary>
    private async Task<(Guid OrderId, Guid OrderItemId)> RecordSplitAsync(Guid instructorProfileId, decimal gross, decimal paymentFee)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var contract = scope.ServiceProvider.GetRequiredService<IRevenueSplitContract>();

        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        await contract.RecordRevenueSplitsAsync(orderId, [new OrderItemSplitInfo(orderItemId, instructorProfileId, gross, paymentFee)], CancellationToken.None);
        return (orderId, orderItemId);
    }

    private async Task<JsonDocument> GetSummaryAsync(Actor actor, string query = "")
    {
        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Get, SummaryUrl + query, actor.Token);
        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static string CurrentPeriodKey() => DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // ---- Real numbers ------------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_ReportsTheInstructorsRealKpisCoursesAndStatuses()
    {
        var instructor = await CreateInstructorAsync();
        var published = await CreateCourseAsync(instructor, "คอร์สที่เผยแพร่", published: true, ratingAverage: 5.0m, ratingCount: 10, enrollmentCount: 7);
        var draft = await CreateCourseAsync(instructor, "คอร์สฉบับร่าง", published: false, ratingAverage: 4.0m, ratingCount: 30);

        var recentA = await EnrollNewLearnerAsync(published);
        var recentB = await EnrollNewLearnerAsync(published);
        await EnrollNewLearnerAsync(draft, existingLearnerId: recentB); // one learner in two courses counts once
        await EnrollNewLearnerAsync(published, enrolledAtUtc: DateTime.UtcNow.AddDays(-30)); // enrolled long ago: a student, not a new one
        var revoked = await EnrollNewLearnerAsync(published, shape: e => e.Revoke()); // a revoked enrollment is not a student
        Assert.NotEqual(Guid.Empty, recentA);
        Assert.NotEqual(Guid.Empty, revoked);

        await RecordSplitAsync(instructor.ProfileId, gross: 1000m, paymentFee: 30m); // (1000 - 30) x 70% = 679.00

        using var json = await GetSummaryAsync(instructor);
        var kpis = json.RootElement.GetProperty("kpis");

        Assert.Equal(CurrentPeriodKey(), kpis.GetProperty("periodKey").GetString());
        Assert.Equal(679m, kpis.GetProperty("netRevenueThisMonth").GetDecimal());
        Assert.Equal(0m, kpis.GetProperty("netRevenuePreviousMonth").GetDecimal());
        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("netRevenueChangePercent").ValueKind); // nothing earned last month
        Assert.Equal(3, kpis.GetProperty("totalStudents").GetInt32());
        Assert.Equal(2, kpis.GetProperty("newStudentsLast7Days").GetInt32());
        Assert.Equal(1, kpis.GetProperty("publishedCourseCount").GetInt32());
        Assert.Equal(2, kpis.GetProperty("totalCourseCount").GetInt32());
        Assert.Equal(4.25m, kpis.GetProperty("ratingAverage").GetDecimal()); // (5.0 x 10 + 4.0 x 30) / 40
        Assert.Equal(40, kpis.GetProperty("ratingCount").GetInt32());

        var summaries = json.RootElement.GetProperty("courseSummaries").EnumerateArray().ToList();
        Assert.Equal(2, summaries.Count);
        var publishedRow = summaries.Single(c => c.GetProperty("courseId").GetGuid() == published);
        var draftRow = summaries.Single(c => c.GetProperty("courseId").GetGuid() == draft);
        Assert.Equal("Published", publishedRow.GetProperty("status").GetString());
        Assert.Equal("Draft", draftRow.GetProperty("status").GetString()); // never a hard-coded "Published"
        Assert.Equal(7, publishedRow.GetProperty("enrollmentCount").GetInt32());
        Assert.Equal("คอร์สที่เผยแพร่", publishedRow.GetProperty("title").GetString());
        Assert.Equal("OnDemand", publishedRow.GetProperty("deliveryFormat").GetString());
        Assert.Equal(990m, publishedRow.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Summary_NumbersFollowTheData_RevenueGrowsWithASaleAndShrinksWhenItIsRefunded()
    {
        var instructor = await CreateInstructorAsync();
        await CreateCourseAsync(instructor, "คอร์ส", published: true);

        using (var empty = await GetSummaryAsync(instructor))
        {
            Assert.Equal(0m, empty.RootElement.GetProperty("kpis").GetProperty("netRevenueThisMonth").GetDecimal());
        }

        var (_, firstItem) = await RecordSplitAsync(instructor.ProfileId, 1000m, 30m); // 679.00
        await RecordSplitAsync(instructor.ProfileId, 2000m, 60m); // 1358.00

        using (var afterSales = await GetSummaryAsync(instructor))
        {
            Assert.Equal(2037m, afterSales.RootElement.GetProperty("kpis").GetProperty("netRevenueThisMonth").GetDecimal());
        }

        // Refund of the first sale before any payout: its split is reversed and no longer counts.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRevenueSplitContract>()
                .ReverseRevenueSplitsForOrderAsync(Guid.NewGuid(), [firstItem], CancellationToken.None);
        }

        using var afterRefund = await GetSummaryAsync(instructor);
        Assert.Equal(1358m, afterRefund.RootElement.GetProperty("kpis").GetProperty("netRevenueThisMonth").GetDecimal());
    }

    [Fact]
    public async Task Summary_SoftDeletedCourse_IsNotCounted()
    {
        var instructor = await CreateInstructorAsync();
        await CreateCourseAsync(instructor, "อยู่", published: true);
        var removed = await CreateCourseAsync(instructor, "ถูกลบ", published: false);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses().SingleAsync(c => c.Id == removed);
            db.Courses().Remove(course); // the audit interceptor turns this into a soft delete
            await db.SaveChangesAsync();
        }

        using var json = await GetSummaryAsync(instructor);

        Assert.Equal(1, json.RootElement.GetProperty("kpis").GetProperty("totalCourseCount").GetInt32());
        Assert.Equal("อยู่", Assert.Single(json.RootElement.GetProperty("courseSummaries").EnumerateArray()).GetProperty("title").GetString());
    }

    // ---- Live --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Summary_LiveSection_ReportsTheNextSessionAndTheAttendanceOfTheFinishedOnes()
    {
        var instructor = await CreateInstructorAsync();
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);

        var upcoming = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 3);
        var finished = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);
        await MoveSessionAsync(finished, DateTime.UtcNow.AddHours(-6)); // started six hours ago, lasted two

        var learners = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            learners.Add(await EnrollNewLearnerAsync(courseId));
        }

        // Four learners were invited to the finished session, two of them entered the room; one of them is invited to the upcoming one.
        await InviteAsync(finished, learners);
        await InviteAsync(upcoming, [learners[0]]);
        await RecordJoinAsync(finished, courseId, learners[0]);
        await RecordJoinAsync(finished, courseId, learners[1]);

        using var json = await GetSummaryAsync(instructor);

        var next = json.RootElement.GetProperty("nextSession");
        Assert.Equal(upcoming, next.GetProperty("sessionId").GetGuid());
        Assert.Equal(courseId, next.GetProperty("courseId").GetGuid());
        Assert.Equal("Upcoming", next.GetProperty("displayState").GetString());
        Assert.Equal(1, next.GetProperty("expectedLearners").GetInt32());
        Assert.EndsWith("Z", next.GetProperty("startsAtUtc").GetString());

        var attendance = Assert.Single(json.RootElement.GetProperty("liveAttendance").EnumerateArray());
        Assert.Equal(finished, attendance.GetProperty("sessionId").GetGuid());
        Assert.Equal(4, attendance.GetProperty("expectedLearners").GetInt32());
        Assert.Equal(2, attendance.GetProperty("joinedLearners").GetInt32());
        Assert.Equal(50.0m, attendance.GetProperty("attendanceRatePercent").GetDecimal());
    }

    [Fact]
    public async Task Summary_InstructorWithoutAnySession_HasNoNextSessionAndEmptyAttendance()
    {
        var instructor = await CreateInstructorAsync();
        await CreateCourseAsync(instructor, "คอร์ส", published: true);

        using var json = await GetSummaryAsync(instructor);

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextSession").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("liveAttendance").EnumerateArray());
    }

    private async Task MoveSessionAsync(Guid sessionId, DateTime startsAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.CourseLiveSessions().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(s => s.StartsAtUtc).CurrentValue = startsAtUtc;
        db.Entry(session).Property(s => s.EndsAtUtc).CurrentValue = startsAtUtc.AddHours(2);
        await db.SaveChangesAsync();
    }

    private async Task InviteAsync(Guid sessionId, IEnumerable<Guid> learnerIds)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        foreach (var learnerId in learnerIds)
        {
            var invite = SESSION_INVITE.Create(sessionId, learnerId, LiveParticipantRole.Learner, clock);
            invite.MarkInvited(0, clock);
            db.SessionInvites().Add(invite);
        }

        await db.SaveChangesAsync();
    }

    private async Task RecordJoinAsync(Guid sessionId, Guid courseId, Guid learnerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.SessionJoinLogs().Add(SESSION_JOIN_LOG.Record(sessionId, courseId, learnerId, LiveParticipantRole.Learner, null, DateTime.UtcNow.AddHours(-5), null, "Dashboard test"));
        await db.SaveChangesAsync();
    }

    // ---- Ownership (IDOR) and authorization -----------------------------------------------------------------

    [Fact]
    public async Task Summary_InstructorB_NeverSeesInstructorAsCoursesLearnersSessionsOrRevenue()
    {
        var a = await CreateInstructorAsync();
        var b = await CreateInstructorAsync();

        var courseA = await CreateCourseAsync(a, "ของ A", published: true, ratingAverage: 5m, ratingCount: 99);
        await CreateCourseAsync(b, "ของ B", published: true);
        await EnrollNewLearnerAsync(courseA);
        await RecordSplitAsync(a.ProfileId, 90_000m, 0m);

        var (liveCourseA, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, a.ProfileId);
        await LiveIntegrationSupport.CreateSessionAsync(_client, a.Token, liveCourseA, daysAhead: 2);

        using var json = await GetSummaryAsync(b);
        var kpis = json.RootElement.GetProperty("kpis");

        Assert.Equal(0m, kpis.GetProperty("netRevenueThisMonth").GetDecimal());
        Assert.Equal(0, kpis.GetProperty("totalStudents").GetInt32());
        Assert.Equal(1, kpis.GetProperty("totalCourseCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, kpis.GetProperty("ratingAverage").ValueKind);
        Assert.Equal("ของ B", Assert.Single(json.RootElement.GetProperty("courseSummaries").EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextSession").ValueKind);
        Assert.DoesNotContain("ของ A", json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Summary_ACourseIdFilterThatBelongsToAnotherInstructor_ChangesNothing()
    {
        var a = await CreateInstructorAsync();
        var b = await CreateInstructorAsync();
        var courseA = await CreateCourseAsync(a, "ของ A", published: true);
        await CreateCourseAsync(b, "ของ B", published: true);

        using var json = await GetSummaryAsync(b, $"?courseId={courseA}");

        var option = Assert.Single(json.RootElement.GetProperty("courses").EnumerateArray());
        Assert.Equal("ของ B", option.GetProperty("title").GetString());
        Assert.DoesNotContain(courseA.ToString(), json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Summary_AdminWithoutAnInstructorProfile_GetsEmptyNumbersNotSomeoneElsesData()
    {
        var instructor = await CreateInstructorAsync();
        await CreateCourseAsync(instructor, "ของผู้สอน", published: true);
        await RecordSplitAsync(instructor.ProfileId, 5000m, 0m);
        var admin = await CreateActorAsync(ROLE.AdminName);

        using var json = await GetSummaryAsync(admin);
        var kpis = json.RootElement.GetProperty("kpis");

        Assert.Equal(0m, kpis.GetProperty("netRevenueThisMonth").GetDecimal());
        Assert.Equal(0, kpis.GetProperty("totalCourseCount").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("courseSummaries").EnumerateArray());
    }

    [Fact]
    public async Task Summary_WithoutALogin_Is401()
    {
        using var response = await _client.GetAsync(SummaryUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_LearnerRole_Is403()
    {
        var learner = await CreateActorAsync(ROLE.LearnerName);

        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Get, SummaryUrl, learner.Token);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- The contract readers against the real database -----------------------------------------------------

    [Fact]
    public async Task CourseStatsReader_ReturnsAtMostTwoHundredCourses_AndReportsTheProfileEvenWithNoCourses()
    {
        var instructor = await CreateInstructorAsync();
        var noCourses = await CreateInstructorAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
            db.Categories().Add(category);
            for (var i = 0; i < 205; i++)
            {
                db.Courses().Add(COURSE.Create($"bulk-{Guid.NewGuid():N}", $"Bulk {i}", instructor.ProfileId, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 100m));
            }

            await db.SaveChangesAsync();
        }

        await using var read = _factory.Services.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<IInstructorCourseStatsReader>();

        var many = await reader.GetByInstructorUserIdAsync(instructor.UserId, CancellationToken.None);
        var none = await reader.GetByInstructorUserIdAsync(noCourses.UserId, CancellationToken.None);
        var stranger = await reader.GetByInstructorUserIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(instructor.ProfileId, many.InstructorProfileId);
        Assert.Equal(200, many.Courses.Count);
        Assert.All(many.Courses, c => Assert.Equal("Draft", c.Status));
        Assert.Equal(noCourses.ProfileId, none.InstructorProfileId);
        Assert.Empty(none.Courses);
        Assert.Null(stranger.InstructorProfileId);
        Assert.Empty(stranger.Courses);
    }

    [Fact]
    public async Task ProfileReader_MapsAUserToTheirOwnProfileOnly()
    {
        var a = await CreateInstructorAsync();
        var b = await CreateInstructorAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IInstructorProfileReader>();

        Assert.Equal(a.ProfileId, await reader.GetProfileIdByUserIdAsync(a.UserId, CancellationToken.None));
        Assert.Equal(b.ProfileId, await reader.GetProfileIdByUserIdAsync(b.UserId, CancellationToken.None));
        Assert.NotEqual(a.UserId, a.ProfileId); // the two ids are different values — the whole reason the mapping exists
        Assert.Null(await reader.GetProfileIdByUserIdAsync(learner.UserId, CancellationToken.None));
        Assert.Null(await reader.GetProfileIdByUserIdAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task RevenueReader_SumsTheNetOfThePeriod_ExcludingReversedAndOtherInstructors()
    {
        var instructor = await CreateInstructorAsync();
        var other = await CreateInstructorAsync();

        await RecordSplitAsync(instructor.ProfileId, 1000m, 30m); // 679.00
        var (_, reversedItem) = await RecordSplitAsync(instructor.ProfileId, 5000m, 150m); // refunded
        await RecordSplitAsync(other.ProfileId, 9000m, 0m);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRevenueSplitContract>()
            .ReverseRevenueSplitsForOrderAsync(Guid.NewGuid(), [reversedItem], CancellationToken.None);

        var reader = scope.ServiceProvider.GetRequiredService<IInstructorRevenueReader>();

        Assert.Equal(679m, await reader.GetNetRevenueForPeriodAsync(instructor.ProfileId, CurrentPeriodKey(), CancellationToken.None));
        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(instructor.ProfileId, "2020-01", CancellationToken.None));
        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(instructor.UserId, CurrentPeriodKey(), CancellationToken.None)); // the USER id is not a key of any money row
    }
}
