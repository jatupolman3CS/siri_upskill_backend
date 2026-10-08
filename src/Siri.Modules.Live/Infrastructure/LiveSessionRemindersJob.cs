using Hangfire;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The recurring job (<c>live-session-reminders</c>, every 5 minutes, registered only in <c>Siri.Workers</c>) that sends the 24-hour
/// and 1-hour reminders to invited learners and to the instructor, and warns an instructor whose session starts within a day without
/// a usable room — P11-04 contract section 4.5. A thin Hangfire wrapper around <see cref="SessionInviteService.SendRemindersAsync"/>;
/// each reminder is sent together with the flag column that records it, so re-running never double-sends.
/// </summary>
public sealed class LiveSessionRemindersJob(SessionInviteService service)
{
    [DisableConcurrentExecution(timeoutInSeconds: 280)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await service.SendRemindersAsync(cancellationToken).ConfigureAwait(false);
    }
}
