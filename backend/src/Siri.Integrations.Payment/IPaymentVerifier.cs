using Siri.SharedKernel;

namespace Siri.Integrations.Payment;

/// <summary>
/// Verifies PromptPay payment slips via EasySlip (see docs/PAYMENT.md). SIRI UpSkill v1 has no
/// card/installment support, so this is deliberately scoped to slip verification rather than a
/// generic payment-gateway abstraction — do not widen it into one.
/// The real EasySlip adapter ships in a later phase; this is the interface stub only.
/// </summary>
public interface IPaymentVerifier
{
    /// <param name="slipReference">The payload/reference decoded from the uploaded PromptPay slip
    /// (QR payload or provider transaction ref) that EasySlip verifies against the bank.</param>
    Task<Result<SlipVerificationResult>> VerifySlipAsync(string slipReference, CancellationToken cancellationToken);
}

public sealed record SlipVerificationResult(
    bool IsVerified,
    decimal Amount,
    string SenderAccountRef,
    string ReceiverAccountRef,
    DateTime TransactionAtUtc);
