using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// X-31: proves ANNOUNCEMENT rows are actually delivered — AnnouncementDispatchJob resolves real
/// active-enrolled recipients, stages USER_NOTIFICATION/EMAIL_OUTBOX_MESSAGE rows, and marks the
/// announcement Sent with the real recipient count. Also covers the unchanged ownership check on
/// POST /api/notifications/announcements (regression — this task does not touch that authorization) and
/// LearningAccessContract.GetActiveEnrolledUserIdsAsync directly.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class AnnouncementDispatchIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "announcement-dispatch-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public AnnouncementDispatchIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Seo:PublicBaseUrl"] = "https://example.test",
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log",
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);
        builder.Services.AddLearningModule();

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapNotificationEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---- Seeding helpers (unique ids per call — this DB is shared across the whole test collection) ----

    private static async Task<USER> CreateUserAsync(IServiceProvider services, AppDbContext dbContext, string email)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, KnownPassword);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private async Task<(USER User, INSTRUCTOR_PROFILE Profile)> CreateApprovedInstructorAsync(IServiceProvider services, AppDbContext dbContext, IClock clock)
    {
        var user = await CreateUserAsync(services, dbContext, $"instructor_{Guid.NewGuid():N}@test.com");
        user.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));

        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Announcement Instructor", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync();

        return (user, profile);
    }

    private static async Task<COURSE> CreateCourseAsync(AppDbContext dbContext, Guid instructorProfileId)
    {
        var category = CATEGORY.Create($"cat-{Guid.NewGuid():N}", "หมวดทดสอบ", "Test Category", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Announcement Test COURSE", instructorProfileId, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();

        return course;
    }

    private static async Task<(USER User, ENROLLMENT Enrollment)> CreateEnrolledLearnerAsync(
        IServiceProvider services, AppDbContext dbContext, IClock clock, Guid courseId, DateTime? expiresAtUtc = null, EnrollmentStatus status = EnrollmentStatus.Active)
    {
        var learner = await CreateUserAsync(services, dbContext, $"learner_{Guid.NewGuid():N}@test.com");
        learner.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));
        await dbContext.SaveChangesAsync();

        var enrollment = ENROLLMENT.Create(learner.Id, courseId, null, EnrollmentSource.Purchase, expiresAtUtc, clock);
        if (status == EnrollmentStatus.Expired)
        {
            enrollment.Expire();
        }
        else if (status == EnrollmentStatus.Revoked)
        {
            enrollment.Revoke();
        }

        dbContext.Enrollments().Add(enrollment);
        await dbContext.SaveChangesAsync();

        return (learner, enrollment);
    }

    private static async Task<string> LoginAndGetAccessTokenAsync(IServiceProvider services, string email)
    {
        var loginHandler = services.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.60", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.AccessToken;
    }

    [Fact]
    public async Task RunAsync_SendEmailFalse_CreatesUserNotificationsOnly_MarksSent()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);
        var (learner1, _) = await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);
        var (learner2, _) = await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);

        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "No email", "Body", sendEmail: false, scheduledAtUtc: null, clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Sent, updated.DispatchStatus);
        Assert.NotNull(updated.SentAtUtc);
        Assert.Equal(2, updated.RecipientCount);

        var notifications = await db.UserNotifications().AsNoTracking()
            .Where(n => n.UserId == learner1.Id || n.UserId == learner2.Id)
            .Where(n => n.Type == "course.announcement")
            .ToListAsync();
        Assert.Equal(2, notifications.Count);

        var outboxCount = await db.EmailOutboxMessages().AsNoTracking()
            .CountAsync(m => m.ToEmail == learner1.Email || m.ToEmail == learner2.Email);
        Assert.Equal(0, outboxCount);
    }

    [Fact]
    public async Task RunAsync_SendEmailTrue_CreatesEmailOutboxAndUserNotificationPerRecipient()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);
        var (learner, _) = await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);

        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "With email", "Body content", sendEmail: true, scheduledAtUtc: null, clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Sent, updated.DispatchStatus);
        Assert.Equal(1, updated.RecipientCount);

        var notification = await db.UserNotifications().AsNoTracking().SingleOrDefaultAsync(n => n.UserId == learner.Id && n.Type == "course.announcement");
        Assert.NotNull(notification);

        var outbox = await db.EmailOutboxMessages().AsNoTracking().SingleOrDefaultAsync(m => m.ToEmail == learner.Email && m.Subject == "With email");
        Assert.NotNull(outbox);
    }

    [Fact]
    public async Task RunAsync_MixOfActiveExpiredAndRevokedEnrollments_RecipientCountCountsOnlyActive()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);

        // 3 active (one lifetime, one with a future expiry), 1 expired, 1 revoked -> RecipientCount == 3
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, expiresAtUtc: clock.UtcNow.AddDays(30));
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, status: EnrollmentStatus.Expired);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, status: EnrollmentStatus.Revoked);

        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "Mix", "Body", sendEmail: false, scheduledAtUtc: null, clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(3, updated.RecipientCount);
        Assert.Equal(AnnouncementDispatchStatus.Sent, updated.DispatchStatus);
    }

    [Fact]
    public async Task RunAsync_CourseWithNoEnrollments_RecipientCountZero_MarksSentWithoutError()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);

        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "No learners", "Body", sendEmail: true, scheduledAtUtc: null, clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(0, updated.RecipientCount);
        Assert.Equal(AnnouncementDispatchStatus.Sent, updated.DispatchStatus);
    }

    [Fact]
    public async Task RunAsync_ScheduledInFuture_IsNotDispatched_RemainsPending()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);

        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "Future", "Body", sendEmail: false, scheduledAtUtc: clock.UtcNow.AddDays(1), clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Pending, updated.DispatchStatus);
        Assert.Null(updated.SentAtUtc);
        Assert.Equal(0, updated.RecipientCount);
    }

    [Fact]
    public async Task RunAsync_ScheduledAtOrBeforeNow_IsDispatched()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);

        // Due "in the past" relative to now (already scheduled) — same as null, must dispatch this run.
        var announcement = ANNOUNCEMENT.Create(course.Id, profile.UserId, "Due", "Body", sendEmail: false, scheduledAtUtc: clock.UtcNow.AddMinutes(-1), clock);
        db.Announcements().Add(announcement);
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AnnouncementDispatchJob>();
        await job.RunAsync(CancellationToken.None);

        var updated = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == announcement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Sent, updated.DispatchStatus);
        Assert.Equal(1, updated.RecipientCount);
    }

    [Fact]
    public async Task RunAsync_OneAnnouncementFailsInBatch_OthersStillDispatch_FailedStaysPendingForRetry()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var realResolver = scope.ServiceProvider.GetRequiredService<IAnnouncementRecipientResolver>();
        var emailOutbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var goodCourse = await CreateCourseAsync(db, profile.Id);
        var failingCourse = await CreateCourseAsync(db, profile.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, goodCourse.Id);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, failingCourse.Id);

        var goodAnnouncement = ANNOUNCEMENT.Create(goodCourse.Id, profile.UserId, "Good", "Body", sendEmail: false, scheduledAtUtc: null, clock);
        var failingAnnouncement = ANNOUNCEMENT.Create(failingCourse.Id, profile.UserId, "Failing", "Body", sendEmail: false, scheduledAtUtc: null, clock);
        db.Announcements().AddRange(goodAnnouncement, failingAnnouncement);
        await db.SaveChangesAsync();

        var resolver = new ThrowingForOneCourseResolver(realResolver, failingCourse.Id);
        var job = new AnnouncementDispatchJob(db, resolver, emailOutbox, clock, NullLogger<AnnouncementDispatchJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var goodResult = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == goodAnnouncement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Sent, goodResult.DispatchStatus);
        Assert.Equal(1, goodResult.RecipientCount);

        var failedResult = await db.Announcements().AsNoTracking().SingleAsync(a => a.Id == failingAnnouncement.Id);
        Assert.Equal(AnnouncementDispatchStatus.Pending, failedResult.DispatchStatus);
        Assert.Null(failedResult.SentAtUtc);
    }

    private sealed class ThrowingForOneCourseResolver(IAnnouncementRecipientResolver inner, Guid throwForCourseId) : IAnnouncementRecipientResolver
    {
        public Task<IReadOnlyList<AnnouncementRecipient>> GetRecipientsAsync(Guid courseId, CancellationToken cancellationToken)
        {
            if (courseId == throwForCourseId)
            {
                throw new InvalidOperationException("Simulated recipient resolution failure.");
            }

            return inner.GetRecipientsAsync(courseId, cancellationToken);
        }
    }

    [Fact]
    public async Task GetActiveEnrolledUserIdsAsync_ReturnsOnlyActiveNonExpiredEnrollmentsForThatCourse()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var learningAccessContract = scope.ServiceProvider.GetRequiredService<ILearningAccessContract>();

        var (_, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);
        var otherCourse = await CreateCourseAsync(db, profile.Id);

        var (activeLearner, _) = await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id);
        var (futureLearner, _) = await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, expiresAtUtc: clock.UtcNow.AddDays(5));
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, expiresAtUtc: clock.UtcNow.AddDays(-1)); // active status but past expiry
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, status: EnrollmentStatus.Expired);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, course.Id, status: EnrollmentStatus.Revoked);
        await CreateEnrolledLearnerAsync(scope.ServiceProvider, db, clock, otherCourse.Id); // different course, must not leak in

        var activeIds = await learningAccessContract.GetActiveEnrolledUserIdsAsync(course.Id, CancellationToken.None);

        Assert.Equal(2, activeIds.Count);
        Assert.Contains(activeLearner.Id, activeIds);
        Assert.Contains(futureLearner.Id, activeIds);
    }

    [Fact]
    public async Task CreateAnnouncement_NonOwnerInstructor_ReturnsForbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (owner, profile) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);
        var course = await CreateCourseAsync(db, profile.Id);

        var (otherInstructor, _) = await CreateApprovedInstructorAsync(scope.ServiceProvider, db, clock);

        var token = await LoginAndGetAccessTokenAsync(scope.ServiceProvider, otherInstructor.Email);
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var command = new CreateAnnouncementCommand(course.Id, "Not mine", "Body content long enough", false, null);
        var response = await client.PostAsJsonAsync("/api/notifications/announcements", command, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var exists = await db.Announcements().AsNoTracking().AnyAsync(a => a.CourseId == course.Id && a.InstructorId == otherInstructor.Id);
        Assert.False(exists);
    }
}
