using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real HTTP-level proof of P1-09's SEO backend (sitemap.xml + robots.txt) — same self-contained
/// <see cref="WebApplication"/> approach every other Catalog integration test file already establishes.
/// Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc comment. The redirect-table
/// part of P1-09's original scope is deliberately not built (see CLAUDE.md's P1-09 entry for why), so
/// there is nothing to test for it here.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class SeoTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "seo-tests-signing-key-0123456789";
    private const string PublicBaseUrl = "https://siriupskill.test";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public SeoTests(ContainersFixture containers)
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
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log",
            ["Seo:PublicBaseUrl"] = PublicBaseUrl,
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
        _app.UseOutputCache();

        _app.MapCatalogEndpoints();

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

    // ---- robots.txt -----------------------------------------------------------------------------

    [Fact]
    public async Task GetRobotsTxt_ReturnsPlainTextWithDisallowRulesAndSitemapReference()
    {
        using var response = await _client.GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Disallow: /admin", body);
        Assert.Contains($"Sitemap: {PublicBaseUrl}/sitemap.xml", body);
    }

    // ---- sitemap.xml ------------------------------------------------------------------------------

    [Fact]
    public async Task GetSitemapIndex_NoCoursesYet_ReturnsValidXmlWithExactlyOnePage()
    {
        using var response = await _client.GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", body);
        Assert.Contains($"{PublicBaseUrl}/sitemap-courses-1.xml", body);
        Assert.DoesNotContain("sitemap-courses-2.xml", body);
    }

    [Fact]
    public async Task GetCoursesSitemapPage_PublishedCourse_IncludesItsUrlAndLastmod()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", null, "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่", "CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Sitemap Test COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        dbContext.Courses().Add(course);

        await dbContext.SaveChangesAsync();

        using var response = await _client.GetAsync("/sitemap-courses-1.xml");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"{PublicBaseUrl}/courses/{course.Slug}", body);
        Assert.Contains("<lastmod>", body);
    }

    [Fact]
    public async Task GetCoursesSitemapPage_DraftCourse_ExcludesIt()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", null, "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่", "CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);

        var draft = COURSE.Create($"course-{Guid.NewGuid():N}", "Still A Draft", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        dbContext.Courses().Add(draft);

        await dbContext.SaveChangesAsync();

        using var response = await _client.GetAsync("/sitemap-courses-1.xml");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(draft.Slug, body);
    }
}
