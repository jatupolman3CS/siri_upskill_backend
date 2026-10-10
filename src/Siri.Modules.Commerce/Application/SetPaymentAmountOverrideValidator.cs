using FluentValidation;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access. Stripe enforces its own per-currency/per-method minimum charge;
/// a value below it is accepted here and surfaces as a provider error when the next payment is created.</summary>
public sealed class SetPaymentAmountOverrideValidator : AbstractValidator<SetPaymentAmountOverrideCommand>
{
    public SetPaymentAmountOverrideValidator()
    {
        RuleFor(c => c.OverrideAmount)
            .GreaterThan(0m)
            .LessThanOrEqualTo(PAYMENT_AMOUNT_OVERRIDE.MaxOverrideAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);

        RuleFor(c => c.Reason)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(PAYMENT_AMOUNT_OVERRIDE.MaxReasonLength);
    }
}
