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
}
