using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Business logic for <see cref="PAYOUT_BATCH"/>, backing <c>PayoutBatchEndpoints</c>.
/// Handles batch creation/aggregation, 14-day hold filtering, ฿500 threshold, 3% withholding tax calculation,
/// batch execution, bank transfer export, and 50 ทวิ tax certificate reporting per docs/DECISIONS.md Q4.
/// </summary>
public sealed class PayoutBatchService
{
    /// <summary>Ends in the shared <c>_not_configured</c> suffix, so it maps to HTTP 503.</summary>
    public const string PayerNotConfiguredCode = "payout.payer_not_configured";

    private readonly IPayoutBatchRepository _batchRepository;
    private readonly IPayoutBatchItemRepository _itemRepository;
    private readonly IRevenueSplitRepository _splitRepository;
    private readonly IInstructorPayoutAccountRepository _accountRepository;
    private readonly ISensitiveDataProtector _dataProtector;
    private readonly IOptions<PayoutOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<PayoutBatchService> _logger;
    private readonly IInstructorProfileReader _instructorProfiles;

    public PayoutBatchService(
        IPayoutBatchRepository batchRepository,
        IPayoutBatchItemRepository itemRepository,
        IRevenueSplitRepository splitRepository,
        IInstructorPayoutAccountRepository accountRepository,
        ISensitiveDataProtector dataProtector,
        IOptions<PayoutOptions> options,
        IClock clock,
        ILogger<PayoutBatchService> logger,
        IInstructorProfileReader instructorProfiles)
    {
        _batchRepository = batchRepository;
        _itemRepository = itemRepository;
        _splitRepository = splitRepository;
        _accountRepository = accountRepository;
        _dataProtector = dataProtector;
        _options = options;
        _clock = clock;
        _logger = logger;
        _instructorProfiles = instructorProfiles;
    }

    public async Task<Result<PayoutBatchResponse>> CreateAsync(CreatePayoutBatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existingBatch = await _batchRepository.GetByPeriodKeyAsync(command.PeriodKey, cancellationToken).ConfigureAwait(false);
        if (existingBatch is not null)
        {
            return Result.Failure<PayoutBatchResponse>(DomainError.Conflict($"มีรอบการจ่ายเงินสำหรับงวด {command.PeriodKey} แล้ว"));
        }

        var payoutOptions = _options.Value;
        var batch = PAYOUT_BATCH.Create(command.PeriodKey);

        // 14-day hold cut-off
        var holdCutOffUtc = _clock.UtcNow.AddDays(-payoutOptions.HoldDays);
        var eligibleSplits = await _splitRepository.GetEligibleSplitsForPayoutAsync(holdCutOffUtc, cancellationToken).ConfigureAwait(false);

        if (eligibleSplits.Count > 0)
        {
            var splitsByInstructor = eligibleSplits.GroupBy(s => s.INSTRUCTOR_ID).ToList();
            var instructorIds = splitsByInstructor.Select(g => g.Key).ToList();

            var verifiedAccounts = await _accountRepository
                .GetVerifiedAccountsAsync(instructorIds, cancellationToken)
                .ConfigureAwait(false);

            foreach (var group in splitsByInstructor)
            {
                var instructorId = group.Key;

                // Rule: Must have a verified payout account
                if (!verifiedAccounts.ContainsKey(instructorId))
                {
                    continue;
                }

                var instructorSplits = group.ToList();
                var grossAmount = instructorSplits.Sum(s => s.INSTRUCTOR_AMOUNT);

                // Rule: Minimum payout amount (฿500 default) — otherwise carries over to next batch
                if (grossAmount < payoutOptions.MinimumPayoutAmount)
                {
                    continue;
                }

                var taxPercent = payoutOptions.WithholdingTaxPercent;
                var taxAmount = Math.Round(grossAmount * taxPercent / 100m, 2, MidpointRounding.AwayFromZero);
                var netAmount = grossAmount - taxAmount;

                var batchItem = batch.AddItem(instructorId, grossAmount, taxPercent, taxAmount, netAmount);

                // Mark splits as payable and assign to this batch item
                foreach (var split in instructorSplits)
                {
                    split.MarkPayable();
                }
            }
        }

        _batchRepository.Add(batch);
        await _batchRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Link splits to created batch items. `eligibleSplits` are already the tracked entities from the
        // batched GetEligibleSplitsForPayoutAsync query above, so we mutate them directly — a previous
        // version of this code re-fetched each one individually via GetByIdAsync inside this loop, which
        // issued one redundant round trip per split even though EF's identity map already held it (a
        // tracked query never skips hitting the database; only Find/FindAsync check the local cache
        // first). It also set PAYOUT_BATCH_ITEM_ID via reflection against the private setter instead of
        // AssignToBatchItem, bypassing the domain encapsulation every other state change on this entity
        // goes through.
        if (batch.Items.Count > 0)
        {
            var itemsByInstructor = batch.Items.ToDictionary(i => i.INSTRUCTOR_ID);
            foreach (var split in eligibleSplits)
            {
                if (itemsByInstructor.TryGetValue(split.INSTRUCTOR_ID, out var item))
                {
                    split.AssignToBatchItem(item.PAYOUT_BATCH_ITEM_ID);
                }
            }
            await _splitRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(ToResponse(batch));
    }

    public async Task<Result<PayoutBatchResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(DomainError.NotFound("ไม่พบรอบการจ่ายเงิน"));
        }

