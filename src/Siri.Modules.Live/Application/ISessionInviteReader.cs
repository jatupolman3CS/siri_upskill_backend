using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Read-only view of <see cref="SESSION_INVITE"/> for the learner/instructor queries (docs/contracts/
/// P11-05-live-learner-instructor-api-join-gate.md sections 4.2 and 4.4). Everything here is untracked and batched. Writing invites
/// (reconcile, e-mails, reminders) belongs to the invite service of P11-04, not to this interface.
/// </summary>
public interface ISessionInviteReader
{
    /// <summary>The invite status of <paramref name="userId"/> for each of <paramref name="sessionIds"/> that has an invite (others are absent).</summary>
    Task<IReadOnlyDictionary<Guid, InviteStatus>> GetStatusesForUserAsync(
        Guid userId,
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken);

    /// <summary>The invite status of every <b>learner</b> invited to <paramref name="sessionId"/> (the instructor's own invite is excluded), keyed by user id.</summary>
    Task<IReadOnlyDictionary<Guid, InviteStatus>> GetLearnerStatusesForSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
