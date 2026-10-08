using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Persistence.Conventions;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class PayoutBatchServiceTests
{
    private static readonly ISensitiveDataProtector DataProtector =
        new SensitiveDataProtector(Options.Create(new DataProtectionOptions { EncryptionKeyBase64 = Convert.ToBase64String(new byte[32]) }));

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakePayoutBatchRepository : IPayoutBatchRepository
    {
        public readonly Dictionary<Guid, PAYOUT_BATCH> Batches = [];

        public Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.TryGetValue(id, out var batch) ? batch : null);

        public Task<PAYOUT_BATCH?> GetByPeriodKeyAsync(string periodKey, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.Values.FirstOrDefault(b => b.PERIOD_KEY == periodKey));

        public IQueryable<PAYOUT_BATCH> Query() => Batches.Values.AsQueryable();

        public Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(
            Guid instructorId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstructorPayoutHistoryItem>>([]);

        public Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void Add(PAYOUT_BATCH batch) => Batches[batch.PAYOUT_BATCH_ID] = batch;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePayoutBatchItemRepository : IPayoutBatchItemRepository
    {
        public readonly Dictionary<Guid, PAYOUT_BATCH_ITEM> Items = [];

        public Task<PAYOUT_BATCH_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.TryGetValue(id, out var item) ? item : null);

        public Task<IReadOnlyList<PAYOUT_BATCH_ITEM>> GetByBatchIdAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYOUT_BATCH_ITEM>>(Items.Values.Where(i => i.BATCH_ID == batchId).ToList());

        public IQueryable<PAYOUT_BATCH_ITEM> Query() => Items.Values.AsQueryable();

        public void AddRange(IEnumerable<PAYOUT_BATCH_ITEM> items)
        {
            foreach (var item in items)
            {
                Items[item.PAYOUT_BATCH_ITEM_ID] = item;
            }
        }
    }

    private sealed class FakeRevenueSplitRepository : IRevenueSplitRepository
    {
        public readonly Dictionary<Guid, REVENUE_SPLIT> Splits = [];

        // Call counts prove PayoutBatchService no longer re-fetches each split individually
        // (CreateAsync used to call GetByIdAsync once per linked split) and batches the
        // mark-paid lookup in ExecuteBatchAsync (one GetSplitsByBatchItemIdsAsync call instead of one
        // GetSplitsByBatchItemIdAsync call per batch item).
        public int GetByIdCallCount;
        public int GetSplitsByBatchItemIdCallCount;
        public int GetSplitsByBatchItemIdsCallCount;

        public Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            GetByIdCallCount++;
            return Task.FromResult(Splits.TryGetValue(id, out var split) ? split : null);
        }

        public Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.FirstOrDefault(s => s.ORDER_ITEM_ID == orderItemId && s.STATUS != RevenueSplitStatus.Reversed));

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetByOrderItemIdsAsync(IEnumerable<Guid> orderItemIds, CancellationToken cancellationToken)
        {
            var idSet = orderItemIds.ToHashSet();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(Splits.Values.Where(s => idSet.Contains(s.ORDER_ITEM_ID)).ToList());
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetEligibleSplitsForPayoutAsync(DateTime holdCutOffUtc, CancellationToken cancellationToken)
        {
            var list = Splits.Values
                .Where(s => (s.STATUS == RevenueSplitStatus.Pending || s.STATUS == RevenueSplitStatus.Payable)
                            && s.CreatedAtUtc <= holdCutOffUtc
                            && s.PAYOUT_BATCH_ITEM_ID == null)
                .ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdAsync(Guid batchItemId, CancellationToken cancellationToken)
        {
            GetSplitsByBatchItemIdCallCount++;
            var list = Splits.Values.Where(s => s.PAYOUT_BATCH_ITEM_ID == batchItemId).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdsAsync(IReadOnlyCollection<Guid> batchItemIds, CancellationToken cancellationToken)
        {
            GetSplitsByBatchItemIdsCallCount++;
            var idSet = batchItemIds.ToHashSet();
            var list = Splits.Values.Where(s => s.PAYOUT_BATCH_ITEM_ID.HasValue && idSet.Contains(s.PAYOUT_BATCH_ITEM_ID.Value)).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public IQueryable<REVENUE_SPLIT> Query() => Splits.Values.AsQueryable();

        public Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.Where(s => s.INSTRUCTOR_ID == instructorId && s.STATUS == RevenueSplitStatus.Paid).Sum(s => s.INSTRUCTOR_AMOUNT));

        public Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.Where(s => s.INSTRUCTOR_ID == instructorId && s.STATUS == RevenueSplitStatus.Pending).Sum(s => s.INSTRUCTOR_AMOUNT));

        public void Add(REVENUE_SPLIT revenueSplit) => Splits[revenueSplit.REVENUE_SPLIT_ID] = revenueSplit;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeInstructorPayoutAccountRepository : IInstructorPayoutAccountRepository
    {
        public readonly Dictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT> Accounts = [];

        public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.TryGetValue(id, out var acc) ? acc : null);

        public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Values.FirstOrDefault(a => a.INSTRUCTOR_ID == instructorId));

        public Task<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>> GetVerifiedAccountsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
        {
            var idSet = instructorIds.ToHashSet();
            var dict = Accounts.Values
                .Where(a => idSet.Contains(a.INSTRUCTOR_ID) && a.VERIFIED_AT_UTC != null)
                .ToDictionary(a => a.INSTRUCTOR_ID);
            return Task.FromResult<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>>(dict);
        }

        public IQueryable<INSTRUCTOR_PAYOUT_ACCOUNT> Query() => Accounts.Values.AsQueryable();

        public void Add(INSTRUCTOR_PAYOUT_ACCOUNT account) => Accounts[account.INSTRUCTOR_PAYOUT_ACCOUNT_ID] = account;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void SetCreatedAt(object entity, DateTime utc)
    {
        if (entity is IAuditable auditable)
        {
            auditable.CreatedAtUtc = utc;
        }
    }

    [Fact]
    public async Task CreateAsync_AggregatesEligibleSplits_With14DayHold_Min500Threshold_And3PercentTax()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions { HoldDays = 14, MinimumPayoutAmount = 500m, WithholdingTaxPercent = 3m });
        var now = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var eligibleInstructor = Guid.NewGuid();
        var unverifiedInstructor = Guid.NewGuid();
        var belowThresholdInstructor = Guid.NewGuid();

        // 1. Setup accounts
        var verifiedAccount = INSTRUCTOR_PAYOUT_ACCOUNT.Create(eligibleInstructor, "KBANK", DataProtector.Encrypt("1234567890"), "ครูสมชาย", DataProtector.Encrypt("1234567890123"));
        verifiedAccount.Verify(clock);
        accountRepo.Add(verifiedAccount);

        var unverifiedAccount = INSTRUCTOR_PAYOUT_ACCOUNT.Create(unverifiedInstructor, "SCB", DataProtector.Encrypt("9876543210"), "ครูสมหญิง", null);
        accountRepo.Add(unverifiedAccount); // not verified!

        var belowThresholdAccount = INSTRUCTOR_PAYOUT_ACCOUNT.Create(belowThresholdInstructor, "BBL", DataProtector.Encrypt("5555555555"), "ครูสมศักดิ์", null);
        belowThresholdAccount.Verify(clock);
        accountRepo.Add(belowThresholdAccount);

        // 2. Setup splits
        // Split 1: Eligible instructor, created 20 days ago (passes 14-day hold) -> 1,000 THB
        var split1 = REVENUE_SPLIT.Create(Guid.NewGuid(), eligibleInstructor, 1428.57m, 0m, 428.57m, 1000m, 70m, "2026-08");
        SetCreatedAt(split1, now.AddDays(-20));
        splitRepo.Add(split1);

        // Split 2: Eligible instructor, created 5 days ago (fails 14-day hold) -> 2,000 THB
        var split2 = REVENUE_SPLIT.Create(Guid.NewGuid(), eligibleInstructor, 2857.14m, 0m, 857.14m, 2000m, 70m, "2026-08");
        SetCreatedAt(split2, now.AddDays(-5));
        splitRepo.Add(split2);

        // Split 3: Unverified instructor, created 20 days ago -> 5,000 THB
        var split3 = REVENUE_SPLIT.Create(Guid.NewGuid(), unverifiedInstructor, 7142.86m, 0m, 2142.86m, 5000m, 70m, "2026-08");
        SetCreatedAt(split3, now.AddDays(-20));
        splitRepo.Add(split3);

        // Split 4: Below threshold instructor, created 20 days ago -> 300 THB (< 500)
        var split4 = REVENUE_SPLIT.Create(Guid.NewGuid(), belowThresholdInstructor, 428.57m, 0m, 128.57m, 300m, 70m, "2026-08");
        SetCreatedAt(split4, now.AddDays(-20));
        splitRepo.Add(split4);

        // 3. Create batch
        var result = await service.CreateAsync(new CreatePayoutBatchCommand("2026-08"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2026-08", result.Value.PeriodKey);
        Assert.Equal(PayoutBatchStatus.Draft, result.Value.Status);

        // Only split 1 should be included!
        Assert.Single(result.Value.Items);
        var item = result.Value.Items[0];
        Assert.Equal(eligibleInstructor, item.InstructorId);
        Assert.Equal(1000m, item.Amount);
        Assert.Equal(3m, item.WithholdingTaxPercent);
        Assert.Equal(30m, item.WithholdingTaxAmount); // 3% of 1000 = 30
        Assert.Equal(970m, item.NetAmount); // 1000 - 30 = 970
        Assert.Equal(970m, result.Value.TotalAmount);

        // split1 is actually linked to the created batch item via AssignToBatchItem — same outcome the
        // old GetByIdAsync-per-split re-fetch + reflection produced, just without the redundant round
        // trips or bypassing the domain's encapsulation.
        Assert.Equal(item.Id, split1.PAYOUT_BATCH_ITEM_ID);
        Assert.Equal(RevenueSplitStatus.Payable, split1.STATUS);

        // Splits that were excluded from this batch (failed the hold window, unverified account, below
        // threshold) must not have been linked to any batch item.
        Assert.Null(split2.PAYOUT_BATCH_ITEM_ID);
        Assert.Null(split3.PAYOUT_BATCH_ITEM_ID);
        Assert.Null(split4.PAYOUT_BATCH_ITEM_ID);

        // Proves the fix: no per-split GetByIdAsync re-fetch — eligibleSplits (already tracked from the
        // batched GetEligibleSplitsForPayoutAsync query) are mutated directly.
        Assert.Equal(0, splitRepo.GetByIdCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenBatchAlreadyExistsForPeriod_ReturnsConflict()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var existingBatch = PAYOUT_BATCH.Create("2026-08");
        batchRepo.Add(existingBatch);

        var result = await service.CreateAsync(new CreatePayoutBatchCommand("2026-08"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteBatchAsync_ExecutesBatch_MarksItemsTransferred_AndSplitsPaid()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var batch = PAYOUT_BATCH.Create("2026-08");
        var instructorId = Guid.NewGuid();
        var item = batch.AddItem(instructorId, 1000m, 3m, 30m, 970m);
        batchRepo.Add(batch);
        itemRepo.Items[item.PAYOUT_BATCH_ITEM_ID] = item;

        var split = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorId, 1428.57m, 0m, 428.57m, 1000m, 70m, "2026-08");
        split.AssignToBatchItem(item.PAYOUT_BATCH_ITEM_ID);
        splitRepo.Add(split);

        var adminId = Guid.NewGuid();
        var result = await service.ExecuteBatchAsync(batch.PAYOUT_BATCH_ID, adminId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayoutBatchStatus.Executed, result.Value.Status);
        Assert.Equal(PayoutBatchItemStatus.Transferred, result.Value.Items[0].Status);
        Assert.Equal(RevenueSplitStatus.Paid, split.STATUS);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithMultipleBatchItems_MarksAllLinkedSplitsPaidInOneBatchedQuery()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var batch = PAYOUT_BATCH.Create("2026-08");
        var instructorA = Guid.NewGuid();
        var instructorB = Guid.NewGuid();
        var itemA = batch.AddItem(instructorA, 1000m, 3m, 30m, 970m);
        var itemB = batch.AddItem(instructorB, 2000m, 3m, 60m, 1940m);
        batchRepo.Add(batch);
        itemRepo.Items[itemA.PAYOUT_BATCH_ITEM_ID] = itemA;
        itemRepo.Items[itemB.PAYOUT_BATCH_ITEM_ID] = itemB;

        // Instructor A has two splits linked to the same batch item (e.g. two order items rolled into one
        // payout) — proves the grouping-by-batch-item-id in memory handles more than one split per item.
        var splitA1 = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorA, 714.29m, 0m, 214.29m, 500m, 70m, "2026-08");
        splitA1.AssignToBatchItem(itemA.PAYOUT_BATCH_ITEM_ID);
        splitRepo.Add(splitA1);

        var splitA2 = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorA, 714.29m, 0m, 214.29m, 500m, 70m, "2026-08");
        splitA2.AssignToBatchItem(itemA.PAYOUT_BATCH_ITEM_ID);
        splitRepo.Add(splitA2);

        var splitB = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorB, 2857.14m, 0m, 857.14m, 2000m, 70m, "2026-08");
        splitB.AssignToBatchItem(itemB.PAYOUT_BATCH_ITEM_ID);
        splitRepo.Add(splitB);

        var adminId = Guid.NewGuid();
        var result = await service.ExecuteBatchAsync(batch.PAYOUT_BATCH_ID, adminId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RevenueSplitStatus.Paid, splitA1.STATUS);
        Assert.Equal(RevenueSplitStatus.Paid, splitA2.STATUS);
        Assert.Equal(RevenueSplitStatus.Paid, splitB.STATUS);
        Assert.Equal(itemA.PAYOUT_BATCH_ITEM_ID, splitA1.PAYOUT_BATCH_ITEM_ID);
        Assert.Equal(itemB.PAYOUT_BATCH_ITEM_ID, splitB.PAYOUT_BATCH_ITEM_ID);

        // Proves the batch: one GetSplitsByBatchItemIdsAsync call covering both batch items, not one
        // GetSplitsByBatchItemIdAsync call per item (the old N+1 shape this fix removes).
        Assert.Equal(1, splitRepo.GetSplitsByBatchItemIdsCallCount);
        Assert.Equal(0, splitRepo.GetSplitsByBatchItemIdCallCount);
    }

    [Fact]
    public async Task ExportBatchTransferFileAsync_GeneratesCsvWithDecryptedAccountNumbers()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var instructorId = Guid.NewGuid();
        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(instructorId, "KBANK", DataProtector.Encrypt("0123456789"), "สมชาย โอนไว", null);
        account.Verify(clock);
        accountRepo.Add(account);

        var batch = PAYOUT_BATCH.Create("2026-08");
        batch.AddItem(instructorId, 1000m, 3m, 30m, 970m);
        batchRepo.Add(batch);

        var result = await service.ExportBatchTransferFileAsync(batch.PAYOUT_BATCH_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("KBANK,0123456789,\"สมชาย โอนไว\",970.00", result.Value.Content);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_Returns50TawiData_ForIndividualAndCorporate()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var options = Options.Create(new PayoutOptions
        {
            PayerCompanyName = "SIRI UpSkill Co., Ltd.",
            PayerTaxId = "0105500000000",
            PayerAddress = "Bangkok, Thailand"
        });
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        var profiles = new FakeInstructorProfileReader();

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock, NullLogger<PayoutBatchService>.Instance, profiles);

        var individualInstructor = Guid.NewGuid(); // the instructor PROFILE id the money is keyed by
        var individualUser = Guid.NewGuid(); // the authenticated USER id of that instructor
        profiles.Map(individualUser, individualInstructor);
        var individualAccount = INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            individualInstructor,
            "KBANK",
            DataProtector.Encrypt("1111111111"),
            "อาจารย์เด่น",
            DataProtector.Encrypt("1234567890123"),
            TaxPayerType.Individual);
        individualAccount.Verify(clock);
        accountRepo.Add(individualAccount);

        var batch = PAYOUT_BATCH.Create("2026-08");
        var item = batch.AddItem(individualInstructor, 10000m, 3m, 300m, 9700m);
        batch.MarkExecuted(Guid.NewGuid(), clock);
        batchRepo.Add(batch);
        itemRepo.Items[item.PAYOUT_BATCH_ITEM_ID] = item;

        var result = await service.GetTaxCertificateAsync(individualUser, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ภ.ง.ด.3", result.Value.TaxFormType);
        Assert.Equal(TaxPayerType.Individual, result.Value.TaxPayerType);
        Assert.Equal("1234567890123", result.Value.PayeeTaxId);
        Assert.Equal("SIRI UpSkill Co., Ltd.", result.Value.PayerName);
        Assert.Equal(10000m, result.Value.GrossIncomeAmount);
        Assert.Equal(300m, result.Value.WithholdingTaxAmount);
        Assert.Equal(9700m, result.Value.NetIncomeAmount);
    }

    private static PayoutOptions RealPayer() => new()
    {
        PayerCompanyName = "SIRI UpSkill Co., Ltd.",
        PayerTaxId = "0105500000000",
        PayerAddress = "123 Sukhumvit Road, Bangkok 10110",
    };

    /// <summary>
    /// An executed batch with one item for a fresh instructor. Money is keyed by the instructor PROFILE id; the returned <c>UserId</c> is the (different) authenticated
    /// user id of that instructor — the id a controller passes in — mapped to the profile by the fake profile reader.
    /// </summary>
    private static (PayoutBatchService Service, FakeInstructorPayoutAccountRepository Accounts, PAYOUT_BATCH_ITEM Item, Guid UserId) ArrangeExecutedItem(
        PayoutOptions options,
        Func<Guid, INSTRUCTOR_PAYOUT_ACCOUNT?> accountFactory)
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var profiles = new FakeInstructorProfileReader();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        var service = new PayoutBatchService(
            batchRepo, itemRepo, new FakeRevenueSplitRepository(), accountRepo, DataProtector, Options.Create(options), clock,
            NullLogger<PayoutBatchService>.Instance, profiles);

        var profileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        profiles.Map(userId, profileId);

        var account = accountFactory(profileId);
        if (account is not null)
        {
            account.Verify(clock);
            accountRepo.Add(account);
        }

        var batch = PAYOUT_BATCH.Create("2026-08");
        var item = batch.AddItem(profileId, 10000m, 3m, 300m, 9700m);
        batch.MarkExecuted(Guid.NewGuid(), clock);
        batchRepo.Add(batch);
        itemRepo.Items[item.PAYOUT_BATCH_ITEM_ID] = item;

        return (service, accountRepo, item, userId);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_CorporatePayee_UsesRealAccountNameAndForm53()
    {
        var (service, _, item, userId) = ArrangeExecutedItem(RealPayer(), id => INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            id, "KBANK", DataProtector.Encrypt("2222222222"), "บริษัท ผู้สอน จำกัด", DataProtector.Encrypt("0105500000099"), TaxPayerType.Corporate));

        var result = await service.GetTaxCertificateAsync(userId, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("บริษัท ผู้สอน จำกัด", result.Value.PayeeName);
        Assert.Equal("0105500000099", result.Value.PayeeTaxId);
        Assert.Equal(TaxPayerType.Corporate, result.Value.TaxPayerType);
        Assert.Equal("ภ.ง.ด.53", result.Value.TaxFormType);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_NoPayoutAccount_FailsInsteadOfInventingAPayeeAsIndividual()
    {
        var (service, _, item, userId) = ArrangeExecutedItem(RealPayer(), _ => null);

        var result = await service.GetTaxCertificateAsync(userId, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_AdminRequestingForInstructorWithoutAccount_AlsoFails()
    {
        var (service, _, item, _) = ArrangeExecutedItem(RealPayer(), _ => null);

        var result = await service.GetTaxCertificateAsync(Guid.NewGuid(), item.PAYOUT_BATCH_ITEM_ID, true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_PayeeWithoutTaxId_FailsInsteadOfPrintingACertificateWithNoTaxId()
    {
        var (service, _, item, userId) = ArrangeExecutedItem(RealPayer(), id => INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            id, "KBANK", DataProtector.Encrypt("3333333333"), "อาจารย์ไม่มีเลขภาษี", null, TaxPayerType.Individual));

        var result = await service.GetTaxCertificateAsync(userId, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Theory]
    [InlineData("CHANGE_ME_DEV_ONLY", "0105500000000", "123 Sukhumvit Road")]
    [InlineData("SIRI UpSkill Co., Ltd.", "0000000000000", "123 Sukhumvit Road")]
    [InlineData("SIRI UpSkill Co., Ltd.", "0105500000000", "CHANGE_ME")]
    [InlineData("SIRI UpSkill Co., Ltd.", "123", "123 Sukhumvit Road")]
    public async Task GetTaxCertificateAsync_PayerIdentityIsPlaceholder_FailsWith503Code(string name, string taxId, string address)
    {
        var options = new PayoutOptions { PayerCompanyName = name, PayerTaxId = taxId, PayerAddress = address };
        var (service, _, item, userId) = ArrangeExecutedItem(options, id => INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            id, "KBANK", DataProtector.Encrypt("4444444444"), "อาจารย์เด่น", DataProtector.Encrypt("1234567890123"), TaxPayerType.Individual));

        var result = await service.GetTaxCertificateAsync(userId, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutBatchService.PayerNotConfiguredCode, result.Error.Code);
        Assert.EndsWith(DomainErrorHttpResults.NotConfiguredCodeSuffix, result.Error.Code);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_StrangerWithUnconfiguredPayer_StillGetsForbiddenNotConfigState()
    {
        var (service, _, item, _) = ArrangeExecutedItem(new PayoutOptions(), _ => null);

        var result = await service.GetTaxCertificateAsync(Guid.NewGuid(), item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task ExportBatchTransferFileAsync_ItemWithoutVerifiedAccount_FailsInsteadOfWritingABlankTransferRow()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new PayoutBatchService(
            batchRepo, new FakePayoutBatchItemRepository(), new FakeRevenueSplitRepository(), accountRepo, DataProtector,
            Options.Create(new PayoutOptions()), clock, NullLogger<PayoutBatchService>.Instance, new FakeInstructorProfileReader());

        var verifiedInstructor = Guid.NewGuid();
        var verified = INSTRUCTOR_PAYOUT_ACCOUNT.Create(verifiedInstructor, "KBANK", DataProtector.Encrypt("0123456789"), "สมชาย โอนไว", null);
        verified.Verify(clock);
        accountRepo.Add(verified);

        var unverifiedInstructor = Guid.NewGuid();
        accountRepo.Add(INSTRUCTOR_PAYOUT_ACCOUNT.Create(unverifiedInstructor, "SCB", DataProtector.Encrypt("9999999999"), "ยังไม่ยืนยัน", null));

        var batch = PAYOUT_BATCH.Create("2026-08");
        batch.AddItem(verifiedInstructor, 1000m, 3m, 30m, 970m);
        batch.AddItem(unverifiedInstructor, 2000m, 3m, 60m, 1940m);
        batchRepo.Add(batch);

        var result = await service.ExportBatchTransferFileAsync(batch.PAYOUT_BATCH_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains(unverifiedInstructor.ToString(), result.Error.Message);
    }

    [Fact]
    public void PayoutOptions_Validation_RejectsMissingPayerFieldsAndOutOfRangePercentages()
    {
        var invalidOptions = new PayoutOptions
        {
            PayerCompanyName = "", // Required
            PayerTaxId = "",        // Required
            PayerAddress = "",      // Required
            WithholdingTaxPercent = 150m, // Invalid (> 100)
            EstimatedPaymentFeePercent = -5m, // Invalid (< 0)
        };

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(invalidOptions);
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(invalidOptions, context, validationResults, true);

        Assert.False(isValid);
        Assert.True(validationResults.Count >= 5);

        var validOptions = new PayoutOptions
        {
            PayerCompanyName = "SIRI UpSkill Co., Ltd.",
            PayerTaxId = "0105500000000",
            PayerAddress = "Bangkok, Thailand",
            WithholdingTaxPercent = 3.00m,
            EstimatedPaymentFeePercent = 3.30m,
            MinimumPayoutAmount = 500m,
            HoldDays = 14
        };

        validationResults.Clear();
        var validContext = new System.ComponentModel.DataAnnotations.ValidationContext(validOptions);
        var isValidPass = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(validOptions, validContext, validationResults, true);

        Assert.True(isValidPass);
        Assert.Empty(validationResults);
    }

    // ---- Identity: a payout account and its splits must meet under the SAME key (the instructor PROFILE id) -----------------------------

    [Fact]
    public async Task CreateAsync_InstructorWhoRegisteredTheirAccountThroughTheirUserId_IsIncludedInTheBatch()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var splitRepo = new FakeRevenueSplitRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var profiles = new FakeInstructorProfileReader();
        var now = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var batchService = new PayoutBatchService(
            batchRepo, new FakePayoutBatchItemRepository(), splitRepo, accountRepo, DataProtector,
            Options.Create(new PayoutOptions { HoldDays = 14, MinimumPayoutAmount = 500m, WithholdingTaxPercent = 3m }), clock,
            NullLogger<PayoutBatchService>.Instance, profiles);
        var accountService = new InstructorPayoutAccountService(accountRepo, DataProtector, clock, profiles);

        // The instructor signs in as a USER; revenue splits are written with the instructor PROFILE id (Course.InstructorId).
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());

        var created = await accountService.CreateForCurrentUserAsync(
            userId,
            new CreateInstructorPayoutAccountCommand("KBANK", "1234567890", "ครูสมชาย", "1234567890123", TaxPayerType.Individual),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        // The admin sees the profile id on the split/batch item and verifies the account with that id.
        Assert.True((await accountService.VerifyAsync(profileId, CancellationToken.None)).IsSuccess);

        var split = REVENUE_SPLIT.Create(Guid.NewGuid(), profileId, 1428.57m, 0m, 428.57m, 1000m, 70m, "2026-08");
        SetCreatedAt(split, now.AddDays(-20));
        splitRepo.Add(split);

        var result = await batchService.CreateAsync(new CreatePayoutBatchCommand("2026-08"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items); // before the fix the account was keyed by the user id, matched no split, and the batch stayed empty
        Assert.Equal(profileId, item.InstructorId);
        Assert.Equal(1000m, item.Amount);
        Assert.Equal(item.Id, split.PAYOUT_BATCH_ITEM_ID);
    }

    [Fact]
    public async Task GetTaxCertificateAsync_AnotherInstructorWithTheirOwnProfile_IsForbidden()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var accountRepo = new FakeInstructorPayoutAccountRepository();
        var profiles = new FakeInstructorProfileReader();
        var clock = new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        var service = new PayoutBatchService(
            batchRepo, itemRepo, new FakeRevenueSplitRepository(), accountRepo, DataProtector, Options.Create(RealPayer()), clock,
            NullLogger<PayoutBatchService>.Instance, profiles);

        var ownerUser = Guid.NewGuid();
        var ownerProfile = profiles.Map(ownerUser, Guid.NewGuid());
        var otherUser = Guid.NewGuid();
        profiles.Map(otherUser, Guid.NewGuid());

        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(ownerProfile, "KBANK", DataProtector.Encrypt("1111111111"), "เจ้าของ", DataProtector.Encrypt("1234567890123"));
        account.Verify(clock);
        accountRepo.Add(account);

        var batch = PAYOUT_BATCH.Create("2026-08");
        var item = batch.AddItem(ownerProfile, 10000m, 3m, 300m, 9700m);
        batch.MarkExecuted(Guid.NewGuid(), clock);
        batchRepo.Add(batch);
        itemRepo.Items[item.PAYOUT_BATCH_ITEM_ID] = item;

        var asOther = await service.GetTaxCertificateAsync(otherUser, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);
        var asOwner = await service.GetTaxCertificateAsync(ownerUser, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);
        var asAdmin = await service.GetTaxCertificateAsync(Guid.NewGuid(), item.PAYOUT_BATCH_ITEM_ID, true, CancellationToken.None);

        Assert.True(asOther.IsFailure);
        Assert.Equal("forbidden", asOther.Error.Code);
        Assert.True(asOwner.IsSuccess);
        Assert.True(asAdmin.IsSuccess);
    }
}
