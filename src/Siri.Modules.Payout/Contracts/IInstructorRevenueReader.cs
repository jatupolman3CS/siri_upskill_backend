namespace Siri.Modules.Payout.Contracts;

/// <summary>
/// Read-only revenue facts the Payout module publishes to other modules — the instructor dashboard (Analytics, P11-10).
/// </summary>
public interface IInstructorRevenueReader
{
    /// <summary>
    /// The instructor's net earnings of one revenue period: the sum of <c>REVENUE_SPLITS.INSTRUCTOR_AMOUNT</c> of every split of
    /// <paramref name="instructorProfileId"/> whose <c>PERIOD_KEY</c> equals <paramref name="periodKey"/> (<c>yyyy-MM</c>, UTC month) and whose status is
    /// not <c>Reversed</c>. Refund adjustments recorded after a payout are negative rows in the period they were created in and are included, so the
    /// result is the true net of that month. <c>0</c> when nothing was earned.
    /// <para>
    /// <paramref name="instructorProfileId"/> is the <b>instructor profile</b> id (what <c>REVENUE_SPLITS.INSTRUCTOR_ID</c> stores) — never the user id.
    /// Resolve it from the authenticated user with <c>Catalog.Contracts.IInstructorProfileReader</c>; a caller must never pass an id that did not come from there.
    /// </para>
    /// </summary>
    Task<decimal> GetNetRevenueForPeriodAsync(Guid instructorProfileId, string periodKey, CancellationToken cancellationToken);
}
