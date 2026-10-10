using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The recurring job (<c>live-recording-import</c>, every five minutes, scheduled by <c>RecurringJobsRegistration</c> in both hosts that can run the Hangfire
/// server) that turns a Google Meet recording into a lesson without the instructor lifting a finger — P11-13 contract section 6. A no-op unless
/// <c>Live:Recording:AutoImport:Enabled</c>.
/// <list type="number">
/// <item><b>Discovery</b> — sessions that ended (not cancelled, no recording lesson, with a platform-made Google room) whose instructor has <c>Auto</c> capability get
/// a <see cref="RecordingImportStatus.Waiting"/> row. Anything else is the manual upload path and gets no row. Catalog is asked only about the sessions of instructors
/// who could be imported at all, and the window is paged through until it is exhausted (hard cap), so sessions of instructors on the manual path can never crowd a
/// Workspace instructor's session out of the list.</item>
/// <item><b>Work</b> — each due row, oldest first, one at a time: <c>Waiting</c> (find the finished recording in Google; poll with a backoff until the search window closes;
/// when it is found, <b>claim</b> the row — <c>Transferring</c> + lease, saved with the version that was read — and queue a <see cref="LiveRecordingTransferJob"/> for
/// it) → <c>Transferring</c> (that background job streams the file from Drive to the video provider; the tick only reclaims a row whose lease ran out) →
/// <c>Processing</c> (wait for the provider to transcode) → <c>Attached</c> (the existing "attach a session recording" rules, unchanged).</item>
/// </list>
/// <para>
/// <b>The tick is short and never copies a file.</b> A multi-GB copy inside it would hold its storage lock for hours, every following tick would wait out the lock
/// and fail, and the admin status would report a failing recurring job. The copy runs as its own Hangfire background job per claimed import; the tick stops starting
/// new rows after <see cref="MaxRunDuration"/> (below the lock timeout) so a slow Google never makes the next trigger fail either.
/// </para>
/// <para>
/// <b>Every state change is one <c>SaveChangesAsync</c>, and no external call happens inside a database transaction</b> (the entity is read, the outside world is
/// asked, then the result is saved). <b>The job never throws out of a row</b>: a failure is logged by code only and recorded on the row (a transient failure
/// counts an attempt and backs off; the configured maximum ends it <c>Failed</c>); a concurrency conflict simply means "someone else moved it — next run". A transfer is
/// claimed first, and queued only after the claim is saved, so a claimed row is queued once; the lease is the safety net that makes a lost job (a crash between claim and
/// queue, a dead worker) reclaimable exactly as before.
/// </para>
/// <para>
/// <b>Never logged:</b> tokens, meeting codes, room links, Drive file ids, asset ids, titles. Only import/session ids (internal GUIDs), status and error codes.
/// </para>
/// <para>
/// <b>Hangfire:</b> serialized by a storage lock like the other Live jobs, with automatic retry off - the next tick is the retry.
/// </para>
/// </summary>
public sealed class LiveRecordingImportJob(
    ISessionRecordingImportRepository imports,
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    ILiveRecordingAttacher attacher,
    IGoogleMeetRecordingProvider recordings,
    IMediaIngestContract ingest,
    IMediaAssetContract mediaAssets,
    InstructorGoogleAccountService googleAccounts,
    SessionMeetingService meetingService,
    IInstructorAlertSender alerts,
    IRecordingTransferScheduler transfers,
    IClock clock,
    IOptions<LiveOptions> liveOptions,
    ILogger<LiveRecordingImportJob> logger)
{
    /// <summary>Sessions that ended within this long ago are considered by discovery (an older one only gets an import through the instructor's retry).</summary>
    public static readonly TimeSpan DiscoveryLookback = TimeSpan.FromHours(48);

    /// <summary>Sessions asked of Catalog per page.</summary>
    public const int DiscoveryPageSize = 200;

    /// <summary>Hard cap on pages per run (so <c>DiscoveryPageSize * MaxDiscoveryPages</c> sessions at most), however many sessions the window holds.</summary>
    public const int MaxDiscoveryPages = 10;

    /// <summary>The search starts this long before the scheduled end: a class can run long, and Meet records from when someone presses record.</summary>
    public static readonly TimeSpan SearchLookBehind = TimeSpan.FromHours(6);

    /// <summary>How often a <c>Processing</c> import checks whether the video provider has finished (it is a local database read, so this is cheap).</summary>
    public static readonly TimeSpan ProcessingPollInterval = TimeSpan.FromMinutes(2);

    /// <summary>The video provider has this long to transcode a copy before the import gives up (<c>transcode_timeout</c>).</summary>
    public static readonly TimeSpan TranscodeTimeout = TimeSpan.FromHours(6);

    /// <summary>Stop starting new imports after this long. Kept under <see cref="LockTimeoutSeconds"/> so a slow tick finishes before the next trigger gives up waiting
    /// for the storage lock (which would show as a failing recurring job).</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromSeconds(100);

    /// <summary>How long a trigger waits for the storage lock held by a still-running tick.</summary>
    public const int LockTimeoutSeconds = 120;

    private const string StatusReady = "Ready";
    private const string StatusFailed = "Failed";

    private LiveRecordingAutoImportOptions Settings => liveOptions.Value.Recording.AutoImport;

    [DisableConcurrentExecution(timeoutInSeconds: LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!Settings.Enabled || liveOptions.Value.Provider == LiveProviderMode.ManualOnly)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A broken discovery must not stop the rows that are already due.
            logger.LogError("Live recording import discovery failed: {ExceptionType}.", ex.GetType().Name);
            imports.ClearTracking();
        }

        var dueIds = await imports.GetDueIdsAsync(clock.UtcNow, Settings.BatchSize, cancellationToken).ConfigureAwait(false);
        if (dueIds.Count == 0)
        {
            return;
        }

        foreach (var importId in dueIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Stopwatch.GetElapsedTime(startedAt) > MaxRunDuration)
            {
                logger.LogInformation("Live recording import stopping early after {Seconds}s; the remaining imports are picked up next run.", (int)MaxRunDuration.TotalSeconds);
                break;
            }

            await ProcessOneAsync(importId, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Discovery ---------------------------------------------------------------------------------

    /// <summary>
    /// Writes a <c>Waiting</c> row for each ended session that the automatic path should handle. A row is only ever written for work to do.
    /// <para>
    /// <b>Why it cannot starve:</b> Catalog lists oldest first with a limit, so a plain "list everything that ended recently" call would re-list the same sessions that
    /// never get a row (manual-path instructors, sessions without a Google room) on every run, and past one page of them a Workspace instructor's newer session would never
    /// be reached. So (1) only the instructors who could be imported at all are asked about - no candidate, no Catalog call - and (2) the window is paged with a cursor
    /// (the end time of the last session of the page) until a short page says it is exhausted, up to <see cref="MaxDiscoveryPages"/> pages.
    /// </para>
    /// </summary>
    internal async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        var instructorIds = await googleAccounts.GetRecordingCandidateInstructorIdsAsync(cancellationToken).ConfigureAwait(false);
        if (instructorIds.Count == 0)
        {
            return; // nobody on the automatic path: nothing to discover, and Catalog is not even asked
        }

        var now = clock.UtcNow;
        var before = now - TimeSpan.FromMinutes(Settings.FirstSearchDelayMinutes);
        var after = now - DiscoveryLookback;

        var seen = new HashSet<Guid>();
        var capabilities = new Dictionary<Guid, RecordingCapability>();
        var added = 0;

        for (var page = 0; page < MaxDiscoveryPages; page++)
        {
            var batch = await attacher
                .ListEndedByInstructorsAsync(instructorIds, after, before, DiscoveryPageSize, cancellationToken)
                .ConfigureAwait(false);

            // The cursor is inclusive (the last session's own end time), so the next page starts with sessions already seen: skip them.
            var fresh = batch.Where(s => seen.Add(s.SessionId)).ToList();
            added += await StageNewImportsAsync(fresh, capabilities, cancellationToken).ConfigureAwait(false);

            if (batch.Count < DiscoveryPageSize)
            {
                break; // a short page = the window is exhausted
            }

            var cursor = batch[^1].EndsAtUtc;

            // A whole page of sessions that all end in the very same instant would leave the inclusive cursor where it was; step over that instant instead of looping
            // (the few sessions beyond that page which end in the same millisecond are skipped - not a case that occurs with real classes).
            after = cursor <= after ? cursor.AddMilliseconds(1) : cursor;
        }

        if (added == 0)
        {
            return;
        }

        try
        {
            await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Live recording import discovered {Count} session(s) to import.", added);
        }
        catch (DbUpdateException ex)
        {
            // Almost certainly the instructor's retry (or another host) created the same row between our read and our write (unique session id) — benign.
            logger.LogWarning("Live recording import could not record newly discovered sessions ({ExceptionType}); will retry next run.", ex.GetType().Name);
            imports.ClearTracking();
        }
    }

    /// <summary>Adds (without saving) a <c>Waiting</c> row for each session of the page that qualifies; returns how many.</summary>
    private async Task<int> StageNewImportsAsync(
        List<EndedLiveSession> page, Dictionary<Guid, RecordingCapability> capabilities, CancellationToken cancellationToken)
    {
        // A cancelled class, or one that already has a recording lesson (uploaded by hand), has nothing to import.
        var candidates = page.Where(s => !s.IsCancelled && !s.HasRecording).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var existing = await imports.GetExistingSessionIdsAsync(candidates.Select(s => s.SessionId).ToArray(), cancellationToken).ConfigureAwait(false);
        var fresh = candidates.Where(s => !existing.Contains(s.SessionId)).ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        var rooms = (await meetings.GetBySessionIdsAsync(fresh.Select(s => s.SessionId).ToArray(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.SESSION_ID);

        var added = 0;
        foreach (var session in fresh)
        {
            if (!RecordingImportRules.HasGoogleRoom(rooms.GetValueOrDefault(session.SessionId)))
            {
                continue;
            }

            // Capability is a property of the instructor: decided once each per run.
            if (!capabilities.TryGetValue(session.InstructorUserId, out var capability))
            {
                capability = await CapabilityOfAsync(session.InstructorUserId, cancellationToken).ConfigureAwait(false);
                capabilities[session.InstructorUserId] = capability;
            }

            if (capability != RecordingCapability.Auto)
            {
                continue;
            }

            imports.Add(SESSION_RECORDING_IMPORT.Create(
                session.SessionId,
                session.CourseId,
                session.InstructorUserId,
                firstSearchAtUtc: session.EndsAtUtc.AddMinutes(Settings.FirstSearchDelayMinutes),
                searchUntilUtc: session.EndsAtUtc.AddHours(Settings.SearchWindowHours)));
            added++;
        }

        return added;
    }

    /// <summary>Exact capability from the stored account row. No Google call: an account that could be imported already has its kind (it was stamped when the
    /// recording consent was granted), and an account of unknown kind can never hold the recording scopes.</summary>
    private async Task<RecordingCapability> CapabilityOfAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var account = await googleAccounts.GetAccountAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        return RecordingCapabilityCalculator.Compute(autoImportEnabled: true, liveOptions.Value.Provider, account);
    }

    // ---- One import --------------------------------------------------------------------------------

    /// <summary>One due row of the tick.</summary>
    private Task ProcessOneAsync(Guid importId, CancellationToken cancellationToken) =>
        GuardedAsync(
            importId,
            async row =>
            {
                if (!IsDue(row, clock.UtcNow))
                {
                    return; // moved on since it was selected
                }

                await ProcessAsync(row, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Loads the row and runs <paramref name="work"/> on it with the guarantees both entry points (the tick and the transfer job) share: it never throws out
    /// of a row (a concurrency conflict just means "someone else moved it"; any other failure is logged by type and counted as a failed attempt).</summary>
    private async Task GuardedAsync(Guid importId, Func<SESSION_RECORDING_IMPORT, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            var row = await imports.GetByIdAsync(importId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return;
            }

            await work(row).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Live recording import {ImportId} was changed concurrently; it will be picked up again next run.", importId);
            imports.ClearTracking();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Only the exception type: its message can carry provider ids, URLs or SQL.
            logger.LogError("Live recording import {ImportId} failed unexpectedly: {ExceptionType}.", importId, ex.GetType().Name);
            imports.ClearTracking();
            await RecordUnexpectedFailureAsync(importId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsDue(SESSION_RECORDING_IMPORT row, DateTime now) => row.STATUS switch
    {
        RecordingImportStatus.Waiting or RecordingImportStatus.Processing => row.NEXT_ATTEMPT_AT_UTC is { } next && next <= now,
        RecordingImportStatus.Transferring => row.LEASE_UNTIL_UTC is { } lease && lease <= now,
        _ => false,
    };

    /// <summary>Re-reads the session first (always): a deleted course, a cancelled class or an existing recording lesson ends the import as <c>Skipped</c> and returns
    /// <c>null</c>; otherwise the session's current facts.</summary>
    private async Task<LiveSessionContext?> ReadSessionOrSkipAsync(SESSION_RECORDING_IMPORT row, CancellationToken cancellationToken)
    {
        var context = (await schedule.GetSessionContextsAsync([row.SESSION_ID], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == row.SESSION_ID);

        if (context is null)
        {
            await SkipAsync(row, RecordingImportErrorCodes.SessionGone, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (context.Status == LiveSessionStatus.Cancelled)
        {
            await SkipAsync(row, RecordingImportErrorCodes.SessionCancelled, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (context.RecordingEpisodeId is not null)
        {
            await SkipAsync(row, RecordingImportErrorCodes.RecordingExists, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return context;
    }

    internal async Task ProcessAsync(SESSION_RECORDING_IMPORT row, CancellationToken cancellationToken)
    {
        var context = await ReadSessionOrSkipAsync(row, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return;
        }

        switch (row.STATUS)
        {
            case RecordingImportStatus.Waiting:
                await ProcessWaitingAsync(row, context, cancellationToken).ConfigureAwait(false);
                break;
            case RecordingImportStatus.Transferring:
                await ProcessExpiredLeaseAsync(row, context, cancellationToken).ConfigureAwait(false);
                break;
            case RecordingImportStatus.Processing:
                await ProcessProcessingAsync(row, context, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    // ---- The transfer (a background job per claimed import) ----------------------------------------

    /// <summary>
    /// The body of <see cref="LiveRecordingTransferJob"/>: the long Drive to video provider copy of one claimed import. Does nothing unless the row is still
    /// <c>Transferring</c> under a lease that has not run out (a row whose lease ran out is the tick's to reclaim; a row that already moved on was finished by someone
    /// else). Like the tick it never throws out of the row.
    /// </summary>
    public async Task RunTransferAsync(Guid importId, CancellationToken cancellationToken)
    {
        if (!Settings.Enabled || liveOptions.Value.Provider == LiveProviderMode.ManualOnly)
        {
            return; // switched off meanwhile: in-flight rows simply stop being processed
        }

        await GuardedAsync(
            importId,
            async row =>
            {
                if (row.STATUS != RecordingImportStatus.Transferring)
                {
                    return;
                }

                if (row.LEASE_UNTIL_UTC is not { } lease || lease <= clock.UtcNow)
                {
                    logger.LogInformation("Live recording import {ImportId}: the transfer job started after the lease ran out; the tick reclaims the row.", importId);
                    return;
                }

                var context = await ReadSessionOrSkipAsync(row, cancellationToken).ConfigureAwait(false);
                if (context is null)
                {
                    return;
                }

                var token = await googleAccounts.TryGetAccessTokenAsync(row.INSTRUCTOR_USER_ID, cancellationToken).ConfigureAwait(false);
                if (token.IsFailure)
                {
                    await HandleGoogleFailureAsync(row, context, token.Error, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await TransferAsync(row, context, token.Value, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Queues the claimed row's transfer job. If it cannot be queued the row is given back at once (a failed attempt and a backoff) instead of waiting out its
    /// lease for a job that does not exist.</summary>
    private async Task QueueTransferAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, CancellationToken cancellationToken)
    {
        try
        {
            transfers.Enqueue(row.SESSION_RECORDING_IMPORT_ID);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Live recording import {ImportId}: queueing the transfer failed ({ExceptionType}).", row.SESSION_RECORDING_IMPORT_ID, ex.GetType().Name);
            await TransientFailureAsync(row, context, RecordingImportErrorCodes.InternalError, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Waiting: find the recording ---------------------------------------------------------------

    private async Task ProcessWaitingAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The account must still be usable, a Workspace one, with the recording scopes — the instructor can have changed any of that since the class.
        var account = await googleAccounts.GetAccountAsync(row.INSTRUCTOR_USER_ID, cancellationToken).ConfigureAwait(false);
        if (account is { IsActive: true, AccountKind: GoogleAccountKind.Unknown })
        {
            await googleAccounts.TryResolveAccountKindAsync(account, cancellationToken).ConfigureAwait(false);
        }

        if (account is not { IsActive: true })
        {
            await NeedsReconnectAsync(row, context, RecordingImportErrorCodes.GoogleAccountUnavailable, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (account.AccountKind)
        {
            case GoogleAccountKind.Personal:
                await SkipAsync(row, RecordingImportErrorCodes.NotWorkspaceAccount, cancellationToken).ConfigureAwait(false);
                return;
            case GoogleAccountKind.Unknown:
                // The kind could not be read right now (no token, Google hiccup): a transient problem, not a verdict.
                await TransientFailureAsync(row, context, RecordingImportErrorCodes.GoogleTransient, cancellationToken).ConfigureAwait(false);
                return;
        }

        if (!account.HasRecordingScopes)
        {
            await NeedsReconnectAsync(row, context, RecordingImportErrorCodes.RecordingScopeMissing, cancellationToken).ConfigureAwait(false);
            return;
        }

        var meetingCode = await ResolveMeetingCodeAsync(row.SESSION_ID, cancellationToken).ConfigureAwait(false);
        if (meetingCode is null)
        {
            await SkipAsync(row, RecordingImportErrorCodes.NoGoogleMeeting, cancellationToken).ConfigureAwait(false);
            return;
        }

        var token = await googleAccounts.TryGetAccessTokenAsync(row.INSTRUCTOR_USER_ID, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            await HandleGoogleFailureAsync(row, context, token.Error, cancellationToken).ConfigureAwait(false);
            return;
        }

        var found = await recordings
            .FindRecordingsAsync(
                token.Value,
                meetingCode,
                context.EndsAtUtc - SearchLookBehind,
                context.EndsAtUtc.AddHours(Settings.SearchWindowHours),
                cancellationToken)
            .ConfigureAwait(false);

        // "Google has no record of that conference (yet)" is an answer, not a failure: keep waiting.
        IReadOnlyList<MeetRecording> candidates;
        if (found.IsSuccess)
        {
            candidates = found.Value;
        }
        else if (found.Error.Code == GoogleErrors.NotFoundCode)
        {
            candidates = [];
        }
        else
        {
            await HandleGoogleFailureAsync(row, context, found.Error, cancellationToken).ConfigureAwait(false);
            return;
        }

        var best = ChooseRecording(candidates);
        if (best is null)
        {
            if (now >= row.SEARCH_UNTIL_UTC)
            {
                await NoRecordingAsync(row, RecordingImportErrorCodes.NoRecordingFound, cancellationToken).ConfigureAwait(false);
                return;
            }

            var firstSearchAt = context.EndsAtUtc.AddMinutes(Settings.FirstSearchDelayMinutes);
            row.ScheduleNextSearch(now + RecordingImportBackoff.ForSearch(now - firstSearchAt));
            await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Claim the transfer (status + lease, saved with the version we read): a concurrent run loses the claim, so only the winner queues the copy - once.
        // The copy itself is a background job (LiveRecordingTransferJob), never part of this tick.
        row.BeginTransfer(best.RecordingName, best.DriveFileId!, now.AddMinutes(Settings.TransferLeaseMinutes));
        await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await QueueTransferAsync(row, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Among the recordings Google has finished (<c>FileGenerated</c> with a Drive file), the longest — a class that was recorded in several takes
    /// is worth the longest one; a tie goes to the earliest start. <c>null</c> when there is none yet (nothing, or still <c>Started</c>/<c>Ended</c>).</summary>
    internal static MeetRecording? ChooseRecording(IReadOnlyList<MeetRecording> candidates) =>
        candidates
            .Where(r => r.State == MeetRecordingState.FileGenerated && !string.IsNullOrEmpty(r.DriveFileId))
            .OrderByDescending(r => r.StartedAtUtc is { } start && r.EndedAtUtc is { } end ? end - start : TimeSpan.Zero)
            .ThenBy(r => r.StartedAtUtc ?? DateTime.MaxValue)
            .FirstOrDefault();

    /// <summary>The Meet code of the session's room, or <c>null</c> when the room is not a platform-made Google room (or its link cannot be read). With the fake
    /// providers (<c>Live:Provider=Logging</c>) the room links are not Meet links, so a fixed stand-in code is used.</summary>
    private async Task<string?> ResolveMeetingCodeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var room = (await meetings.GetBySessionIdsAsync([sessionId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (!RecordingImportRules.HasGoogleRoom(room))
        {
            return null;
        }

        if (liveOptions.Value.Provider == LiveProviderMode.Logging)
        {
            return GoogleMeetCode.DevCode;
        }

        return GoogleMeetCode.TryParse(meetingService.RevealUrl(room!));
    }

    // ---- Transferring: Drive -> video provider -----------------------------------------------------

    /// <summary>The lease of a <c>Transferring</c> row ran out: the transfer job that held it died, hung or was never queued. Counts as a failed attempt, takes a fresh
    /// lease and queues the transfer again (the tick itself never copies).</summary>
    private async Task ProcessExpiredLeaseAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, CancellationToken cancellationToken)
    {
        // Note: the Drive -> provider copy is atomic (IMediaIngestContract records the asset only when the copy succeeded), so an expired lease never has a
        // MEDIA_ASSET_ID to clean up here; a job that died mid-copy leaves at most an unfinished asset in the instructor's own library.
        var failed = row.ReclaimExpiredLease(clock.UtcNow.AddMinutes(Settings.TransferLeaseMinutes), Settings.MaxAttempts, clock);
        if (failed)
        {
            await FinishAsync(row, context, FinishKind.Failed, cancellationToken).ConfigureAwait(false);
            return;
        }

        await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await QueueTransferAsync(row, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task TransferAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, string accessToken, CancellationToken cancellationToken)
    {
        var importId = row.SESSION_RECORDING_IMPORT_ID;
        var maxBytes = Settings.MaxFileSizeBytes;

        var open = await recordings.OpenDownloadAsync(accessToken, row.GOOGLE_FILE_ID!, cancellationToken).ConfigureAwait(false);
        if (open.IsFailure)
        {
            if (open.Error.Code == GoogleErrors.NotFoundCode)
            {
                // The file is gone from Drive (deleted, or never was ours): nothing to import.
                await NoRecordingAsync(row, RecordingImportErrorCodes.DriveFileNotFound, cancellationToken).ConfigureAwait(false);
                return;
            }

            await HandleGoogleFailureAsync(row, context, open.Error, cancellationToken).ConfigureAwait(false);
            return;
        }

        SizeLimitedReadStream? limited = null;
        Result<Guid> ingested;
        try
        {
            using var download = open.Value;

            if (download.ContentLength is { } announced && announced > maxBytes)
            {
                await FailAsync(row, context, RecordingImportErrorCodes.FileTooLarge, cancellationToken).ConfigureAwait(false);
                return;
            }

            limited = new SizeLimitedReadStream(download.Content, maxBytes);
            ingested = await ingest
                .IngestAsync(row.INSTRUCTOR_USER_ID, $"บันทึก: {context.Title}", limited, download.ContentLength, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A network or I/O failure while streaming (or an oversized file that announced no length). Only the type is logged.
            logger.LogWarning("Live recording import {ImportId}: the copy to the video provider broke off ({ExceptionType}).", importId, ex.GetType().Name);
            await RecordTransferFailureAsync(importId, limited?.Exceeded == true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (ingested.IsFailure)
        {
            logger.LogWarning("Live recording import {ImportId}: the video provider did not take the file ({ErrorCode}).", importId, ingested.Error.Code);
            await RecordTransferFailureAsync(importId, limited?.Exceeded == true, cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = clock.UtcNow;
        row.MarkProcessing(ingested.Value, now + ProcessingPollInterval, now + TranscodeTimeout);
        await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The copy failed after the file was opened. The shared context may hold half-finished changes of the Media module, so it is wiped and the row re-read
    /// before the failure is recorded on it.</summary>
    private async Task RecordTransferFailureAsync(Guid importId, bool tooLarge, CancellationToken cancellationToken)
    {
        imports.ClearTracking();

        var row = await imports.GetByIdAsync(importId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.IsTerminal)
        {
            return;
        }

        var context = (await schedule.GetSessionContextsAsync([row.SESSION_ID], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == row.SESSION_ID);

        if (tooLarge)
        {
            await FailAsync(row, context, RecordingImportErrorCodes.FileTooLarge, cancellationToken).ConfigureAwait(false);
            return;
        }

        await TransientFailureAsync(row, context, RecordingImportErrorCodes.IngestFailed, cancellationToken).ConfigureAwait(false);
    }

    // ---- Processing: wait for the transcode, then attach -------------------------------------------

    private async Task ProcessProcessingAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (row.MEDIA_ASSET_ID is not { } assetId)
        {
            await FailAsync(row, context, RecordingImportErrorCodes.AssetMissing, cancellationToken).ConfigureAwait(false);
            return;
        }

        var summary = await mediaAssets.GetAssetSummaryAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            await FailAsync(row, context, RecordingImportErrorCodes.AssetMissing, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(summary.Status, StatusFailed, StatusComparison))
        {
            await FailAsync(row, context, RecordingImportErrorCodes.TranscodeFailed, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(summary.Status, StatusReady, StatusComparison) || summary.DurationSeconds is not > 0)
        {
            if (row.LEASE_UNTIL_UTC is { } deadline && now >= deadline)
            {
                await FailAsync(row, context, RecordingImportErrorCodes.TranscodeTimeout, cancellationToken).ConfigureAwait(false);
                return;
            }

            row.ScheduleNextProcessingCheck(now + ProcessingPollInterval);
            await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // The lesson title is the attach handler's own default ("บันทึก: {session title}").
        var attached = await attacher
            .AttachAsync(row.INSTRUCTOR_USER_ID, row.COURSE_ID, row.SESSION_ID, assetId, episodeTitle: null, cancellationToken)
            .ConfigureAwait(false);

        if (attached.IsSuccess)
        {
            row.MarkAttached(attached.Value.EpisodeId, clock);
            await FinishAsync(row, context, FinishKind.Attached, cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandleAttachFailureAsync(row, context, attached.Error, cancellationToken).ConfigureAwait(false);
    }

    private const StringComparison StatusComparison = StringComparison.Ordinal;

    private async Task HandleAttachFailureAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, DomainError error, CancellationToken cancellationToken)
    {
        logger.LogWarning("Live recording import {ImportId}: attaching the recording was refused ({ErrorCode}/{Reason}).", row.SESSION_RECORDING_IMPORT_ID, error.Code, error.Reason ?? "-");

        switch (error.Reason)
        {
            case LiveRecordingAttachReasons.AssetNotReady:
                // The provider's summary and the attach rules briefly disagreed: look again soon (the transcode deadline still bounds it).
                row.ScheduleNextProcessingCheck(clock.UtcNow + ProcessingPollInterval);
                await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;

            case LiveRecordingAttachReasons.AssetInUse:
                // The asset already is a lesson of the course. If the session has a recording now (a manual upload, or our own attach whose save was lost), that is done.
                var latest = (await schedule.GetSessionContextsAsync([row.SESSION_ID], cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(c => c.SessionId == row.SESSION_ID);
                if (latest?.RecordingEpisodeId is not null)
                {
                    await SkipAsync(row, RecordingImportErrorCodes.RecordingExists, cancellationToken).ConfigureAwait(false);
                    return;
                }

                break;
        }

        switch (error.Code)
        {
            case "not_found":
                // The course or the session was deleted meanwhile.
                await SkipAsync(row, RecordingImportErrorCodes.SessionGone, cancellationToken).ConfigureAwait(false);
                return;

            case "conflict" when error.Reason is null:
                // A concurrent edit of the course (no stable reason): worth trying again.
                await TransientFailureAsync(row, context, RecordingImportErrorCodes.AttachFailed, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await FailAsync(row, context, RecordingImportErrorCodes.AttachFailed, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    // ---- Google failures ---------------------------------------------------------------------------

    private async Task HandleGoogleFailureAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext context, DomainError error, CancellationToken cancellationToken)
    {
        switch (error.Code)
        {
            case GoogleErrors.UnauthorizedCode:
                // The token is dead (InstructorGoogleAccountService already revoked the account and alerted the instructor when it was the refresh that failed).
                await NeedsReconnectAsync(row, context, RecordingImportErrorCodes.InvalidGrant, cancellationToken).ConfigureAwait(false);
                return;

            case GoogleErrors.ForbiddenCode:
                await NeedsReconnectAsync(row, context, RecordingImportErrorCodes.RecordingScopeMissing, cancellationToken).ConfigureAwait(false);
                return;

            case InstructorGoogleAccountService.NotConnectedCode:
                await NeedsReconnectAsync(row, context, RecordingImportErrorCodes.GoogleAccountUnavailable, cancellationToken).ConfigureAwait(false);
                return;

            case GoogleErrors.BadRequestCode:
                await FailAsync(row, context, RecordingImportErrorCodes.GoogleBadRequest, cancellationToken).ConfigureAwait(false);
                return;

            default:
                await TransientFailureAsync(row, context, TransientCodeFor(error), cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>The short stored code for a retry-worthy Google error. Never the provider's message.</summary>
    internal static string TransientCodeFor(DomainError error) => error.Code switch
    {
        GoogleErrors.RateLimitedCode => RecordingImportErrorCodes.GoogleRateLimited,
        // Platform-side credential problems (Google rejects our OAuth client, or we cannot decrypt the stored token) share one stored code.
        GoogleErrors.TransientCode when error.Reason is "invalid_client" or InstructorGoogleAccountService.CredentialUnreadableReason
            => RecordingImportErrorCodes.GoogleClientMisconfigured,
        GoogleErrors.NotConfiguredCode => RecordingImportErrorCodes.GoogleClientMisconfigured,
        _ => RecordingImportErrorCodes.GoogleTransient,
    };

    // ---- State changes (each is one SaveChangesAsync) ----------------------------------------------

    private enum FinishKind
    {
        Attached,
        Failed,
        NeedsReconnect,
    }

    private Task SkipAsync(SESSION_RECORDING_IMPORT row, string code, CancellationToken cancellationToken)
    {
        row.MarkSkipped(code, clock);
        return imports.SaveChangesAsync(cancellationToken);
    }

    private Task NeedsReconnectAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext? context, string code, CancellationToken cancellationToken)
    {
        row.MarkNeedsReconnect(code, clock);
        return FinishAsync(row, context, FinishKind.NeedsReconnect, cancellationToken);
    }

    /// <summary>No alert: a class nobody recorded is not an error, and the session row already says so (with the upload and retry buttons). The contract e-mails the
    /// instructor only on <c>Failed</c>, <c>NeedsReconnect</c> and success.</summary>
    private Task NoRecordingAsync(SESSION_RECORDING_IMPORT row, string code, CancellationToken cancellationToken)
    {
        row.MarkNoRecording(code, clock);
        return imports.SaveChangesAsync(cancellationToken);
    }

    private Task FailAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext? context, string code, CancellationToken cancellationToken)
    {
        row.MarkFailed(code, clock);
        return FinishAsync(row, context, FinishKind.Failed, cancellationToken);
    }

    /// <summary>A retry-worthy failure: the row stays alive with a backoff and one more attempt; at the configured maximum it ends <c>Failed</c> (and the instructor is told).</summary>
    private async Task TransientFailureAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext? context, string code, CancellationToken cancellationToken)
    {
        var failed = row.RecordTransientFailure(code, RecordingImportBackoff.ForFailure(row.ATTEMPTS + 1), Settings.MaxAttempts, clock);
        if (failed)
        {
            logger.LogWarning("Live recording import {ImportId} gave up after {Attempts} attempts ({ErrorCode}).", row.SESSION_RECORDING_IMPORT_ID, row.ATTEMPTS, code);
            await FinishAsync(row, context, FinishKind.Failed, cancellationToken).ConfigureAwait(false);
            return;
        }

        await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stages the one e-mail/notification that goes with a row reaching an outcome the instructor cares about, then saves it together with the state change —
    /// so the alert exists if and only if the state change does.</summary>
    private async Task FinishAsync(SESSION_RECORDING_IMPORT row, LiveSessionContext? context, FinishKind kind, CancellationToken cancellationToken)
    {
        if (context is not null)
        {
            switch (kind)
            {
                case FinishKind.Attached:
                    await alerts.RecordingImportedAsync(row.INSTRUCTOR_USER_ID, context.SessionId, context.Title, context.CourseTitle, cancellationToken).ConfigureAwait(false);
                    break;
                case FinishKind.Failed:
                    await alerts.RecordingImportFailedAsync(row.INSTRUCTOR_USER_ID, context.SessionId, context.Title, context.CourseTitle, cancellationToken).ConfigureAwait(false);
                    break;
                case FinishKind.NeedsReconnect:
                    await alerts.RecordingNeedsReconnectAsync(row.INSTRUCTOR_USER_ID, context.SessionId, context.Title, context.CourseTitle, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        await imports.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An unexpected exception must not make a poisoned row spin every run forever: count it as a failed attempt (best effort).</summary>
    private async Task RecordUnexpectedFailureAsync(Guid importId, CancellationToken cancellationToken)
    {
        try
        {
            var row = await imports.GetByIdAsync(importId, cancellationToken).ConfigureAwait(false);
            if (row is null || row.IsTerminal)
            {
                return;
            }

            var context = (await schedule.GetSessionContextsAsync([row.SESSION_ID], cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(c => c.SessionId == row.SESSION_ID);

            await TransientFailureAsync(row, context, RecordingImportErrorCodes.InternalError, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not record the failed attempt for live recording import {ImportId} ({ExceptionType}).", importId, ex.GetType().Name);
            imports.ClearTracking();
        }
    }
}
