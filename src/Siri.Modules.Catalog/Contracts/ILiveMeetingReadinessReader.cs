namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// Whether the live sessions of a course already have a usable online room (task P11-03,
/// docs/contracts/P11-03-live-module-google-meetings.md §4.2). The room state belongs to
/// <c>Siri.Modules.Live</c> (a different aggregate in a different module), so Catalog's publish workflow asks
/// through this contract instead of the <c>COURSE</c> aggregate knowing about it.
/// <para>
/// Implemented by <c>Siri.Modules.Live</c>. Hosts that do not load Live fall back to
/// <c>NullLiveMeetingReadinessReader</c> (nothing is ever reported as missing — no gate).
/// </para>
/// </summary>
public interface ILiveMeetingReadinessReader
{
    /// <summary>
    /// Returns the subset of <paramref name="sessionIds"/> that does <b>not</b> have a usable room yet (no meeting
    /// row, or a row that is not usable). An empty result means every session is ready.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetSessionsWithoutUsableMeetingAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken);
}
