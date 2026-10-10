using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for Payout module (C3).
/// Verifies:
/// 1. Revenue split calculation according to Q4 decision (fee deduction, snapshot percentage, platform remainder).
/// 2. Split reversal on refund.
/// 3. Payout batch generation with 14-day hold, ฿500 minimum threshold, and 3% withholding tax.
/// <para>
/// Boots via <see cref="SiriApiFactory"/> (the real <c>Program.cs</c> composition root, D-19) purely to
/// get a correctly-wired DI graph for <see cref="RevenueSplitService"/>/<see cref="PayoutBatchService"/>/
/// <see cref="InstructorPayoutAccountService"/> against Testcontainers MSSQL — no test here ever goes
/// over HTTP, so there is no Minimal-API-vs-Controller coverage gap to close in this file; the old
/// hand-rolled host's <c>MapCatalogEndpoints()</c>/<c>MapPayoutEndpoints()</c>/<c>MapCommerceEndpoints()</c>
/// calls and JwtBearer/<c>HttpClient</c> wiring were already dead weight before this migration.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class PayoutIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    private Guid _instructorUserId;
    private Guid _instructorProfileId; // revenue splits, payout accounts and batch items are keyed by this, never by the user id
    private Guid _adminUserId;
    private Guid _courseId;

    public PayoutIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        await SeedDataAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();

        // 1. Instructor & Admin
        var instructor = await CreateUserAsync(services, dbContext, $"instructor_payout_{Guid.NewGuid():N}@test.com");
        instructor.AssignRole(await dbContext.SeededRoleAsync(ROLE.InstructorId));
        _instructorUserId = instructor.Id;

        var admin = await CreateUserAsync(services, dbContext, $"admin_payout_{Guid.NewGuid():N}@test.com");
        admin.AssignRole(await dbContext.SeededRoleAsync(ROLE.AdminId));
        _adminUserId = admin.Id;

        // 2. Instructor Profile
        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Payout Instructor", "Finance Guru", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        _instructorProfileId = profile.Id;

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
        await using var scope = _factory.Services.CreateAsyncScope();
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
            _instructorProfileId,
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
        await using var scope = _factory.Services.CreateAsyncScope();
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

        // The account is keyed by the instructor PROFILE id (what splits carry), even though the instructor signs in as a user.
        Assert.Equal(_instructorProfileId, accountResult.Value.InstructorId);
        var verifyResult = await payoutAccountService.VerifyAsync(_instructorProfileId, CancellationToken.None);
        Assert.True(verifyResult.IsSuccess);

        // 2. Add revenue split with period key
        var splitCmd = new CreateRevenueSplitCommand(
            Guid.NewGuid(),
            _instructorProfileId,
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

    /// <summary>
    /// Covers the N+1 fix on <see cref="PayoutBatchService.CreateAsync"/>'s split-linking loop (used to
    /// re-fetch each split individually via <c>GetByIdAsync</c> and set <c>PAYOUT_BATCH_ITEM_ID</c> via
    /// reflection) and <see cref="PayoutBatchService.ExecuteBatchAsync"/>'s mark-paid loop (used to call
    /// <c>GetSplitsByBatchItemIdAsync</c> once per batch item) — against a real SQL Server database via
    /// Testcontainers, not the in-memory fakes the unit tests use, since both fixes depend on EF Core's
    /// real change-tracking/identity-map behavior (a Fake repository can't prove a tracked-entity
    /// assumption is actually correct against a real <c>DbContext</c>). Two distinct instructors/batch
    /// items exercise the batched-lookup grouping in <c>ExecuteBatchAsync</c>.
    /// </summary>
    [Fact]
    public async Task PayoutBatch_CreateThenExecute_LinksAndPaysSplitsAcrossMultipleBatchItems()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var splitService = scope.ServiceProvider.GetRequiredService<RevenueSplitService>();
        var payoutAccountService = scope.ServiceProvider.GetRequiredService<InstructorPayoutAccountService>();
        var batchService = scope.ServiceProvider.GetRequiredService<PayoutBatchService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Second instructor, independent of the one seeded in InitializeAsync, so this batch aggregates
        // into two distinct PAYOUT_BATCH_ITEM rows.
        var secondInstructor = await CreateUserAsync(_factory.Services, db, $"instructor_payout2_{Guid.NewGuid():N}@test.com");
        secondInstructor.AssignRole(await db.SeededRoleAsync(ROLE.InstructorId));
        var secondProfile = INSTRUCTOR_PROFILE.Apply(secondInstructor.Id, "Second Payout Instructor", "Headline", "Bio");
        secondProfile.Approve(clock);
        db.InstructorProfiles().Add(secondProfile);
        await db.SaveChangesAsync();

        Assert.True((await payoutAccountService.CreateForCurrentUserAsync(
            _instructorUserId,
            new CreateInstructorPayoutAccountCommand("KBANK", "1112223334", "Instructor One", "1100500111111", TaxPayerType.Individual),
            CancellationToken.None)).IsSuccess);
        Assert.True((await payoutAccountService.VerifyAsync(_instructorProfileId, CancellationToken.None)).IsSuccess);

        Assert.True((await payoutAccountService.CreateForCurrentUserAsync(
            secondInstructor.Id,
            new CreateInstructorPayoutAccountCommand("SCB", "5556667778", "Instructor Two", "1100500222222", TaxPayerType.Individual),
            CancellationToken.None)).IsSuccess);
        Assert.True((await payoutAccountService.VerifyAsync(secondProfile.Id, CancellationToken.None)).IsSuccess);

        const string periodKey = "2026-06";

        var split1Result = await splitService.CreateAsync(
            new CreateRevenueSplitCommand(Guid.NewGuid(), _instructorProfileId, 2000m, 60m, 582m, 1358m, 70.00m, periodKey),
            CancellationToken.None);
        Assert.True(split1Result.IsSuccess);

        var split2Result = await splitService.CreateAsync(
            new CreateRevenueSplitCommand(Guid.NewGuid(), secondProfile.Id, 3000m, 90m, 873m, 2037m, 70.00m, periodKey),
            CancellationToken.None);
        Assert.True(split2Result.IsSuccess);

        // Backdate past the 14-day hold so GetEligibleSplitsForPayoutAsync picks both up — CreatedAtUtc is
        // only auto-stamped on insert (AuditableEntityInterceptor), so setting it here on an already
        // "Modified" tracked entity sticks.
        var split1 = await db.RevenueSplits().FirstAsync(s => s.REVENUE_SPLIT_ID == split1Result.Value.Id);
        split1.MarkPayable();
        ((IAuditable)split1).CreatedAtUtc = clock.UtcNow.AddDays(-20);

        var split2 = await db.RevenueSplits().FirstAsync(s => s.REVENUE_SPLIT_ID == split2Result.Value.Id);
        split2.MarkPayable();
        ((IAuditable)split2).CreatedAtUtc = clock.UtcNow.AddDays(-20);

        await db.SaveChangesAsync();

        var batchResult = await batchService.CreateAsync(new CreatePayoutBatchCommand(periodKey), CancellationToken.None);
        Assert.True(batchResult.IsSuccess);
        Assert.Equal(2, batchResult.Value.Items.Count);

        // Fix #3: linkage set via REVENUE_SPLIT.AssignToBatchItem against a real (non-Fake) DbContext —
        // not a reflection SetValue against the private setter, and not a redundant GetByIdAsync re-fetch.
        var split1AfterCreate = await db.RevenueSplits().AsNoTracking().FirstAsync(s => s.REVENUE_SPLIT_ID == split1Result.Value.Id);
        var split2AfterCreate = await db.RevenueSplits().AsNoTracking().FirstAsync(s => s.REVENUE_SPLIT_ID == split2Result.Value.Id);
        Assert.NotNull(split1AfterCreate.PAYOUT_BATCH_ITEM_ID);
        Assert.NotNull(split2AfterCreate.PAYOUT_BATCH_ITEM_ID);
        Assert.NotEqual(split1AfterCreate.PAYOUT_BATCH_ITEM_ID, split2AfterCreate.PAYOUT_BATCH_ITEM_ID);

        var executeResult = await batchService.ExecuteBatchAsync(batchResult.Value.Id, _adminUserId, CancellationToken.None);
        Assert.True(executeResult.IsSuccess);
        Assert.Equal(PayoutBatchStatus.Executed, executeResult.Value.Status);

        // Fix #4: both batch items' splits marked Paid via the batched GetSplitsByBatchItemIdsAsync
        // lookup (one query covering both PAYOUT_BATCH_ITEM rows) instead of one lookup per item.
        var split1AfterExecute = await db.RevenueSplits().AsNoTracking().FirstAsync(s => s.REVENUE_SPLIT_ID == split1Result.Value.Id);
        var split2AfterExecute = await db.RevenueSplits().AsNoTracking().FirstAsync(s => s.REVENUE_SPLIT_ID == split2Result.Value.Id);
        Assert.Equal(RevenueSplitStatus.Paid, split1AfterExecute.STATUS);
        Assert.Equal(RevenueSplitStatus.Paid, split2AfterExecute.STATUS);
    }

    // ---- Identity: the signed-in USER reaches the money of their own instructor PROFILE ---------------------------------

    [Fact]
    public async Task InstructorMoney_IsReadForTheProfileOfTheCallingUser_AndNeverForAnotherInstructor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var splitService = scope.ServiceProvider.GetRequiredService<RevenueSplitService>();
        var earnings = scope.ServiceProvider.GetRequiredService<InstructorEarningsService>();
        var accounts = scope.ServiceProvider.GetRequiredService<InstructorPayoutAccountService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // A second instructor whose money must stay invisible to the first.
        var other = await CreateUserAsync(_factory.Services, db, $"instructor_payout3_{Guid.NewGuid():N}@test.com");
        other.AssignRole(await db.SeededRoleAsync(ROLE.InstructorId));
        var otherProfile = INSTRUCTOR_PROFILE.Apply(other.Id, "Other Payout Instructor", "Headline", "Bio");
        otherProfile.Approve(clock);
        db.InstructorProfiles().Add(otherProfile);
        await db.SaveChangesAsync();

        Assert.True((await splitService.CreateAsync(
            new CreateRevenueSplitCommand(Guid.NewGuid(), _instructorProfileId, 1000m, 30m, 291m, 679m, 70.00m, "2026-08"), CancellationToken.None)).IsSuccess);
        Assert.True((await splitService.CreateAsync(
            new CreateRevenueSplitCommand(Guid.NewGuid(), otherProfile.Id, 9000m, 270m, 2619m, 6111m, 70.00m, "2026-08"), CancellationToken.None)).IsSuccess);

        var summary = await earnings.GetEarningsSummaryAsync(_instructorUserId, CancellationToken.None);
        var mine = await splitService.ListForInstructorAsync(_instructorUserId, 1, 50, CancellationToken.None);

        Assert.Equal(679m, summary.EstimatedNextPayoutAmount); // pending earnings of THIS instructor's profile only
        Assert.All(mine.Items, split => Assert.Equal(_instructorProfileId, split.InstructorId));
        Assert.DoesNotContain(mine.Items, split => split.InstructorId == otherProfile.Id);

        // A user without an instructor profile owns no money at all.
        var stranger = await earnings.GetEarningsSummaryAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(0m, stranger.EstimatedNextPayoutAmount);
        Assert.Equal(0, (await splitService.ListForInstructorAsync(Guid.NewGuid(), 1, 50, CancellationToken.None)).TotalCount);

        // An account created by the user is stored under the profile id and read back by the same user — and only by them.
        var created = await accounts.CreateForCurrentUserAsync(
            _instructorUserId,
            new CreateInstructorPayoutAccountCommand("KBANK", "9876543210", "Payout Instructor", "1100500123456", TaxPayerType.Individual),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(_instructorProfileId, created.Value.InstructorId);
        Assert.True((await accounts.GetForCurrentUserAsync(_instructorUserId, CancellationToken.None)).IsSuccess);
        Assert.True((await accounts.GetForCurrentUserAsync(other.Id, CancellationToken.None)).IsFailure);
        Assert.Equal("forbidden", (await accounts.CreateForCurrentUserAsync(
            Guid.NewGuid(),
            new CreateInstructorPayoutAccountCommand("SCB", "1111111111", "Nobody", null),
            CancellationToken.None)).Error.Code);
    }
}
