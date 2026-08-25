using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

public sealed record InstructorPayoutHistoryItem(
    Guid PayoutId,
    string BatchNumber,
    decimal GrossAmount,
    decimal NetAmount,
    decimal WithholdingTaxAmount,
    DateTime? PeriodEndUtc,
    string Status,
    DateTime? PaidAtUtc);

public sealed record InstructorEarningsSummaryResponse(
    decimal CumulativeEarnings,
    decimal LatestPayoutAmount,
    decimal EstimatedNextPayoutAmount,
    IReadOnlyList<InstructorPayoutHistoryItem> History);
