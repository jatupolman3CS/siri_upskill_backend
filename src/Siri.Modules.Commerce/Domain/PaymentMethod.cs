namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase — same reasoning as OrderStatus. Only PromptPay is
// actually reachable in v1 (docs/PAYMENT.md, docs/DECISIONS.md D-14: card checkout is out of scope
// until cards are enabled) — Card is declared now so PAYMENTS.METHOD never needs a breaking enum-shape
// change later, matching how this scaffold task's own instructions described the field: "design the
// enum open for Card later".
public enum PaymentMethod { PromptPay, Card }
