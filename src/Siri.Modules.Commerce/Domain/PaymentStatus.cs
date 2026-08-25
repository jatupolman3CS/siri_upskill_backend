namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase — same reasoning as OrderStatus.
public enum PaymentStatus { Pending, Processing, Succeeded, Failed, Expired, Refunded }
