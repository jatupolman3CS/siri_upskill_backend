using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Business logic for <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/>, backing <c>InstructorPayoutAccountEndpoints</c>.
/// <para>
/// An account is keyed by the instructor <b>profile</b> id (<c>CATALOG.INSTRUCTOR_PROFILES.Id</c>) — the same key <c>REVENUE_SPLITS.INSTRUCTOR_ID</c> and
/// <c>PAYOUT_BATCH_ITEMS.INSTRUCTOR_ID</c> carry, so payout-batch creation can match splits to a verified account. The "current user" methods take the
/// authenticated <b>user</b> id and resolve their own profile id first; the admin methods (<see cref="GetByInstructorIdAsync"/>, <see cref="VerifyAsync"/>)
/// take the profile id exactly as the admin sees it on batch items.
/// </para>
/// </summary>
public sealed class InstructorPayoutAccountService(
    IInstructorPayoutAccountRepository repository,
    ISensitiveDataProtector dataProtector,
    IClock clock,
    IInstructorProfileReader instructorProfiles)
{
    public async Task<Result<InstructorPayoutAccountResponse>> CreateForCurrentUserAsync(
        Guid userId, CreateInstructorPayoutAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var instructorProfileId = await instructorProfiles.GetProfileIdByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (instructorProfileId is not { } profileId)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.Forbidden("เฉพาะผู้สอนที่สมัครเป็นผู้สอนแล้วเท่านั้นที่ลงทะเบียนบัญชีรับเงินได้"));
        }

        var existing = await repository.GetByInstructorIdAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<InstructorPayoutAccountResponse>(DomainError.Conflict("มีข้อมูลบัญชีธนาคารสำหรับผู้สอนนี้แล้ว"));
        }

        var accountNoEncrypted = dataProtector.Encrypt(command.AccountNo);
        var taxIdEncrypted = command.TaxId is not null ? dataProtector.Encrypt(command.TaxId) : null;

        var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(
            profileId,
            command.BankCode,
            accountNoEncrypted,
            command.AccountName,
            taxIdEncrypted,
            command.TaxPayerType);

        repository.Add(account);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(account));
    }

    /// <summary>
    /// <c>PUT /api/payout/instructor/payout-account</c>: creates the caller's own payout account when none exists, otherwise replaces its details — the
    /// idempotent "save my bank account" the instructor screen needs (POST is create-only and answers 409 once an account exists).
    /// <para>
    /// Whose account? Always the profile that belongs to <paramref name="userId"/> (from the token via <c>IUserContext</c>), resolved through
    /// <see cref="IInstructorProfileReader"/> — the request carries no instructor or profile id, so there is no way to address another instructor's account.
    /// </para>
    /// <para>
    /// <b>Verification:</b> changing any detail makes the account unverified again (see <see cref="INSTRUCTOR_PAYOUT_ACCOUNT.UpdateDetails"/>), and an
    /// unverified account is skipped by payout-batch creation and refuses the transfer-file export until an admin re-verifies it. Re-sending the stored
    /// details exactly is a no-op that keeps the verification — so a double submit, or a form re-saved without edits, can neither pay a stale account nor
    /// silently drop a legitimate verification. The submitted values are compared to the stored ones in plaintext (the ciphertext is salted, so it cannot be
    /// compared); nothing sensitive is logged.
    /// </para>
    /// <para>
    /// <b>Double submit:</b> two concurrent first-time saves both see "no account"; the loser's insert hits the unique key on the instructor and is then
    /// applied as an update on the winner's row instead of failing (the second request ends in the same state as if it had arrived a moment later).
    /// </para>
    /// </summary>
    public async Task<Result<UpsertInstructorPayoutAccountResult>> UpsertForCurrentUserAsync(
        Guid userId, CreateInstructorPayoutAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var instructorProfileId = await instructorProfiles.GetProfileIdByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (instructorProfileId is not { } profileId)
        {
            return Result.Failure<UpsertInstructorPayoutAccountResult>(DomainError.Forbidden("เฉพาะผู้สอนที่สมัครเป็นผู้สอนแล้วเท่านั้นที่บันทึกบัญชีรับเงินได้"));
        }

        var details = NormalizedDetails.From(command);

        var existing = await repository.GetByInstructorIdAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var account = INSTRUCTOR_PAYOUT_ACCOUNT.Create(
                profileId,
                details.BankCode,
                dataProtector.Encrypt(details.AccountNo),
                details.AccountName,
                details.TaxId is not null ? dataProtector.Encrypt(details.TaxId) : null,
                details.TaxPayerType);

            repository.Add(account);
            try
            {
                await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Success(new UpsertInstructorPayoutAccountResult(ToResponse(account), Created: true));
            }
            catch (DbUpdateException)
            {
                // Lost the race on the unique instructor key? Then the winner's row exists now and this request is just an update of it. Anything else
                // (no such row) is a real failure: let the global exception handler log and 500 it.
                repository.Discard(account);
                existing = await repository.GetByInstructorIdAsync(profileId, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        var update = ApplyDetails(existing, details);
        if (update.Changed)
        {
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new UpsertInstructorPayoutAccountResult(ToResponse(existing), Created: false));
    }

    private PayoutAccountUpdate ApplyDetails(INSTRUCTOR_PAYOUT_ACCOUNT account, NormalizedDetails details)
    {
        // Compare in plaintext: the stored ciphertext carries a random nonce, so re-encrypting the same value never reproduces it.
        var accountNoChanged = !string.Equals(dataProtector.Decrypt(account.ACCOUNT_NO_ENCRYPTED).Trim(), details.AccountNo, StringComparison.Ordinal);

        var storedTaxId = account.TAX_ID is { Length: > 0 } ? dataProtector.Decrypt(account.TAX_ID).Trim() : null;
        var taxIdChanged = !string.Equals(string.IsNullOrEmpty(storedTaxId) ? null : storedTaxId, details.TaxId, StringComparison.Ordinal);

        return account.UpdateDetails(
            details.BankCode,
            accountNoChanged ? dataProtector.Encrypt(details.AccountNo) : account.ACCOUNT_NO_ENCRYPTED,
            accountNoChanged,
            details.AccountName,
            taxIdChanged && details.TaxId is not null ? dataProtector.Encrypt(details.TaxId) : null,
            taxIdChanged,
            details.TaxPayerType);
    }

    /// <summary>The submitted details with surrounding whitespace removed and a blank tax id turned into "none", so "same value" means the same thing at the
    /// compare step and at the store step.</summary>
    private readonly record struct NormalizedDetails(string BankCode, string AccountNo, string AccountName, string? TaxId, TaxPayerType TaxPayerType)
    {
        public static NormalizedDetails From(CreateInstructorPayoutAccountCommand command) => new(
            command.BankCode.Trim(),
            command.AccountNo.Trim(),
            command.AccountName.Trim(),
            string.IsNullOrWhiteSpace(command.TaxId) ? null : command.TaxId.Trim(),
            command.TaxPayerType);
    }

    public async Task<Result<InstructorPayoutAccountResponse>> GetForCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // A user without an instructor profile has no account — the same "not found" as an instructor who never registered one.
        var instructorProfileId = await instructorProfiles.GetProfileIdByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var account = instructorProfileId is { } profileId
            ? await repository.GetByInstructorIdAsync(profileId, cancellationToken).ConfigureAwait(false)
            : null;
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
