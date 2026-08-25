using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Data access for <see cref="WATCH_EVENT"/>. No <c>{Entity}Service</c>/Endpoints exist for this entity in
/// this scaffold pass — see <see cref="WATCH_EVENT"/>'s own doc comment: rows are written via a lightweight
/// append call from the (not-yet-built) playback-heartbeat flow, not through a standalone "create a watch
/// event" HTTP resource, same "no dedicated Service/Endpoints, consumed internally" shape
/// <c>Siri.Modules.Payout.Application.IPayoutBatchItemRepository</c>'s own doc comment already establishes
/// for its own module's pure-log/composition-child case. This interface still exists (and is still
/// registered in <see cref="LearningModule.AddLearningModule"/>) so a later task's heartbeat endpoint has
/// something to inject.
/// </summary>
public interface IWatchEventRepository
{
    /// <summary>Untracked (<c>AsNoTracking</c>) query source — for a later task's analytics-rollup job
    /// (docs/DATABASE.md: <c>analytics.DailyCourseStats</c>/<c>analytics.EpisodeDropOff</c> are built FROM
    /// this table by a nightly job) or a "this enrollment's watch history" read. Not consumed by anything
    /// in this scaffold pass yet.</summary>
    IQueryable<WATCH_EVENT> Query();

    /// <summary>Stages a new event for insertion — does not persist until <see cref="SaveChangesAsync"/>.
    /// Named <c>Append</c>, not <c>Add</c>, to underline this table's append-only nature (see
    /// <see cref="WATCH_EVENT"/>'s own doc comment) — functionally identical to every other repository's
    /// <c>Add</c> in this module, just named for what this table actually is.</summary>
    void Append(WATCH_EVENT watchEvent);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
