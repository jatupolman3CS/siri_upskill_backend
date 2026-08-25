namespace Siri.Modules.Commerce.Domain;

// Enum TYPE and MEMBERS stay normal PascalCase — same reasoning as OrderStatus. Members are an
// inferred-but-reasonable scaffold decision: docs/DATABASE.md's sketch lists a "Status" column for this
// table without enumerating its values (unlike e.g. PAYMENTS.Status, which the scaffold task's own
// instructions spelled out explicitly) — this is a standard ops-triage lifecycle, consistent with how
// this codebase already models other admin review queues (compare InstructorApplicationStatus).
public enum PaymentOpsQueueStatus { Open, InProgress, Resolved, Dismissed }
