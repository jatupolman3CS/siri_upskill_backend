namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase — same reasoning as OrderStatus.
public enum RefundStatus { Requested, Approved, Rejected, Processing, Completed, Failed }
