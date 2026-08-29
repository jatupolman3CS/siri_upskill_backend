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
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for the Learning module (C1).
/// Verifies enrollment (including idempotent re-enroll), episode progress upsert,
/// quiz attempt (ensuring IS_CORRECT is not leaked to learners),
/// assignment submission and instructor grading, and certificate verification.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LearningIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "learning-integration-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _instructorUserId;
    private Guid _learnerUserId;
    private Guid _courseId;
    private Guid _sectionId;
    private Guid _episodeId;

    public LearningIntegrationTests(ContainersFixture containers)
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
        builder.Services.AddLearningModule();

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();
        _app.MapLearningEndpoints();

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

        // 1. Instructor & Learner
        var instructor = await CreateUserAsync(services, dbContext, $"instructor_learn_{Guid.NewGuid():N}@test.com");
        instructor.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));
        _instructorUserId = instructor.Id;

        var learner = await CreateUserAsync(services, dbContext, $"learner_{Guid.NewGuid():N}@test.com");
        _learnerUserId = learner.Id;

        // 2. Instructor Profile & COURSE Structure
        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Learning Instructor", "Lead Educator", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"cat-learning-{Guid.NewGuid():N}", "หมวดเรียน", "Learning Cat", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"learning-course-{Guid.NewGuid():N}", "Learning Test COURSE", profile.Id, category.Id, CourseLevel.Intermediate, CourseLanguage.Thai, 1500m);
        _courseId = course.Id;
        dbContext.Courses().Add(course);

        var section = course.AddSection("Section 1");
        _sectionId = section.Id;

        var episode = section.AddEpisode("Episode 1", "Introduction", isFreePreview: false);
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

    private async Task<string> LoginAndGetAccessTokenAsync(string email)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "TestDevice", "Test Agent"),
            "UA",
            "127.0.0.1",
            CancellationToken.None);

        if (!loginResult.IsSuccess)
        {
            throw new InvalidOperationException($"Login failed: {loginResult.Error.Code}");
        }

        return loginResult.Value.AccessToken;
    }

    [Fact]
    public async Task Enrollment_CreationAndIdempotency_Succeeds()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        // 1. Create first enrollment
        var command1 = new CreateEnrollmentCommand(
            _learnerUserId,
            _courseId,
            null,
            EnrollmentSource.Purchase,
            null);

        var result1 = await enrollmentService.CreateAsync(command1, CancellationToken.None);
        Assert.True(result1.IsSuccess);
        Assert.Equal(_courseId, result1.Value.CourseId);
        Assert.Equal(_learnerUserId, result1.Value.UserId);

        // 2. Re-enrollment (same user and course when active) should return conflict or existing
        var result2 = await enrollmentService.CreateAsync(command1, CancellationToken.None);
        Assert.False(result2.IsSuccess);
    }

    [Fact]
    public async Task EpisodeProgress_UpsertAndWatchEvents_UpdatesCorrectly()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
        var progressService = scope.ServiceProvider.GetRequiredService<EpisodeProgressService>();

        // Ensure enrolled
        var enrollResult = await enrollmentService.CreateAsync(
            new CreateEnrollmentCommand(_learnerUserId, _courseId, null, EnrollmentSource.Purchase, null),
            CancellationToken.None);
        Assert.True(enrollResult.IsSuccess);
        var enrollmentId = enrollResult.Value.Id;

        // Upsert progress
        var upsertCmd = new UpsertEpisodeProgressCommand(
            120, // 2 minutes in
            120, // watched
            false);

        var result = await progressService.UpsertProgressAsync(_learnerUserId, enrollmentId, _episodeId, upsertCmd, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(120, result.Value.LastPositionSeconds);
        Assert.False(result.Value.IsCompleted);

        // Upsert completion
        var completeCmd = new UpsertEpisodeProgressCommand(
            295,
            300,
            true);

        var completeResult = await progressService.UpsertProgressAsync(_learnerUserId, enrollmentId, _episodeId, completeCmd, CancellationToken.None);
        Assert.True(completeResult.IsSuccess);
        Assert.True(completeResult.Value.IsCompleted);
    }

    [Fact]
    public async Task Quiz_LearnerView_NeverLeaksIsCorrectBeforeSubmission()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var quizService = scope.ServiceProvider.GetRequiredService<QuizService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Instructor creates quiz
        var quizResult = await quizService.CreateAsync(
            _instructorUserId,
            new CreateQuizRequest(_episodeId, "Test Invariant Quiz", 70, 3),
            CancellationToken.None);
        Assert.True(quizResult.IsSuccess);
        var quizId = quizResult.Value.Id;

        // Add a question
        var questionResult = await quizService.AddQuestionAsync(
            _instructorUserId,
            quizId,
            new AddQuizQuestionRequest(QuizQuestionType.SingleChoice, "What is 2+2?", "Basic arithmetic", 10),
            CancellationToken.None);
        Assert.True(questionResult.IsSuccess);
        var questionId = questionResult.Value.Id;

        // Add options (one correct, one incorrect)
        await quizService.AddOptionAsync(_instructorUserId, quizId, questionId, new AddQuizOptionRequest("4", true), CancellationToken.None);
        await quizService.AddOptionAsync(_instructorUserId, quizId, questionId, new AddQuizOptionRequest("5", false), CancellationToken.None);

        // Activate quiz
        await quizService.ActivateAsync(_instructorUserId, quizId, CancellationToken.None);

        // Enroll learner
        var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
        await enrollmentService.CreateAsync(
            new CreateEnrollmentCommand(_learnerUserId, _courseId, null, EnrollmentSource.Purchase, null),
            CancellationToken.None);

        // 2. Learner requests quiz via HTTP
        var learner = await db.Users().FirstAsync(u => u.Id == _learnerUserId);
        var token = await LoginAndGetAccessTokenAsync(learner.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/learning/quizzes/by-episode/{_episodeId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        // Assert that IsCorrect and Explanation are completely absent in the learner json payload
        Assert.DoesNotContain("isCorrect", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Basic arithmetic", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What is 2+2?", rawJson);
        Assert.Contains("4", rawJson);
        Assert.Contains("5", rawJson);
    }

    [Fact]
    public async Task Assignment_SubmissionAndGradingOwnership_WorksCorrectly()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var assignmentService = scope.ServiceProvider.GetRequiredService<AssignmentService>();
        var submissionService = scope.ServiceProvider.GetRequiredService<AssignmentSubmissionService>();
        var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();

        // 1. Enroll learner
        var enrollResult = await enrollmentService.CreateAsync(
            new CreateEnrollmentCommand(_learnerUserId, _courseId, null, EnrollmentSource.Purchase, null),
            CancellationToken.None);
        Assert.True(enrollResult.IsSuccess);
        var enrollmentId = enrollResult.Value.Id;

        // 2. Create assignment
        var assignResult = await assignmentService.CreateAsync(
            _instructorUserId,
            new CreateAssignmentRequest(_episodeId, "Final Project", "Submit your git repo link", 10, 50, ".zip,.pdf"),
            CancellationToken.None);
        Assert.True(assignResult.IsSuccess);
        var assignmentId = assignResult.Value.Id;

        // 3. Learner submits assignment
        var submitResult = await submissionService.SubmitAsync(
            _learnerUserId,
            new SubmitAssignmentRequest(assignmentId, enrollmentId, "attachments/assignments/final.zip", "Completed project"),
            CancellationToken.None);
        Assert.True(submitResult.IsSuccess);
        var submissionId = submitResult.Value.Id;
        Assert.Equal(AssignmentSubmissionStatus.Submitted, submitResult.Value.Status);

        // 4. Instructor grades assignment
        var gradeResult = await submissionService.GradeAsync(
            _instructorUserId,
            submissionId,
            new GradeAssignmentSubmissionRequest(AssignmentSubmissionStatus.Graded, 85, "Great work! Passing grade."),
            CancellationToken.None);
        Assert.True(gradeResult.IsSuccess);
        Assert.Equal(AssignmentSubmissionStatus.Graded, gradeResult.Value.Status);
        Assert.Equal(85, gradeResult.Value.Score);
    }

    [Fact]
    public async Task Certificate_IssueAndVerifyPublicCode_Succeeds()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var enrollmentService = scope.ServiceProvider.GetRequiredService<EnrollmentService>();
        var certificateService = scope.ServiceProvider.GetRequiredService<CertificateService>();

        // 1. Enroll and complete course
        var enrollResult = await enrollmentService.CreateAsync(
            new CreateEnrollmentCommand(_learnerUserId, _courseId, null, EnrollmentSource.Purchase, null),
            CancellationToken.None);
        Assert.True(enrollResult.IsSuccess);
        var enrollmentId = enrollResult.Value.Id;

        var updateProgResult = await enrollmentService.UpdateOwnProgressAsync(
            _learnerUserId,
            enrollmentId,
            new UpdateEnrollmentProgressCommand(100m),
            CancellationToken.None);
        Assert.True(updateProgResult.IsSuccess);

        // 2. Issue certificate
        var issueResult = await certificateService.CreateAsync(
            new IssueCertificateCommand(enrollmentId, null),
            CancellationToken.None);
        Assert.True(issueResult.IsSuccess);
        Assert.NotNull(issueResult.Value.VerifyCode);

        // 3. Verify public certificate
        var verifyResult = await certificateService.VerifyByCodeAsync(issueResult.Value.VerifyCode, CancellationToken.None);
        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(issueResult.Value.VerifyCode, verifyResult.Value.VerifyCode);
        Assert.True(verifyResult.Value.IsValid);
    }
}
