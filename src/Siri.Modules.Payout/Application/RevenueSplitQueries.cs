using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Query building blocks shared by the real repository (EF) and the repository interface's default (in-memory) implementations, so a filter exists in
/// exactly one place and a unit test over a fake repository exercises the same predicate the database runs.
/// </summary>
internal static class RevenueSplitQueries
{
    /// <summary>
    /// The rows that make up an instructor's net earnings for a period: that instructor's (profile id) splits of the period, excluding <c>Reversed</c> ones.
    /// Negative refund-adjustment rows are kept — they reduce the period they were recorded in.
    /// </summary>
    public static IQueryable<REVENUE_SPLIT> NetForInstructorPeriod(IQueryable<REVENUE_SPLIT> source, Guid instructorProfileId, string periodKey) =>
        source.Where(r => r.INSTRUCTOR_ID == instructorProfileId
                          && r.PERIOD_KEY == periodKey
                          && r.STATUS != RevenueSplitStatus.Reversed);
}