        return Result.Success(ToResponse(batch));
    }

    public async Task<PagedResult<PayoutBatchResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = _batchRepository.Query();
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<PayoutBatchResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<Result<PayoutBatchResponse>> ExecuteBatchAsync(Guid batchId, Guid executedByUserId, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(batchId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return Result.Failure<PayoutBatchResponse>(DomainError.NotFound("ไม่พบรอบการจ่ายเงิน"));
        }

        if (batch.STATUS != PayoutBatchStatus.Draft)
        {
            return Result.Failure<PayoutBatchResponse>(DomainError.Conflict($"ไม่สามารถประมวลผลรอบจ่ายเงินในสถานะ {batch.STATUS} ได้"));
        }

        batch.MarkExecuted(executedByUserId, _clock);

        // Mark all associated splits as Paid — one batched query for every split across all of this
        // batch's items instead of one GetSplitsByBatchItemIdAsync round trip per item.
        var batchItemIds = batch.Items.Select(i => i.PAYOUT_BATCH_ITEM_ID).ToList();
        var allSplits = await _splitRepository.GetSplitsByBatchItemIdsAsync(batchItemIds, cancellationToken).ConfigureAwait(false);
        var splitsByBatchItemId = allSplits
            .Where(s => s.PAYOUT_BATCH_ITEM_ID.HasValue)
            .ToLookup(s => s.PAYOUT_BATCH_ITEM_ID!.Value);

        foreach (var item in batch.Items)
        {
            foreach (var split in splitsByBatchItemId[item.PAYOUT_BATCH_ITEM_ID])
            {
                split.MarkPaid(item.PAYOUT_BATCH_ITEM_ID);
            }
        }

        await _batchRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _splitRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(batch));
    }

    public async Task<Result<BatchExportResponse>> ExportBatchTransferFileAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(batchId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return Result.Failure<BatchExportResponse>(DomainError.NotFound("ไม่พบรอบการจ่ายเงิน"));
        }

        var instructorIds = batch.Items.Select(i => i.INSTRUCTOR_ID).Distinct().ToList();
        var accounts = await _accountRepository.GetVerifiedAccountsAsync(instructorIds, cancellationToken).ConfigureAwait(false);

        // A transfer file row with a blank bank/account would be an instruction to the bank built on nothing,
        // so the export refuses to run while any item's instructor has no verified payout account (e.g. the
        // account was removed or un-verified after the batch was drafted) — fix the account, then re-export.
        var instructorsWithoutAccount = instructorIds.Where(id => !accounts.ContainsKey(id)).ToList();
        if (instructorsWithoutAccount.Count > 0)
        {
            return Result.Failure<BatchExportResponse>(DomainError.Conflict(
                $"ไม่สามารถส่งออกไฟล์โอนเงินได้: ผู้สอน {instructorsWithoutAccount.Count} ราย ({string.Join(", ", instructorsWithoutAccount)}) ไม่มีบัญชีธนาคารที่ยืนยันแล้ว"));
        }

        var sb = new StringBuilder();
        sb.AppendLine("BankCode,AccountNumber,AccountName,Amount,TransferRef");

        foreach (var item in batch.Items)
        {
            var account = accounts[item.INSTRUCTOR_ID];
            var accountNo = _dataProtector.Decrypt(account.ACCOUNT_NO_ENCRYPTED);
            var transferRef = item.TRANSFER_REF ?? item.PAYOUT_BATCH_ITEM_ID.ToString("N")[..12].ToUpperInvariant();

            sb.AppendLine($"{account.BANK_CODE},{accountNo},\"{account.ACCOUNT_NAME}\",{item.NET_AMOUNT:F2},{transferRef}");
        }

        var fileName = $"payout-batch-{batch.PERIOD_KEY}-{batch.PAYOUT_BATCH_ID}.csv";
        return Result.Success(new BatchExportResponse(
            batch.PAYOUT_BATCH_ID,
            batch.PERIOD_KEY,
            fileName,
            "text/csv; charset=utf-8",
            sb.ToString()));
    }

    public async Task<Result<WithholdingTaxCertificateResponse>> GetTaxCertificateAsync(
        Guid userId,
        Guid batchItemId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var item = await _itemRepository.GetByIdAsync(batchItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<WithholdingTaxCertificateResponse>(DomainError.NotFound("ไม่พบรายการจ่ายเงินที่ระบุ"));
        }

        if (!isAdmin)
        {
            // Batch items are keyed by the instructor PROFILE id; the caller is identified by their USER id, so a non-admin may only open an item whose
            // instructor is the profile that belongs to their own account (a user without a profile owns no item).
            var callerProfileId = await _instructorProfiles.GetProfileIdByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (callerProfileId is not { } ownProfileId || item.INSTRUCTOR_ID != ownProfileId)
            {
                return Result.Failure<WithholdingTaxCertificateResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ดูหนังสือรับรองการหักภาษีนี้"));
            }
        }

        var batch = await _batchRepository.GetByIdAsync(item.BATCH_ID, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return Result.Failure<WithholdingTaxCertificateResponse>(DomainError.NotFound("ไม่พบรอบการจ่ายเงิน"));
        }

        // A 50 ทวิ is a legal tax document: both parties must be real. The payer is the configured company; the
        // payee is the instructor's own payout account. Nothing is made up — if a required value is missing
        // the request fails with a clear error (the admin sees which side to fix) instead of printing a
        // stand-in payee name or silently assuming the payee is an individual.
        var payoutOptions = _options.Value;
        var payerProblems = PayoutOptionsGuard.GetPayerInfoProblems(payoutOptions);
        if (payerProblems.Count > 0)
        {
            _logger.LogError(
                "Withholding tax certificate not generated: the payer identity is not configured ({Problems}).",
                string.Join(" ", payerProblems));
            return Result.Failure<WithholdingTaxCertificateResponse>(new DomainError(
                PayerNotConfiguredCode,
                "Withholding tax certificate payer details are not configured."));
        }

        var account = await _accountRepository.GetByInstructorIdAsync(item.INSTRUCTOR_ID, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result.Failure<WithholdingTaxCertificateResponse>(DomainError.Conflict(
                "ผู้สอนยังไม่ได้บันทึกข้อมูลบัญชีรับเงิน จึงไม่สามารถออกหนังสือรับรองการหักภาษี ณ ที่จ่ายได้"));
        }

        if (string.IsNullOrWhiteSpace(account.TAX_ID))
        {
            return Result.Failure<WithholdingTaxCertificateResponse>(DomainError.Conflict(
                "ผู้สอนยังไม่ได้ระบุเลขประจำตัวผู้เสียภาษี จึงไม่สามารถออกหนังสือรับรองการหักภาษี ณ ที่จ่ายได้"));
        }

        var decryptedTaxId = _dataProtector.Decrypt(account.TAX_ID);
        var taxPayerType = account.TAX_PAYER_TYPE;
        var taxFormType = taxPayerType == TaxPayerType.Individual ? "ภ.ง.ด.3" : "ภ.ง.ด.53";

        var cert = new WithholdingTaxCertificateResponse(
            item.PAYOUT_BATCH_ITEM_ID,
            batch.PERIOD_KEY,
            batch.EXECUTED_AT_UTC ?? batch.CreatedAtUtc,
            payoutOptions.PayerCompanyName,
            payoutOptions.PayerTaxId,
            payoutOptions.PayerAddress,
            account.ACCOUNT_NAME,
            decryptedTaxId,
            taxPayerType,
            taxFormType,
            item.AMOUNT,
            item.WITHHOLDING_TAX_PERCENT,
            item.WITHHOLDING_TAX_AMOUNT,
            item.NET_AMOUNT);

        return Result.Success(cert);
    }

    private static PayoutBatchResponse ToResponse(PAYOUT_BATCH b) =>
        new(
            b.PAYOUT_BATCH_ID,
            b.PERIOD_KEY,
            b.TOTAL_AMOUNT,
            b.STATUS,
            b.CreatedAtUtc,
            b.EXECUTED_AT_UTC,
            b.EXECUTED_BY_USER_ID,
            b.Items.Select(i => new PayoutBatchItemResponse(
                i.PAYOUT_BATCH_ITEM_ID,
                i.INSTRUCTOR_ID,
                i.AMOUNT,
                i.WITHHOLDING_TAX_PERCENT,
                i.WITHHOLDING_TAX_AMOUNT,
                i.NET_AMOUNT,
                i.STATUS,
                i.TRANSFER_REF)).ToList());
}
