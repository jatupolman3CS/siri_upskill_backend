using Siri.SharedKernel;

namespace Siri.Integrations.Payment;

/// <summary>
/// SUPERSEDED STUB (Q2/D-14 revised 2026-08-18): the payment decision changed from EasySlip slip
/// verification to Stripe (PromptPay QR via PaymentIntent + webhook — see docs/PAYMENT.md). This
/// slip-shaped interface no longer matches the design: with Stripe there is no slip upload at all;
/// payment confirmation comes from signature-verified Stripe webhooks. It is kept only as the
/// assembly anchor for ArchitectureTests until P3 replaces it with the Stripe-shaped
/// IPaymentMethod/adapter contracts. Do not build new code against it.
/// </summary>
public interface IPaymentVerifier
{
    /// <param name="slipReference">Slip-era parameter, meaningless under the Stripe design — see
    /// the interface summary.</param>
    Task<Result<SlipVerificationResult>> VerifySlipAsync(string slipReference, CancellationToken cancellationToken);
}

public sealed record SlipVerificationResult(
    bool IsVerified,
    decimal Amount,
    string SenderAccountRef,
    string ReceiverAccountRef,
    DateTime TransactionAtUtc);
