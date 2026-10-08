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
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Cms;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Modules.Cms.Infrastructure;
using Siri.Modules.Community;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Application.Response;
using Siri.Modules.Community.Domain;
using Siri.Modules.Community.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for CMS & Community modules (C4).
/// Verifies:
/// 1. CMS HTML sanitization (stripping XSS attacks), Banner/Post/Menu CRUD.
/// 2. Community Q&A discussions, replies, upvoting, and moderation reporting.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CmsAndCommunityIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "cms-community-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _learnerUserId;
    private Guid _courseId;
    private Guid _episodeId;

    public CmsAndCommunityIntegrationTests(ContainersFixture containers)
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
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);
        builder.Services.AddCmsModule();
        builder.Services.AddCommunityModule();

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();
        _app.MapCmsEndpoints();
        _app.MapCommunityEndpoints();

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

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();

        // 1. Admin & Learner
        var admin = await CreateUserAsync(services, dbContext, $"admin_cms_{Guid.NewGuid():N}@test.com");
        admin.AssignRole(await dbContext.SeededRoleAsync(ROLE.AdminId));
        _adminUserId = admin.Id;

        var learner = await CreateUserAsync(services, dbContext, $"learner_cms_{Guid.NewGuid():N}@test.com");
        _learnerUserId = learner.Id;

        // 2. Dummy Catalog Episode for Q&A testing
        var profile = INSTRUCTOR_PROFILE.Apply(admin.Id, "Admin Instructor", "Expert", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var cat = CATEGORY.Create($"cms-cat-{Guid.NewGuid():N}", "หมวด CMS", "CMS Cat", null, null, 0);
        dbContext.Categories().Add(cat);

        var course = COURSE.Create($"cms-course-{Guid.NewGuid():N}", "CMS Test COURSE", profile.Id, cat.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        _courseId = course.Id;
        dbContext.Courses().Add(course);

        var section = course.AddSection("Section 1");
        var episode = section.AddEpisode("Episode 1", "Desc", false);
        _episodeId = episode.Id;

        await dbContext.SaveChangesAsync();
    }

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

    [Fact]
    public async Task CmsPost_HtmlSanitization_StripsMaliciousScriptTags()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var postService = scope.ServiceProvider.GetRequiredService<PostService>();

        var rawMaliciousHtml = "<p>Hello world</p><script>alert('XSS Attack!')</script><img src='x' onerror='alert(1)'>";
        var slug = $"sec-post-{Guid.NewGuid():N}";
        var cmd = new CreatePostCommand(
            slug,
            "Security Post",
            "Brief excerpt",
            rawMaliciousHtml,
            null,
            "SEO Title",
            "SEO Desc");

        var createResult = await postService.CreateAsync(_adminUserId, cmd, CancellationToken.None);
        Assert.True(createResult.IsSuccess);

        // Publish the post
        await postService.ChangeStatusAsync(createResult.Value.Id, new ChangePostStatusCommand(PostStatus.Published), CancellationToken.None);

        // Fetch post
        var postResult = await postService.GetPublishedBySlugAsync(slug, CancellationToken.None);
        Assert.True(postResult.IsSuccess);

        // Invariant: Script tags and dangerous attributes must be sanitized
        Assert.DoesNotContain("<script>", postResult.Value.ContentHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", postResult.Value.ContentHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Hello world</p>", postResult.Value.ContentHtml);
    }

    [Fact]
    public async Task CommunityDiscussion_CreateReplyAndUpvote_WorksCorrectly()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var discussionService = scope.ServiceProvider.GetRequiredService<DiscussionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Learner asks a question
        var questionCmd = new CreateDiscussionCommand(
            _courseId,
            _episodeId,
            null,
            "How do I set up Testcontainers?");

        var questionResult = await discussionService.CreateAsync(_learnerUserId, questionCmd, CancellationToken.None);
        Assert.True(questionResult.IsSuccess);
        var questionId = questionResult.Value.Id;

        // 2. Instructor / Admin answers question
        var replyCmd = new CreateDiscussionCommand(
            _courseId,
            _episodeId,
            questionId,
            "On local machine without Docker, Testcontainers will skip gracefully in unit mode.");

        var replyResult = await discussionService.CreateAsync(_adminUserId, replyCmd, CancellationToken.None);
        Assert.True(replyResult.IsSuccess);
        Assert.Equal(questionId, replyResult.Value.ParentId);

        // 3. Learner upvotes the answer
        var upvoteResult = await discussionService.UpvoteAsync(_learnerUserId, replyResult.Value.Id, CancellationToken.None);
        Assert.True(upvoteResult.IsSuccess);

        // 4. Verify upvote count in DB
        var reply = await db.Discussions().FirstAsync(d => d.DISCUSSION_ID == replyResult.Value.Id);
        Assert.Equal(1, reply.UPVOTE_COUNT);
    }

    [Fact]
    public async Task CommunityReport_SubmitAndAdminResolve_WorksCorrectly()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var discussionService = scope.ServiceProvider.GetRequiredService<DiscussionService>();
        var reportService = scope.ServiceProvider.GetRequiredService<ReportService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Create a spam discussion
        var spamCmd = new CreateDiscussionCommand(_courseId, _episodeId, null, "Buy cheap pills at spam.test");
        var spamResult = await discussionService.CreateAsync(_learnerUserId, spamCmd, CancellationToken.None);
        Assert.True(spamResult.IsSuccess);

        // 2. Learner reports the spam
        var reportCmd = new CreateReportCommand(
            spamResult.Value.Id,
            "Spam content");

        var reportResult = await reportService.CreateAsync(_learnerUserId, reportCmd, CancellationToken.None);
        Assert.True(reportResult.IsSuccess);
        var reportId = reportResult.Value.Id;

        // 3. Admin resolves report
        var resolveResult = await reportService.ResolveAsync(reportId, CancellationToken.None);
        Assert.True(resolveResult.IsSuccess);

        var report = await db.Reports().FirstAsync(r => r.REPORT_ID == reportId);
        Assert.Equal(ReportStatus.Resolved, report.STATUS);
    }
}
