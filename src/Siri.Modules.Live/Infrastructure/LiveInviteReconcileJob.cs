using Hangfire;
using Microsoft.Extensions.Logging;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The recurring job (<c>live-invite-reconcile</c>, every 2 minutes, registered only in <c>Siri.Workers</c>) that keeps every
/// participant's invitations in step with reality — P11-04 contract section 4.4 — and then, for courses that opted in, mirrors the invited
/// learners onto the session's Google Calendar event (section 6). A thin Hangfire wrapper: the work, its idempotency and its
/// all-or-nothing-per-course transaction live in <see cref="SessionInviteService.ReconcileAsync"/> and
/// <see cref="GoogleAttendeeSyncService.SyncAsync"/>.
/// <para>
/// The 110-second lock timeout is a hair under the 2-minute schedule, so a run that is still going when the next trigger fires makes
/// that trigger wait or give up instead of two reconciles racing (the unique <c>(SESSION_ID, USER_ID)</c> index backs this up if two
/// workers ever do overlap). The attendee sync runs <em>after</em> the reconcile so a learner invited in this run is on the event in the
/// same run, and it can never make the reconcile fail: it handles its own per-session errors, and anything it still throws is logged here.
/// </para>
/// </summary>
public sealed class LiveInviteReconcileJob(
    SessionInviteService invites,
    GoogleAttendeeSyncService attendeeSync,
    ILogger<LiveInviteReconcileJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 110)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await invites.ReconcileAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await attendeeSync.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Live attendee sync failed as a whole ({ExceptionType}); retried next run.", ex.GetType().Name);
        }
    }
}
