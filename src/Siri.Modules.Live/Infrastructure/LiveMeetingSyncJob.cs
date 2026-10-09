using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The recurring job (<c>live-meeting-sync</c>, every minute, registered only in <c>Siri.Workers</c>) that drives each live session's
/// online room to a usable state — P11-03 contract section 6.3. Catalog stages a <see cref="SESSION_MEETING"/> when a class is
/// created/changed/cancelled; this job reads the class's real details and does the outside-world part: create/patch/delete the
/// instructor's Google Calendar event with a Meet room, or fall back to waiting for a manually pasted link.
/// <para>
/// <b>One meeting at a time:</b> each is loaded fresh (tracked), processed, and saved on its own; a failure in one never blocks the
/// others, and a concurrency conflict simply means "someone else changed it — next run". After any exception the change tracker is
/// cleared so a half-applied state is never saved. There is <b>no in-process retry</b> against Google: a transient failure records an
/// attempt and the entity schedules the next one 1/5/15/60 minutes later, <c>Failed</c> after the fifth.
/// </para>
/// <para>
/// <b>Never logged:</b> tokens, e-mail addresses, room URLs. Only session ids, event ids, status/error codes.
/// </para>
/// </summary>
public sealed class LiveMeetingSyncJob(
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    InstructorGoogleAccountService googleAccounts,
    ICalendarProvider calendar,
    MeetingLinkValidator linkValidator,
    IInstructorAlertSender alerts,
    ISensitiveDataProtector protector,
    IClock clock,
    IOptions<LiveOptions> liveOptions,
    ILogger<LiveMeetingSyncJob> logger)
{
    /// <summary>Meetings handled per run; the rest wait for the next minute.</summary>
    public const int BatchSize = 50;

    /// <summary>Sessions starting within this many days are checked for a missing meeting row each run.</summary>
    public const int OrphanLookaheadDays = 365;

    /// <summary>Meeting rows created for orphaned sessions per run.</summary>
    public const int OrphanBatchLimit = 200;

    /// <summary>How many times to re-read an event whose Meet room Google is still creating.</summary>
    public const int MaxConferencePolls = 3;

    /// <summary>Stop starting new meetings after this long, so one slow run cannot overlap the next trigger for long.</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromSeconds(45);

    /// <summary>Wait between conference polls (1 second; tests set it to zero).</summary>
    internal TimeSpan ConferencePollInterval { get; init; } = TimeSpan.FromSeconds(1);

    private const int SummaryMaxLength = 500;
    private const int DescriptionMaxLength = 8000;

    /// <summary>The access token handed to the fake calendar when <c>Live:Provider=Logging</c> (it ignores it).</summary>
    private static readonly string FakeAccessToken = LoggingGoogleOAuthService.DevTokenPrefix + "access-token";

    private LiveProviderMode Mode => liveOptions.Value.Provider;

    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        await AdoptOrphansAsync(cancellationToken).ConfigureAwait(false);

        var dueSessionIds = await meetings.GetDueSessionIdsAsync(clock.UtcNow, BatchSize, cancellationToken).ConfigureAwait(false);
        if (dueSessionIds.Count == 0)
        {
            return;
        }

        var contexts = (await schedule.GetSessionContextsAsync(dueSessionIds, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.SessionId);

        foreach (var sessionId in dueSessionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Stopwatch.GetElapsedTime(startedAt) > MaxRunDuration)
            {
                logger.LogInformation("Live meeting sync stopping early after {Seconds}s; remaining meetings are picked up next run.", (int)MaxRunDuration.TotalSeconds);
                break;
            }

            contexts.TryGetValue(sessionId, out var context);
            await ProcessOneAsync(sessionId, context, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Orphans ----------------------------------------------------------------------------------

    /// <summary>Gives a meeting row to every upcoming scheduled session that lacks one (classes scheduled before Live existed, or whose
    /// row was never created) — the job then treats it like any other pending meeting.</summary>
    private async Task AdoptOrphansAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var upcoming = await schedule
            .GetSessionContextsInWindowAsync(now, now.AddDays(OrphanLookaheadDays), includeCancelled: false, cancellationToken)
            .ConfigureAwait(false);
        if (upcoming.Count == 0)
        {
            return;
        }

        var existing = await meetings.GetExistingSessionIdsAsync(upcoming.Select(c => c.SessionId).ToArray(), cancellationToken).ConfigureAwait(false);
        var missing = upcoming.Where(c => !existing.Contains(c.SessionId)).Take(OrphanBatchLimit).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        foreach (var context in missing)
        {
            meetings.Add(SESSION_MEETING.Stage(context.SessionId));
        }

        try
        {
            await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Live meeting sync adopted {Count} session(s) that had no meeting row.", missing.Length);
        }
        catch (DbUpdateException ex)
        {
            // Almost certainly the sink created the same row between our read and our write (unique session id) — benign.
            logger.LogWarning("Live meeting sync could not adopt orphaned sessions ({ExceptionType}); will retry next run.", ex.GetType().Name);
            meetings.ClearTracking();
        }
    }

    // ---- One meeting ------------------------------------------------------------------------------

    private async Task ProcessOneAsync(Guid sessionId, LiveSessionContext? context, CancellationToken cancellationToken)
    {
        try
        {
            var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (meeting is null || meeting.SYNC_STATUS is not (MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete))
            {
                return; // handled or changed since it was selected
            }

            await ProcessAsync(meeting, context, cancellationToken).ConfigureAwait(false);
            await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Live meeting for session {SessionId} was changed concurrently; it will be picked up again next run.", sessionId);
            meetings.ClearTracking();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Live meeting sync failed for session {SessionId}.", sessionId);
            meetings.ClearTracking();
            await RecordUnexpectedFailureAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>An unexpected exception must not make a poisoned row spin every minute forever: count it as a failed attempt (best effort).</summary>
    private async Task RecordUnexpectedFailureAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (meeting is not null && meeting.SYNC_STATUS is MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete)
            {
                meeting.RecordAttemptFailed("internal_error", clock);
                await meetings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not record the failed attempt for live session {SessionId} ({ExceptionType}).", sessionId, ex.GetType().Name);
            meetings.ClearTracking();
        }
    }

    internal async Task ProcessAsync(SESSION_MEETING meeting, LiveSessionContext? context, CancellationToken cancellationToken)
    {
        // 1. The class is gone (deleted course): nothing to build.
        if (context is null)
        {
            meeting.MarkDeleted();
            return;
        }

        // 2. First time seen: remember whose class this is (used to reset rooms when they reconnect Google).
        if (meeting.INSTRUCTOR_USER_ID is null)
        {
            meeting.AssignInstructor(context.InstructorUserId);
        }

        // 3. Cancelled class: route it to deletion first, then carry on with whatever state that produced.
        if (context.Status == LiveSessionStatus.Cancelled
            && meeting.SYNC_STATUS is not (MeetingSyncStatus.PendingDelete or MeetingSyncStatus.Deleted))
        {
            meeting.MarkSessionCancelled();
        }

        switch (meeting.SYNC_STATUS)
        {
            case MeetingSyncStatus.PendingDelete:
                await ProcessDeleteAsync(meeting, context, cancellationToken).ConfigureAwait(false);
                break;
            case MeetingSyncStatus.Pending:
                await ProcessPendingAsync(meeting, context, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    // ---- PendingDelete ----------------------------------------------------------------------------

    private async Task ProcessDeleteAsync(SESSION_MEETING meeting, LiveSessionContext context, CancellationToken cancellationToken)
    {
        if (meeting.PROVIDER_EVENT_ID is { } eventId)
        {
            var failureCode = await DeleteEventAsync(meeting, context, eventId, cancellationToken).ConfigureAwait(false);
            if (failureCode is not null)
            {
                // Transient (or unreadable) failure: keep the event id and try again later; five failures end in Failed.
                await RecordFailedAttemptAsync(meeting, context, failureCode, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        meeting.FinishDelete(sessionStillScheduled: context.Status == LiveSessionStatus.Scheduled && context.EndsAtUtc > clock.UtcNow);
    }

    /// <summary>Deletes the Google event. Returns <c>null</c> when the delete is done <em>or deliberately given up</em>
    /// (no usable account → the event is orphaned, recorded as <c>orphan_event</c>), or an error code when it should be retried.</summary>
    private async Task<string?> DeleteEventAsync(SESSION_MEETING meeting, LiveSessionContext context, string eventId, CancellationToken cancellationToken)
    {
        if (Mode == LiveProviderMode.Logging)
        {
            await calendar.DeleteEventAsync(FakeAccessToken, eventId, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var account = Mode == LiveProviderMode.ManualOnly
            ? null
            : await googleAccounts.GetActiveAccountAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            meeting.RecordError(MeetingErrorCodes.OrphanEvent);
            return null;
        }

        var token = await googleAccounts.TryGetAccessTokenAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            if (token.Error.Code is GoogleErrors.UnauthorizedCode or InstructorGoogleAccountService.NotConnectedCode)
            {
                meeting.RecordError(MeetingErrorCodes.OrphanEvent);
                return null;
            }

            return ErrorCodeFor(token.Error);
        }

        var deleted = await calendar.DeleteEventAsync(token.Value, eventId, cancellationToken).ConfigureAwait(false);
        if (deleted.IsSuccess)
        {
            return null;
        }

        if (deleted.Error.Code == GoogleErrors.UnauthorizedCode)
        {
            await googleAccounts.HandleCalendarUnauthorizedAsync(context.InstructorUserId, deleted.Error, cancellationToken).ConfigureAwait(false);
            meeting.RecordError(MeetingErrorCodes.OrphanEvent);
            return null;
        }

        return ErrorCodeFor(deleted.Error);
    }

    // ---- Pending ----------------------------------------------------------------------------------

    private async Task ProcessPendingAsync(SESSION_MEETING meeting, LiveSessionContext context, CancellationToken cancellationToken)
    {
        // A class that has already finished never gets a room built for it.
        if (context.EndsAtUtc <= clock.UtcNow)
        {
            meeting.ResolveEndedSession();
            return;
        }

        if (Mode == LiveProviderMode.Logging)
        {
            meeting.AssignProvider(MeetingProvider.Logging, accountId: null);
            await SyncEventAsync(meeting, context, MeetingProvider.Logging, accountId: null, FakeAccessToken, cancellationToken).ConfigureAwait(false);
            return;
        }

        var account = Mode == LiveProviderMode.ManualOnly
            ? null
            : await googleAccounts.GetActiveAccountAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            await ResolveWithoutAccountAsync(meeting, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Recorded before the first call to Google, so "provider decided" holds from the first pass whether that call succeeds or not.
        meeting.AssignProvider(MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID);

        var token = await googleAccounts.TryGetAccessTokenAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            await HandleTokenFailureAsync(meeting, context, token.Error, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SyncEventAsync(meeting, context, MeetingProvider.GoogleMeet, account.INSTRUCTOR_GOOGLE_ACCOUNT_ID, token.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The instructor has no usable Google account: keep a manual link, wait for one, or flag that the old event can no longer be maintained.</summary>
    private async Task ResolveWithoutAccountAsync(SESSION_MEETING meeting, LiveSessionContext context, CancellationToken cancellationToken)
    {
        if (meeting.PROVIDER_EVENT_ID is not null)
        {
            meeting.RecordNeedsReconnect(MeetingErrorCodes.GoogleAccountUnavailable);
            return;
        }

        if (meeting.MEET_URL_ENCRYPTED is not null)
        {
            meeting.SettleWithExistingLink();
            return;
        }

        // An instructor whose Google connection FAILED (token revoked/expired/too narrow) was already told to reconnect, once, when it
        // failed — their other classes wait for that reconnect (NeedsReconnect), they are not asked to paste a link each time. Someone who
        // never connected, or disconnected on purpose, gets the paste-a-link prompt. (The same rule LiveMeetingSink applies synchronously when the
        // class is created — MeetingProviderDecision — so a row decided there and a row decided here can never disagree.)
        if (Mode != LiveProviderMode.ManualOnly
            && MeetingProviderDecision.BrokenConnectionReason(
                    await googleAccounts.GetAccountAsync(context.InstructorUserId, cancellationToken).ConfigureAwait(false))
                is { } revokedReason)
        {
            meeting.RecordNeedsReconnect(revokedReason);
            return;
        }

        meeting.ResolveAsAwaitingLink();

        // Tell the instructor once.
        if (meeting.MEETING_ALERT_SENT_AT_UTC is null)
        {
            await alerts.MeetingNeedsLinkAsync(context.InstructorUserId, context.SessionId, context.Title, context.CourseTitle, cancellationToken).ConfigureAwait(false);
            meeting.MarkAlertSent(clock);
        }
    }

    private async Task HandleTokenFailureAsync(SESSION_MEETING meeting, LiveSessionContext context, DomainError error, CancellationToken cancellationToken)
    {
        switch (error.Code)
        {
            case GoogleErrors.UnauthorizedCode:
                // The account was just revoked and the instructor alerted by InstructorGoogleAccountService.
                meeting.RecordNeedsReconnect(MeetingErrorCodes.InvalidGrant);
                break;

            case InstructorGoogleAccountService.NotConnectedCode:
                await ResolveWithoutAccountAsync(meeting, context, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await RecordFailedAttemptAsync(meeting, context, ErrorCodeFor(error), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    // ---- Calendar event ---------------------------------------------------------------------------

    private async Task SyncEventAsync(
        SESSION_MEETING meeting,
        LiveSessionContext context,
        MeetingProvider provider,
        Guid? accountId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(meeting, context);
        var updatedExisting = meeting.PROVIDER_EVENT_ID is not null;

        Result<CalendarEventResult> result;
        if (meeting.PROVIDER_EVENT_ID is { } knownEventId)
        {
            result = await calendar.UpdateEventAsync(accessToken, knownEventId, request, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure && result.Error.Code == GoogleErrors.NotFoundCode)
            {
                // The instructor deleted the event in Google: build a new one (new request id — Google ignores a repeated one).
                meeting.ClearProviderEvent();
                updatedExisting = false;
                result = await calendar
                    .CreateEventWithMeetAsync(accessToken, request with { RequestId = NewRequestId(meeting) }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            result = await CreateOrAdoptAsync(meeting, request, accessToken, cancellationToken).ConfigureAwait(false);
        }

        if (result.IsFailure)
        {
            await HandleCalendarFailureAsync(meeting, context, result.Error, cancellationToken).ConfigureAwait(false);
            return;
        }

        var calendarEvent = result.Value;

        if (calendarEvent.MeetUrl is null && calendarEvent.ConferencePending)
        {
            var polled = await PollConferenceAsync(accessToken, calendarEvent, cancellationToken).ConfigureAwait(false);
            if (polled.IsFailure)
            {
                await HandleCalendarFailureAsync(meeting, context, polled.Error, cancellationToken).ConfigureAwait(false);
                return;
            }

            calendarEvent = polled.Value;
        }

        if (calendarEvent.MeetUrl is null)
        {
            // A patch that reports no room keeps the one we already have; a create that yields none is a failed attempt.
            if (updatedExisting && meeting.MEET_URL_ENCRYPTED is not null)
            {
                meeting.RecordGoogleSynced(provider, calendarEvent.EventId, meeting.MEET_URL_ENCRYPTED, accountId, clock);
                return;
            }

            await RecordFailedAttemptAsync(
                meeting,
                context,
                calendarEvent.ConferencePending ? MeetingErrorCodes.ConferencePending : MeetingErrorCodes.ConferenceFailed,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var acceptedUrl = AcceptMeetUrl(provider, calendarEvent.MeetUrl);
        if (acceptedUrl is null)
        {
            await RecordFailedAttemptAsync(meeting, context, MeetingErrorCodes.MeetUrlRejected, cancellationToken).ConfigureAwait(false);
            return;
        }

        meeting.RecordGoogleSynced(provider, calendarEvent.EventId, protector.Encrypt(acceptedUrl), accountId, clock);
    }

    /// <summary>First create — or, after an earlier failed attempt, look for an event that a lost response may already have created
    /// (found by its private session id) and adopt it instead of making a duplicate.</summary>
    private async Task<Result<CalendarEventResult>> CreateOrAdoptAsync(
        SESSION_MEETING meeting, CalendarEventRequest request, string accessToken, CancellationToken cancellationToken)
    {
        if (meeting.ATTEMPTS > 0)
        {
            var found = await calendar.FindEventByPrivateSessionIdAsync(accessToken, request.PrivateSessionId, cancellationToken).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return Result.Failure<CalendarEventResult>(found.Error);
            }

            if (found.Value is { } existing)
            {
                var updated = await calendar.UpdateEventAsync(accessToken, existing.EventId, request, cancellationToken).ConfigureAwait(false);
                if (updated.IsFailure)
                {
                    return updated;
                }

                return Result.Success(updated.Value with { MeetUrl = updated.Value.MeetUrl ?? existing.MeetUrl });
            }
        }

        return await calendar.CreateEventWithMeetAsync(accessToken, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-reads an event whose Meet room Google is still creating, up to <see cref="MaxConferencePolls"/> times.</summary>
    private async Task<Result<CalendarEventResult>> PollConferenceAsync(string accessToken, CalendarEventResult initial, CancellationToken cancellationToken)
    {
        var current = initial;
        for (var attempt = 0; attempt < MaxConferencePolls && current.MeetUrl is null && current.ConferencePending; attempt++)
        {
            await Task.Delay(ConferencePollInterval, cancellationToken).ConfigureAwait(false);

            var polled = await calendar.GetEventAsync(accessToken, current.EventId, cancellationToken).ConfigureAwait(false);
            if (polled.IsFailure)
            {
                return polled;
            }

            current = polled.Value;
        }

        return Result.Success(current);
    }

    private async Task HandleCalendarFailureAsync(SESSION_MEETING meeting, LiveSessionContext context, DomainError error, CancellationToken cancellationToken)
    {
        if (error.Code == GoogleErrors.UnauthorizedCode)
        {
            var reason = await googleAccounts.HandleCalendarUnauthorizedAsync(context.InstructorUserId, error, cancellationToken).ConfigureAwait(false);
            meeting.RecordNeedsReconnect(reason);
            return;
        }

        await RecordFailedAttemptAsync(meeting, context, ErrorCodeFor(error), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a failed attempt (backoff 1/5/15/60 minutes) and, on the transition to <c>Failed</c>, alerts the instructor once.</summary>
    private async Task RecordFailedAttemptAsync(SESSION_MEETING meeting, LiveSessionContext context, string code, CancellationToken cancellationToken)
    {
        var wasFailed = meeting.SYNC_STATUS == MeetingSyncStatus.Failed;
        meeting.RecordAttemptFailed(code, clock);

        if (!wasFailed && meeting.SYNC_STATUS == MeetingSyncStatus.Failed)
        {
            logger.LogWarning("Live meeting for session {SessionId} failed after {Attempts} attempts ({ErrorCode}).", context.SessionId, meeting.ATTEMPTS, code);
            await alerts.MeetingFailedAsync(context.InstructorUserId, context.SessionId, context.Title, context.CourseTitle, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Mapping helpers ---------------------------------------------------------------------------

    /// <summary>The URL Google (or the fake) produced, normalised — or <c>null</c> if it must not be stored. Google's URL goes through
    /// the same host allow-list as a pasted link. The Logging provider's fake host is not on that list, so it is accepted only when it is
    /// exactly the fake provider's own URL shape (and only because <c>Live:Provider=Logging</c> is explicitly selected).</summary>
    private string? AcceptMeetUrl(MeetingProvider provider, string meetUrl)
    {
        if (provider == MeetingProvider.Logging)
        {
            return meetUrl.StartsWith(LoggingCalendarProvider.DevMeetUrlPrefix, StringComparison.Ordinal) ? meetUrl : null;
        }

        var validated = linkValidator.Validate(meetUrl);
        return validated.IsSuccess ? validated.Value : null;
    }

    private CalendarEventRequest BuildRequest(SESSION_MEETING meeting, LiveSessionContext context)
    {
        var joinLink = $"{liveOptions.Value.GetNormalizedPublicBaseUrl()}/live/{context.SessionId:D}/join";

        // Learners only ever see the platform's join link (never the Meet URL, never each other's names/e-mails).
        var description = $"เข้าห้องเรียนผ่านแพลตฟอร์ม: {joinLink}";
        if (!string.IsNullOrWhiteSpace(context.Description))
        {
            description += "\n\n" + context.Description.Trim();
        }

        return new CalendarEventRequest(
            Truncate($"{context.CourseTitle} — {context.Title}", SummaryMaxLength),
            Truncate(description, DescriptionMaxLength),
            context.StartsAtUtc,
            context.EndsAtUtc,
            RequestId: meeting.SESSION_MEETING_ID.ToString("N"),
            PrivateSessionId: context.SessionId.ToString("N"));
    }

    private static string NewRequestId(SESSION_MEETING meeting) =>
        $"{meeting.SESSION_MEETING_ID:N}-{Guid.NewGuid().ToString("N")[..8]}";

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>The short stored code for a Google error. Never the provider's message.</summary>
    internal static string ErrorCodeFor(DomainError error) => error.Code switch
    {
        GoogleErrors.RateLimitedCode => MeetingErrorCodes.GoogleRateLimited,
        // Platform-side credential problems (Google rejects our OAuth client, or we cannot decrypt the stored token) share one stored code.
        GoogleErrors.TransientCode => error.Reason is "invalid_client" or InstructorGoogleAccountService.CredentialUnreadableReason
            ? MeetingErrorCodes.GoogleClientMisconfigured
            : MeetingErrorCodes.GoogleTransient,
        GoogleErrors.BadRequestCode => MeetingErrorCodes.GoogleBadRequest,
        GoogleErrors.NotConfiguredCode => MeetingErrorCodes.GoogleNotConfigured,
        GoogleErrors.NotFoundCode => MeetingErrorCodes.ConferenceFailed,
        GoogleErrors.UnauthorizedCode => MeetingErrorCodes.InvalidGrant,
        _ => MeetingErrorCodes.GoogleTransient,
    };
}
