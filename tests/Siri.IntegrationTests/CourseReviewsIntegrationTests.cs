using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.GetCourseReviews;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// Regression coverage for GetCourseReviewsHandler's rating-summary computation (audit fix, 2026-09-01):
/// it used to re-average the entire unbounded review set on every paginated page request instead of
/// reading COURSE.RatingAverage (already denormalized and kept in sync by CreateCourseReviewHandler's
/// COURSE.UpdateRatingStats call), and computed the 5-bucket histogram with five in-memory .Count()
/// passes instead of one GROUP BY aggregate.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseReviewsIntegrationTests : IAsyncLifetime
{
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-reviews-integration-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _courseId;

    public CourseReviewsIntegrationTests(ContainersFixture containers)
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

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedDataAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetCourseReviews_RatingAverage_ReadsCourseDenormalizedValue_NotLiveRecompute()
    {
        // The three seeded reviews (5, 5, 3) live-average to 4.3 — deliberately different from the
        // denormalized COURSE.RatingAverage this test sets to 2.5. If the handler regresses back to
        // recomputing the average from COURSE_REVIEW rows itself, this assertion fails.
        var response = await _client.GetAsync($"/api/catalog/courses/{_courseId}/reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<CourseReviewSummaryResponse>(JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(2.5m, summary.RatingAverage);
    }

    [Fact]
    public async Task GetCourseReviews_Distribution_CountsEachStarBucketCorrectly()
    {
        var response = await _client.GetAsync($"/api/catalog/courses/{_courseId}/reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<CourseReviewSummaryResponse>(JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(2, summary.Distribution.Star5);
        Assert.Equal(0, summary.Distribution.Star4);
        Assert.Equal(1, summary.Distribution.Star3);
        Assert.Equal(0, summary.Distribution.Star2);
        Assert.Equal(0, summary.Distribution.Star1);
        Assert.Equal(3, summary.RatingCount);
    }

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext db)
    {
        var clock = services.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Review Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create("course-review-dev", "Course Review Dev", "Course Review Dev En", null, null, 0);
        db.Categories().Add(category);
        await db.SaveChangesAsync();

        var course = COURSE.Create("c1-review", "COURSE 1 Review", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var section = course.AddSection("Sec 1");
        section.AddEpisode("Ep 1", null, false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview();
        course.Publish(clock);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        _courseId = course.Id;

        // Ratings 5, 5, 3 — same review data CreateCourseReviewHandler would produce, but the denormalized
        // stat below is deliberately set to a different value so the two tests above can tell whether the
        // handler is reading COURSE.RatingAverage (2.5) or recomputing live from these rows (would be 4.3).
        var reviewer1 = Guid.NewGuid();
        var reviewer2 = Guid.NewGuid();
        var reviewer3 = Guid.NewGuid();

        var review1 = COURSE_REVIEW.Create(_courseId, reviewer1, 5, "Great course", clock.UtcNow).Value;
        var review2 = COURSE_REVIEW.Create(_courseId, reviewer2, 5, "Loved it", clock.UtcNow).Value;
        var review3 = COURSE_REVIEW.Create(_courseId, reviewer3, 3, "It was okay", clock.UtcNow).Value;
        db.CourseReviews().AddRange(review1, review2, review3);

        course.UpdateRatingStats(2.5m, 3);

        await db.SaveChangesAsync();
    }
}
