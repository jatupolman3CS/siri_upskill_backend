using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Business logic for <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/>, backing <c>InstructorPayoutAccountEndpoints</c>.
/// </summary>
public sealed class InstructorPayoutAccountService(
    IInstructorPayoutAccountRepository repository,
    ISensitiveDataProtector dataProtector,
    IClock clock)
{
    public async Task<Result<InstructorPayoutAccountResponse>> CreateForCurrentUserAsync(
        Guid userId, CreateInstructorPayoutAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await repository.GetByInstructorIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.Conflict("มีข้อมูลบัญชีธนาคารสำหรับผู้สอนนี้แล้ว"));
        }

        var accountNoEncrypted = dataProtector.Encrypt(command.AccountNo);
        var taxIdEncrypted = command.TaxId is not null ? dataProtector.Encrypt(command.TaxId) : null;

        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            userId,
            command.BankCode,
            accountNoEncrypted,
            command.AccountName,
            taxIdEncrypted,
            command.TaxPayerType);

        repository.Add(account);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(account));
    }

    public async Task<Result<InstructorPayoutAccountResponse>> GetForCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await repository.GetByInstructorIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.NotFound("ไม่พบข้อมูลบัญชีธนาคาร"));
        }

        return Result.Success(ToResponse(account));
    }

    public async Task<Result<InstructorPayoutAccountResponse>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken)
    {
        var account = await repository.GetByInstructorIdAsync(instructorId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.NotFound("ไม่พบข้อมูลบัญชีธนาคาร"));
        }

        return Result.Success(ToResponse(account));
    }

    public async Task<Result<InstructorPayoutAccountResponse>> VerifyAsync(Guid instructorId, CancellationToken cancellationToken)
    {
        var account = await repository.GetByInstructorIdAsync(instructorId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.NotFound("ไม่พบข้อมูลบัญชีธนาคาร"));
        }

        account.Verify(clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(account));
    }

    public async Task<PagedResult<InstructorPayoutAccountResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = repository.Query();
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<InstructorPayoutAccountResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private InstructorPayoutAccountResponse ToResponse(INSTRUCTOR_PAYOUT_ACCOUNT a)
    {
        var decryptedAccountNo = dataProtector.Decrypt(a.ACCOUNT_NO_ENCRYPTED);
        var maskedAccountNo = dataProtector.MaskAccountNumber(decryptedAccountNo);

        var decryptedTaxId = a.TAX_ID is not null ? dataProtector.Decrypt(a.TAX_ID) : null;
        var maskedTaxId = decryptedTaxId is not null ? dataProtector.MaskAccountNumber(decryptedTaxId) : null;

        return new InstructorPayoutAccountResponse(
            a.INSTRUCTOR_PAYOUT_ACCOUNT_ID,
            a.INSTRUCTOR_ID,
            a.BANK_CODE,
            maskedAccountNo,
            a.ACCOUNT_NAME,
            maskedTaxId,
            a.TAX_PAYER_TYPE,
            a.VERIFIED_AT_UTC);
    }
}
