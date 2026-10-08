using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Live.Domain;
using Siri.Modules.Notification.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>What one <see cref="GoogleAttendeeSyncService.SyncAsync"/> pass did.</summary>
/// <param name="SessionsSynced">Sessions whose Google event now lists the right attendees.</param>
/// <param name="SessionsOverCap">Sessions skipped because they have more invited learners than the cap.</param>
/// <param name="SessionsSkipped">Sessions that had work but could not be done this run (no/invalid Google credential, transient error, event gone) — retried next run.</param>
/// <param name="SessionsFailed">Sessions whose unit of work threw — rolled back, retried next run.</param>
public sealed record AttendeeSyncResult(int SessionsSynced, int SessionsOverCap, int SessionsSkipped, int SessionsFailed);

/// <summary>
/// The opt-in Google Calendar attendee sync (docs/contracts/P11-04-live-invites-ics-reminders.md §6): when an instructor has switched it on
/// for a course, every learner with an <see cref="InviteStatus.Invited"/> invite is added to the session's Google event, so a person who was
/// invited walks straight into the Meet room while anyone who merely received a forwarded link must <em>ask to join</em> and the host decides.
/// <para>
/// <b>Privacy:</b> this sends learners' e-mail addresses to Google, which is why it is off by default and only runs for a course whose owner
/// turned it on (<c>COURSES.GOOGLE_ATTENDEE_SYNC_ENABLED</c>). The event is written with <c>sendUpdates=none</c> (the invitation e-mail comes
/// from this platform, not Google) and was created with <c>guestsCanSeeOtherGuests=false</c>, so learners never see each other's address.
/// Addresses are never logged — only session ids and counts.
/// </para>
/// <para>
/// <b>One Google call per session per run, and only on a difference:</b> a session is touched when an invited learner is not on the event yet
/// or a withdrawn one still is; the call replaces the event's whole guest list with the current invited set; the per-invite
/// <c>GOOGLE_ATTENDEE_SYNCED_AT_UTC</c> stamps are updated in the same save, so a re-run finds nothing to do.
/// </para>
/// <para>
/// <b>Cap:</b> a Google event holds about 200 guests; above <c>Live:GoogleAttendeeCap</c> (default 150) the sync is switched off for that
/// session — Google is not called, the instructor is told once through an in-app notification, and the calendar e-mails to learners carry on
/// as usual. When the count falls back under the cap the sync resumes by itself.
/// </para>
/// </summary>
public sealed class GoogleAttendeeSyncService(
    ISessionInviteRepository invites,
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    IUserContactReader contacts,
    InstructorGoogleAccountService googleAccounts,
    ICalendarProvider calendar,
    IUserNotificationOutbox notifications,
    IOptions<LiveOptions> options,
    IClock clock,
    ILogger<GoogleAttendeeSyncService> logger)
{
    /// <summary>Sessions synced per run; the rest wait for the next one.</summary>
    public const int MaxSessionsPerRun = 20;

    /// <summary>Stop starting new sessions after this long, so a slow Google cannot make one run overlap the next trigger for long.</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromSeconds(60);

    private LiveOptions Options => options.Value;

    public async Task<AttendeeSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        // ManualOnly never calls Google; Logging mode's rooms are not Google events (provider is not GoogleMeet) so nothing qualifies anyway.
        if (Options.Provider == LiveProviderMode.ManualOnly)
        {
            return new AttendeeSyncResult(0, 0, 0, 0);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var now = clock.UtcNow;

        var contexts = await LiveScheduleWindow
            .GetAllAsync(schedule, now, now.AddDays(Options.InviteLookaheadDays), includeCancelled: false, cancellationToken)
            .ConfigureAwait(false);

        var enabled = contexts
            .Where(c => c.GoogleAttendeeSyncEnabled && SessionInvitePolicy.IsUpcoming(c, now))
            .ToList();
        if (enabled.Count == 0)
        {
            return new AttendeeSyncResult(0, 0, 0, 0);
        }

        var meetingRows = await meetings
            .GetBySessionIdsAsync(enabled.Select(c => c.SessionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        var meetingBySession = meetingRows
            .Where(m => m.PROVIDER == MeetingProvider.GoogleMeet
                && m.SYNC_STATUS == MeetingSyncStatus.Synced
                && !string.IsNullOrEmpty(m.PROVIDER_EVENT_ID))
            .ToDictionary(m => m.SESSION_ID);

        var candidates = enabled.Where(c => meetingBySession.ContainsKey(c.SessionId)).ToList();
        if (candidates.Count == 0)
        {
            return new AttendeeSyncResult(0, 0, 0, 0);
        }

        var needing = (await invites
                .GetSessionIdsNeedingAttendeeSyncAsync(candidates.Select(c => c.SessionId).ToArray(), cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var synced = 0;
        var overCap = 0;
        var skipped = 0;
        var failed = 0;
        var unusableInstructors = new HashSet<Guid>(); // a revoked/unusable credential is not retried within the same run

        foreach (var context in candidates.Where(c => needing.Contains(c.SessionId)).OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId).Take(MaxSessionsPerRun))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Stopwatch.GetElapsedTime(startedAt) > MaxRunDuration)
            {
                logger.LogInformation("Live attendee sync stopping early after {Seconds}s; remaining sessions are picked up next run.", (int)MaxRunDuration.TotalSeconds);
                break;
            }

            if (unusableInstructors.Contains(context.InstructorUserId))
            {
                skipped++;
                continue;
            }

            try
            {
                switch (await SyncSessionAsync(context, meetingBySession[context.SessionId], unusableInstructors, cancellationToken).ConfigureAwait(false))
                {
                    case SessionOutcome.Synced:
                        synced++;
                        break;
                    case SessionOutcome.OverCap:
                        overCap++;
                        break;
                    case SessionOutcome.Skipped:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                invites.ClearTracking();
                failed++;
                logger.LogError("Live attendee sync failed for session {SessionId} ({ExceptionType}); retried next run.", context.SessionId, ex.GetType().Name);
            }
        }

        if (synced + overCap + skipped + failed > 0)
        {
            logger.LogInformation(
                "Live attendee sync: {Synced} synced, {OverCap} over the cap, {Skipped} skipped, {Failed} failed.", synced, overCap, skipped, failed);
        }

        return new AttendeeSyncResult(synced, overCap, skipped, failed);
    }

    private enum SessionOutcome
    {
        NothingToDo,
        Synced,
        OverCap,
        Skipped,
    }

    private async Task<SessionOutcome> SyncSessionAsync(
        LiveSessionContext context, SESSION_MEETING snapshot, HashSet<Guid> unusableInstructors, CancellationToken cancellationToken)
    {
        var all = await invites.GetBySessionIdsAsync([context.SessionId], cancellationToken).ConfigureAwait(false);
        var learners = all.Where(i => i.ROLE == LiveParticipantRole.Learner).ToList();

        var desired = learners
            .Where(i => i.STATUS == InviteStatus.Invited)
            .OrderBy(i => i.INVITE_SENT_AT_UTC)
            .ThenBy(i => i.USER_ID)
            .ToList();
        var toRemove = learners.Where(i => i.STATUS != InviteStatus.Invited && i.GOOGLE_ATTENDEE_SYNCED_AT_UTC is not null).ToList();
        var toAdd = desired.Where(i => i.GOOGLE_ATTENDEE_SYNCED_AT_UTC is null).ToList();

        // Cap: above it Google is not called at all (a partial list would admit some invited learners and not others).
        if (desired.Count > Options.GoogleAttendeeCap)
        {
            await WarnOverCapAsync(context, desired.Count, cancellationToken).ConfigureAwait(false);
            invites.ClearTracking();
            return SessionOutcome.OverCap;
        }

        var tracked = await meetings.GetBySessionIdAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
        var resumedUnderCap = tracked?.ATTENDEE_SYNC_ALERT_SENT_AT_UTC is not null;
        if (tracked is not null && resumedUnderCap)
        {
            tracked.ClearAttendeeSyncAlert();
        }

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            if (resumedUnderCap)
            {
                await invites.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            invites.ClearTracking();
            return SessionOutcome.NothingToDo;
        }

        // Only the people who can actually be written to the event: an invited learner whose address has gone missing cannot be.
        var contactMap = desired.Count == 0
            ? new Dictionary<Guid, (string Email, string DisplayName)>()
            : (await contacts.GetUsersContactInfoAsync(desired.Select(i => i.USER_ID).ToArray(), cancellationToken).ConfigureAwait(false))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        var onEvent = desired.Where(i => contactMap.TryGetValue(i.USER_ID, out var c) && SessionInviteService.IsDeliverableEmail(c.Email)).ToList();
        var emails = onEvent.Select(i => contactMap[i.USER_ID].Email.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var instructorUserId = context.InstructorUserId;
        var token = await googleAccounts.TryGetAccessTokenAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            // Not connected / revoked (the token service already alerted the instructor once) / transient: no calls this run.
            if (token.Error.Code == GoogleErrors.UnauthorizedCode || token.Error.Code == InstructorGoogleAccountService.NotConnectedCode)
            {
                unusableInstructors.Add(instructorUserId);
            }

            invites.ClearTracking();
            return SessionOutcome.Skipped;
        }

        var result = await calendar
            .SetAttendeesAsync(token.Value, snapshot.PROVIDER_EVENT_ID!, emails, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (result.Error.Code == GoogleErrors.UnauthorizedCode)
            {
                // The fresh token was refused: the grant no longer covers Calendar. Revoke + alert once, and stop for this instructor.
                await googleAccounts.HandleCalendarUnauthorizedAsync(instructorUserId, result.Error, cancellationToken).ConfigureAwait(false);
                unusableInstructors.Add(instructorUserId);
            }

            // not_found (the event is gone — the meeting sync job re-creates it), rate-limited, transient, bad_request: leave the
            // stamps alone so the next run retries. Codes only in the log — no address, no event URL.
            logger.LogWarning("Live attendee sync for session {SessionId} not applied ({ErrorCode}); retried next run.", context.SessionId, result.Error.Code);
            invites.ClearTracking();
            return SessionOutcome.Skipped;
        }

        foreach (var invite in onEvent)
        {
            invite.MarkAttendeeSynced(clock);
        }

        // Invited but unreachable (the address is gone): same treatment as the reminder job — they cannot be written to the event,
        // so they stop being "invited" instead of being re-examined on every run.
        foreach (var invite in desired.Except(onEvent))
        {
            invite.MarkSkipped(SessionInviteService.NoContactCode);
            invite.ClearAttendeeSynced();
        }

        foreach (var invite in toRemove)
        {
            invite.ClearAttendeeSynced();
        }

        await invites.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invites.ClearTracking();
        return SessionOutcome.Synced;
    }

    /// <summary>Tells the instructor (in-app, once) that this session is over the attendee cap and the sync is off for it.</summary>
    private async Task WarnOverCapAsync(LiveSessionContext context, int invitedCount, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null || meeting.ATTENDEE_SYNC_ALERT_SENT_AT_UTC is not null)
        {
            return;
        }

        var settings = LiveTemplateSettings.From(Options);
        var title = LiveEmailTemplates.SubjectText(LiveEmailTemplates.Clean(settings, context.Title));
        var course = LiveEmailTemplates.Clean(settings, context.CourseTitle);

        notifications.Stage(
            context.InstructorUserId,
            LiveEmailTemplates.InAppMeetingAlert,
            $"ผู้เรียนเกินเพดานการเชิญผ่าน Google: {title}",
            $"{course} — คาบนี้มีผู้เรียน {invitedCount} คน เกินเพดาน {Options.GoogleAttendeeCap} คน ระบบจึงหยุดเพิ่มผู้เรียนเป็นผู้เข้าร่วมใน Google Calendar สำหรับคาบนี้ " +
            "ผู้เรียนยังได้รับอีเมลและไฟล์ปฏิทินตามปกติ แนะนำให้ตั้งค่าให้ผู้จัดต้องอนุญาตก่อนเข้าห้อง",
            LiveTemplateSettings.InstructorSessionPath(context.SessionId));

        meeting.MarkAttendeeSyncAlertSent(clock);
        await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
