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

        public Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.TryGetValue(id, out var split) ? split : null);

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
            var list = Splits.Values.Where(s => s.PAYOUT_BATCH_ITEM_ID == batchItemId).ToList();
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

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock);

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

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock);

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

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock);

        var batch = PAYOUT_BATCH.Create("2026-08");
        var instructorId = Guid.NewGuid();
        var item = batch.AddItem(instructorId, 1000m, 3m, 30m, 970m);
        batchRepo.Add(batch);
        itemRepo.Items[item.PAYOUT_BATCH_ITEM_ID] = item;

        var split = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorId, 1428.57m, 0m, 428.57m, 1000m, 70m, "2026-08");
        typeof(REVENUE_SPLIT).GetProperty(nameof(REVENUE_SPLIT.PAYOUT_BATCH_ITEM_ID))!.SetValue(split, item.PAYOUT_BATCH_ITEM_ID);
        splitRepo.Add(split);

        var adminId = Guid.NewGuid();
        var result = await service.ExecuteBatchAsync(batch.PAYOUT_BATCH_ID, adminId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayoutBatchStatus.Executed, result.Value.Status);
        Assert.Equal(PayoutBatchItemStatus.Transferred, result.Value.Items[0].Status);
        Assert.Equal(RevenueSplitStatus.Paid, split.STATUS);
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

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock);

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

        var service = new PayoutBatchService(batchRepo, itemRepo, splitRepo, accountRepo, DataProtector, options, clock);

        var individualInstructor = Guid.NewGuid();
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

        var result = await service.GetTaxCertificateAsync(individualInstructor, item.PAYOUT_BATCH_ITEM_ID, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ภ.ง.ด.3", result.Value.TaxFormType);
        Assert.Equal(TaxPayerType.Individual, result.Value.TaxPayerType);
        Assert.Equal("1234567890123", result.Value.PayeeTaxId);
        Assert.Equal("SIRI UpSkill Co., Ltd.", result.Value.PayerName);
        Assert.Equal(10000m, result.Value.GrossIncomeAmount);
        Assert.Equal(300m, result.Value.WithholdingTaxAmount);
        Assert.Equal(9700m, result.Value.NetIncomeAmount);
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
}

