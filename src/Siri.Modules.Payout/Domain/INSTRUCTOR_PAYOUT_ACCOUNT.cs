using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Domain;

/// <summary>
/// The bank account SIRI UpSkill transfers an instructor's proceeds to.
/// </summary>
public sealed class INSTRUCTOR_PAYOUT_ACCOUNT : IAuditable
{
    private INSTRUCTOR_PAYOUT_ACCOUNT()
    {
    }

    public Guid INSTRUCTOR_PAYOUT_ACCOUNT_ID { get; private set; }

    public Guid INSTRUCTOR_ID { get; private set; }

    public string BANK_CODE { get; private set; } = string.Empty;

    public string ACCOUNT_NO_ENCRYPTED { get; private set; } = string.Empty;

    public string ACCOUNT_NAME { get; private set; } = string.Empty;

    public string? TAX_ID { get; private set; }

    public TaxPayerType TAX_PAYER_TYPE { get; private set; }

    public DateTime? VERIFIED_AT_UTC { get; private set; }

    // ---- IAuditable -----------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    public static INSTRUCTOR_PAYOUT_ACCOUNT Create(
        Guid instructorId,
        string bankCode,
        string accountNoEncrypted,
        string accountName,
        string? taxId,
        TaxPayerType taxPayerType = TaxPayerType.Individual)
    {
        if (instructorId == Guid.Empty) throw new ArgumentException("Instructor ID cannot be empty.", nameof(instructorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(bankCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNoEncrypted);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        return new INSTRUCTOR_PAYOUT_ACCOUNT
        {
            INSTRUCTOR_PAYOUT_ACCOUNT_ID = UuidV7.NewId(),
            INSTRUCTOR_ID = instructorId,
            BANK_CODE = bankCode.Trim(),
            ACCOUNT_NO_ENCRYPTED = accountNoEncrypted.Trim(),
            ACCOUNT_NAME = accountName.Trim(),
            TAX_ID = taxId?.Trim(),
            TAX_PAYER_TYPE = taxPayerType,
        };
    }

    public void Verify(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        VERIFIED_AT_UTC = clock.UtcNow;
    }

    /// <summary>
    /// Replaces the account details the instructor typed in (the owner editing their own account) and keeps the verification state honest.
    /// <para>
    /// <b>Verification rule (money safety):</b> an admin verified <i>these exact details</i> — bank, account number, holder name, tax id and payer type. If
    /// <b>any</b> of them differs after this call, <see cref="VERIFIED_AT_UTC"/> is cleared, so <c>GetVerifiedAccountsAsync</c> (payout-batch creation and the
    /// transfer-file export) skips the account until an admin verifies it again. Re-submitting exactly the stored details changes nothing and keeps the
    /// verification.
    /// </para>
    /// <para>
    /// The account number and tax id are stored encrypted with a random nonce, so two encryptions of the same plaintext never match — the entity cannot tell by
    /// itself whether they changed. The caller (which owns the protector) compares plaintexts and reports the answer through <paramref name="accountNoChanged"/>
    /// and <paramref name="taxIdChanged"/>; when a flag is <c>false</c> the matching ciphertext argument is ignored and the stored ciphertext is kept as is.
    /// </para>
    /// </summary>
    /// <param name="bankCode">Plain, compared ordinally after trimming.</param>
    /// <param name="accountNoEncrypted">New account-number ciphertext; used only when <paramref name="accountNoChanged"/> is <c>true</c>.</param>
    /// <param name="accountNoChanged">Whether the plaintext account number differs from the stored one.</param>
    /// <param name="accountName">Plain, compared ordinally after trimming.</param>
    /// <param name="taxIdEncrypted">New tax-id ciphertext, or <c>null</c> to clear it; used only when <paramref name="taxIdChanged"/> is <c>true</c>.</param>
    /// <param name="taxIdChanged">Whether the plaintext tax id (or its absence) differs from the stored one.</param>
    /// <param name="taxPayerType">Individual or juristic person — decides the withholding form.</param>
    public PayoutAccountUpdate UpdateDetails(
        string bankCode,
        string accountNoEncrypted,
        bool accountNoChanged,
        string accountName,
        string? taxIdEncrypted,
        bool taxIdChanged,
        TaxPayerType taxPayerType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bankCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        if (accountNoChanged)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accountNoEncrypted);
        }

        if (!Enum.IsDefined(taxPayerType))
        {
            throw new ArgumentOutOfRangeException(nameof(taxPayerType), taxPayerType, "Unknown tax payer type.");
        }

        var newBankCode = bankCode.Trim();
        var newAccountName = accountName.Trim();

        var bankCodeChanged = !string.Equals(BANK_CODE, newBankCode, StringComparison.Ordinal);
        var accountNameChanged = !string.Equals(ACCOUNT_NAME, newAccountName, StringComparison.Ordinal);
        var taxPayerTypeChanged = TAX_PAYER_TYPE != taxPayerType;

        var anyChange = bankCodeChanged || accountNoChanged || accountNameChanged || taxIdChanged || taxPayerTypeChanged;
        if (!anyChange)
        {
            return PayoutAccountUpdate.NoChange;
        }

        if (bankCodeChanged)
        {
            BANK_CODE = newBankCode;
        }

        if (accountNoChanged)
        {
            ACCOUNT_NO_ENCRYPTED = accountNoEncrypted.Trim();
        }

        if (accountNameChanged)
        {
            ACCOUNT_NAME = newAccountName;
        }

        if (taxIdChanged)
        {
            TAX_ID = string.IsNullOrWhiteSpace(taxIdEncrypted) ? null : taxIdEncrypted.Trim();
        }

        if (taxPayerTypeChanged)
        {
            TAX_PAYER_TYPE = taxPayerType;
        }

        var verificationReset = VERIFIED_AT_UTC is not null;
        VERIFIED_AT_UTC = null;

        return new PayoutAccountUpdate(Changed: true, VerificationReset: verificationReset);
    }
}

/// <summary>What <see cref="INSTRUCTOR_PAYOUT_ACCOUNT.UpdateDetails"/> did: nothing (identical details), or changed details — and whether that took away a verification.</summary>
/// <param name="Changed">At least one of the five details differed and was replaced.</param>
/// <param name="VerificationReset">The account was verified before and is now unverified again (an admin must re-verify before it is paid).</param>
public readonly record struct PayoutAccountUpdate(bool Changed, bool VerificationReset)
{
    public static PayoutAccountUpdate NoChange { get; } = new(Changed: false, VerificationReset: false);
}
