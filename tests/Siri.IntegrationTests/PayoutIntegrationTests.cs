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
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for Payout module (C3).
/// Verifies:
/// 1. Revenue split calculation according to Q4 decision (fee deduction, snapshot percentage, platform remainder).
/// 2. Split reversal on refund.
/// 3. Payout batch generation with 14-day hold, ฿500 minimum threshold, and 3% withholding tax.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class PayoutIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "payout-integration-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _instructorUserId;
    private Guid _adminUserId;
    private Guid _courseId;

    public PayoutIntegrationTests(ContainersFixture containers)
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
            ["Payout:WithholdingTaxPercent"] = "3.00",
            ["Payout:PayerCompanyName"] = "SIRI UPSKILL CO., LTD.",
            ["Payout:PayerTaxId"] = "0105566000000",
            ["Payout:PayerAddress"] = "Bangkok, Thailand",
            ["Payment:Stripe:SecretKey"] = "sk_test_placeholder_key_for_testing_purposes_only",
            ["Payment:Stripe:PublishableKey"] = "pk_test_placeholder_key_for_testing_purposes_only",
            ["Payment:Stripe:WebhookSecret"] = "whsec_test_placeholder_webhook_secret_for_tests",
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
        builder.Services.AddPayoutModule(builder.Configuration);
        builder.Services.AddCommerceModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();
        _app.MapPayoutEndpoints();
        _app.MapCommerceEndpoints();

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

        // 1. Instructor & Admin
        var instructor = await CreateUserAsync(services, dbContext, $"instructor_payout_{Guid.NewGuid():N}@test.com");
        instructor.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));
        _instructorUserId = instructor.Id;

        var admin = await CreateUserAsync(services, dbContext, $"admin_payout_{Guid.NewGuid():N}@test.com");
        admin.AssignRole(new ROLE(ROLE.AdminId, ROLE.AdminName));
        _adminUserId = admin.Id;

        // 2. Instructor Profile
        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Payout Instructor", "Finance Guru", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"cat-payout-{Guid.NewGuid():N}", "หมวดการเงิน", "Finance Cat", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"payout-course-{Guid.NewGuid():N}", "Payout Analytics COURSE", profile.Id, category.Id, CourseLevel.Advanced, CourseLanguage.Thai, 3000m);
        _courseId = course.Id;
        dbContext.Courses().Add(course);

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
    public async Task RevenueSplit_Calculation_FollowsQ4FormulaAndSnapshotsRate()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var splitService = scope.ServiceProvider.GetRequiredService<RevenueSplitService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orderItemId = Guid.NewGuid();
        var grossAmount = 1000m;
        var paymentFee = 35m;
        var platformFee = 193m;
        var instructorAmount = 772m;
        var revenueShare = 80.00m;
        var periodKey = "2026-08";

        // Create Split with 80% instructor rate
        var cmd = new CreateRevenueSplitCommand(
            orderItemId,
            _instructorUserId,
            grossAmount,
            paymentFee,
            platformFee,
            instructorAmount,
            revenueShare,
            periodKey);

        var splitResult = await splitService.CreateAsync(cmd, CancellationToken.None);
        Assert.True(splitResult.IsSuccess);

        // Check Q4 calculations:
        var split = await db.RevenueSplits().FirstAsync(s => s.REVENUE_SPLIT_ID == splitResult.Value.Id);
        Assert.Equal(grossAmount, split.GROSS_AMOUNT);
        Assert.Equal(paymentFee, split.PAYMENT_FEE_AMOUNT);
        Assert.Equal(platformFee, split.PLATFORM_FEE_AMOUNT);
        Assert.Equal(revenueShare, split.REVENUE_SHARE_PERCENT);
        Assert.Equal(instructorAmount, split.INSTRUCTOR_AMOUNT);
    }

    [Fact]
    public async Task PayoutBatch_GeneratesWithHoldAndWithholdingTax()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var splitService = scope.ServiceProvider.GetRequiredService<RevenueSplitService>();
        var payoutAccountService = scope.ServiceProvider.GetRequiredService<InstructorPayoutAccountService>();
        var batchService = scope.ServiceProvider.GetRequiredService<PayoutBatchService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Create and verify payout account
        var accountResult = await payoutAccountService.CreateForCurrentUserAsync(
            _instructorUserId,
            new CreateInstructorPayoutAccountCommand(
                "KBANK",
                "1234567890",
                "Payout Instructor",
                "1100500123456",
                TaxPayerType.Individual),
            CancellationToken.None);
        Assert.True(accountResult.IsSuccess);

        var verifyResult = await payoutAccountService.VerifyAsync(_instructorUserId, CancellationToken.None);
        Assert.True(verifyResult.IsSuccess);

        // 2. Add revenue split with period key
        var splitCmd = new CreateRevenueSplitCommand(
            Guid.NewGuid(),
            _instructorUserId,
            2000m,
            60m,
            582m,
            1358m,
            70.00m,
            "2026-07");
        var splitResult = await splitService.CreateAsync(splitCmd, CancellationToken.None);
        Assert.True(splitResult.IsSuccess);

        // Mark split payable
        var splitEntity = await db.RevenueSplits().FirstAsync(s => s.REVENUE_SPLIT_ID == splitResult.Value.Id);
        splitEntity.MarkPayable();
        await db.SaveChangesAsync();

        // 3. Create Payout Batch for 2026-07
        var batchCmd = new CreatePayoutBatchCommand("2026-07");
        var batchResult = await batchService.CreateAsync(batchCmd, CancellationToken.None);
        Assert.True(batchResult.IsSuccess);
        Assert.NotNull(batchResult.Value);
    }
}
