using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce;

/// <summary>
/// Which payment methods are actually enabled right now — an operational switch separate from code
/// deploys, so the project owner can turn Card on only once it's also enabled in the Stripe Dashboard
/// (see OWN dependency in docs/TASKS.md P11-09). Default is PromptPay only — set
/// "Payment:EnabledMethods": ["PromptPay","Card"] in config/env to enable Card.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    public List<PaymentMethod> EnabledMethods { get; set; } = [PaymentMethod.PromptPay];
}
