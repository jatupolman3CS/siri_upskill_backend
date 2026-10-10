using Microsoft.EntityFrameworkCore;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.UnitTests.Live;

/// <summary>In-memory <see cref="ISessionRecordingImportRepository"/>: tracked entities are the very objects in the list, so a mutation is "tracked" at once; a save only counts.</summary>
internal sealed class InMemoryRecordingImportRepository : ISessionRecordingImportRepository
{
    public List<SESSION_RECORDING_IMPORT> Imports { get; } = [];

    public int SaveCount { get; private set; }

    public int ClearTrackingCount { get; private set; }

    /// <summary>Set to make the next <see cref="SaveChangesAsync"/> throw (then clears itself).</summary>
    public Exception? ThrowOnNextSave { get; set; }

    public Task<SESSION_RECORDING_IMPORT?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Imports.FirstOrDefault(i => i.SESSION_ID == sessionId));

    public Task<SESSION_RECORDING_IMPORT?> GetByIdAsync(Guid importId, CancellationToken cancellationToken) =>
        Task.FromResult(Imports.FirstOrDefault(i => i.SESSION_RECORDING_IMPORT_ID == importId));

    public Task<IReadOnlyList<SESSION_RECORDING_IMPORT>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_RECORDING_IMPORT>>(Imports.Where(i => sessionIds.Contains(i.SESSION_ID)).ToList());

    public Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(Imports.Where(i => sessionIds.Contains(i.SESSION_ID)).Select(i => i.SESSION_ID).ToHashSet());

    public Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Imports
            .Where(i =>
                (i.STATUS is RecordingImportStatus.Waiting or RecordingImportStatus.Processing && i.NEXT_ATTEMPT_AT_UTC is { } next && next <= nowUtc)
                || (i.STATUS == RecordingImportStatus.Transferring && i.LEASE_UNTIL_UTC is { } lease && lease <= nowUtc))
            .OrderBy(i => i.NEXT_ATTEMPT_AT_UTC ?? i.LEASE_UNTIL_UTC)
            .Take(take)
            .Select(i => i.SESSION_RECORDING_IMPORT_ID)
            .ToList());

    public void Add(SESSION_RECORDING_IMPORT import) => Imports.Add(import);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnNextSave is { } exception)
        {
            ThrowOnNextSave = null;
            throw exception;
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public void ClearTracking() => ClearTrackingCount++;
}

/// <summary>Fake Google Meet/Drive: scripted answers, every call recorded. Download streams are produced fresh per call.</summary>
internal sealed class FakeRecordingProvider : IGoogleMeetRecordingProvider
{
    public Func<Result<IReadOnlyList<MeetRecording>>> OnFind { get; set; } = () => Result.Success<IReadOnlyList<MeetRecording>>([]);

    public Func<Result<MeetRecordingDownload>> OnOpen { get; set; } = () => Result.Success(new MeetRecordingDownload(new MemoryStream(new byte[1024]), 1024, "rec.mp4", owner: null));

    /// <summary>Throws instead of answering (a provider bug / unexpected exception).</summary>
    public Exception? ThrowOnFind { get; set; }

    public List<(string AccessToken, string MeetingCode, DateTime NotBeforeUtc, DateTime NotAfterUtc)> FindCalls { get; } = [];

    public List<(string AccessToken, string DriveFileId)> OpenCalls { get; } = [];

    public Task<Result<IReadOnlyList<MeetRecording>>> FindRecordingsAsync(
        string accessToken, string meetingCode, DateTime notBeforeUtc, DateTime notAfterUtc, CancellationToken ct)
    {
        FindCalls.Add((accessToken, meetingCode, notBeforeUtc, notAfterUtc));
        if (ThrowOnFind is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(OnFind());
    }

    public Task<Result<MeetRecordingDownload>> OpenDownloadAsync(string accessToken, string driveFileId, CancellationToken ct)
    {
        OpenCalls.Add((accessToken, driveFileId));
        return Task.FromResult(OnOpen());
    }
}

/// <summary>Fake media ingest: by default reads the whole stream (like the real upload would) and returns a new asset id.</summary>
internal sealed class FakeMediaIngest : IMediaIngestContract
{
    public Func<Stream, Task<Result<Guid>>>? Handler { get; set; }

    public List<(Guid OwnerUserId, string Title, long? ContentLength)> Calls { get; } = [];

    public long BytesRead { get; private set; }

    public Guid LastAssetId { get; private set; }

    public async Task<Result<Guid>> IngestAsync(Guid ownerUserId, string title, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        Calls.Add((ownerUserId, title, contentLength));

        if (Handler is not null)
        {
            return await Handler(content);
        }

        var buffer = new byte[8192];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            BytesRead += read;
        }

        LastAssetId = Guid.NewGuid();
        return Result.Success(LastAssetId);
    }
}

internal sealed class FakeMediaAssets : IMediaAssetContract
{
    public Dictionary<Guid, MediaAssetSummary> Assets { get; } = [];

    public Task<MediaAssetSummary?> GetAssetSummaryAsync(Guid mediaAssetId, CancellationToken cancellationToken) =>
        Task.FromResult(Assets.GetValueOrDefault(mediaAssetId));
}

/// <summary>Fake Catalog side of the import: lists ended sessions and attaches (or refuses to).</summary>
internal sealed class FakeRecordingAttacher : ILiveRecordingAttacher
{
    public List<EndedLiveSession> Ended { get; } = [];

    public Func<Result<AttachedLiveRecording>> OnAttach { get; set; } = () => Result.Success(new AttachedLiveRecording(Guid.NewGuid()));

    public List<(DateTime After, DateTime Before, int Limit)> ListCalls { get; } = [];

    public List<(Guid InstructorUserId, Guid CourseId, Guid SessionId, Guid AssetId, string? Title)> AttachCalls { get; } = [];

    /// <summary>Set to make an attach throw (a Catalog bug / unexpected exception).</summary>
    public Exception? ThrowOnAttach { get; set; }

    public Task<IReadOnlyList<EndedLiveSession>> ListEndedAsync(DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        ListCalls.Add((endedAfterUtc, endedBeforeUtc, limit));
        return Task.FromResult<IReadOnlyList<EndedLiveSession>>(
            Ended.Where(s => s.EndsAtUtc >= endedAfterUtc && s.EndsAtUtc < endedBeforeUtc).OrderBy(s => s.EndsAtUtc).Take(limit).ToList());
    }

    public List<(IReadOnlyCollection<Guid> InstructorUserIds, DateTime After, DateTime Before, int Limit)> ListByInstructorCalls { get; } = [];

    /// <summary>Like the real implementation: the instructor filter is applied BEFORE the limit; oldest first by end time then id; inclusive start, exclusive end.</summary>
    public Task<IReadOnlyList<EndedLiveSession>> ListEndedByInstructorsAsync(
        IReadOnlyCollection<Guid> instructorUserIds, DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        ListByInstructorCalls.Add((instructorUserIds.ToArray(), endedAfterUtc, endedBeforeUtc, limit));
        return Task.FromResult<IReadOnlyList<EndedLiveSession>>(
            Ended.Where(s => instructorUserIds.Contains(s.InstructorUserId) && s.EndsAtUtc >= endedAfterUtc && s.EndsAtUtc < endedBeforeUtc)
                .OrderBy(s => s.EndsAtUtc).ThenBy(s => s.SessionId)
                .Take(limit)
                .ToList());
    }

    public Task<EndedLiveSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Ended.FirstOrDefault(s => s.SessionId == sessionId));

    public Task<Result<AttachedLiveRecording>> AttachAsync(
        Guid instructorUserId, Guid courseId, Guid sessionId, Guid mediaAssetId, string? episodeTitle, CancellationToken cancellationToken)
    {
        AttachCalls.Add((instructorUserId, courseId, sessionId, mediaAssetId, episodeTitle));
        if (ThrowOnAttach is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(OnAttach());
    }
}

/// <summary>Records the transfers the tick queues (what Hangfire would run as separate background jobs).</summary>
internal sealed class FakeTransferScheduler : IRecordingTransferScheduler
{
    /// <summary>Every import id ever queued, in order.</summary>
    public List<Guid> Enqueued { get; } = [];

    /// <summary>Queued and not yet run by the test harness.</summary>
    public Queue<Guid> Pending { get; } = new();

    /// <summary>Set to make queueing fail (Hangfire storage down).</summary>
    public Exception? ThrowOnEnqueue { get; set; }

    public void Enqueue(Guid importId)
    {
        if (ThrowOnEnqueue is { } exception)
        {
            throw exception;
        }

        Enqueued.Add(importId);
        Pending.Enqueue(importId);
    }
}

/// <summary>
/// Wires <see cref="LiveRecordingImportJob"/> over the fakes. Time is a <see cref="FakeClock"/> (default <see cref="LiveTestData.Now"/>); the room link of a session is a
/// real AES-GCM ciphertext made by the same protector the services use. The default scenario is the happy one: a Workspace instructor with recording access whose
/// one-hour class ended an hour ago in a platform-made Google room.
/// </summary>
internal sealed class RecordingJobHarness
{
    public const string RoomUrl = "https://meet.google.com/abc-defg-hij";
    public const string MeetingCode = "abc-defg-hij";
    public const string DriveFileId = "DRIVE-FILE-ID-SECRET";
    public const string RecordingName = "conferenceRecords/CONF-SECRET/recordings/REC-SECRET";

    public static readonly Guid InstructorId = Guid.NewGuid();

    public FakeClock Clock { get; } = new(LiveTestData.Now);

    public InMemoryRecordingImportRepository Imports { get; } = new();

    public InMemorySessionMeetingRepository Meetings { get; } = new();

    public InMemoryAccountRepository Accounts { get; } = new();

    /// <summary>Replaces the account repository the jobs and services are built with (default: <see cref="Accounts"/>). Lets a test give each simulated scope its own
    /// view of a shared, row-versioned account row.</summary>
    public IInstructorGoogleAccountRepository? AccountRepository { get; set; }

    public FakeGoogleOAuth OAuth { get; } = new();

    public FakeSchedule Schedule { get; } = new();

    public RecordingAlertSender Alerts { get; } = new();

    public FakeRecordingProvider Recordings { get; } = new();

    public FakeMediaIngest Ingest { get; } = new();

    public FakeMediaAssets Assets { get; } = new();

    public FakeRecordingAttacher Attacher { get; } = new();

    public FakeTransferScheduler Scheduler { get; } = new();

    public ISensitiveDataProtector Protector { get; } = LiveTestData.Protector();

    public ListLogger<LiveRecordingImportJob> JobLog { get; } = new();

    public ListLogger<InstructorGoogleAccountService> ServiceLog { get; } = new();

    public LiveProviderMode Mode { get; set; } = LiveProviderMode.GoogleMeet;

    public Action<LiveRecordingAutoImportOptions>? ConfigureAutoImport { get; set; }

    public bool Enabled { get; set; } = true;

    public Microsoft.Extensions.Options.IOptions<LiveOptions> Options() =>
        LiveTestData.OptionsOf(o =>
        {
            o.Provider = Mode;
            o.Recording.AutoImport.Enabled = Enabled;
            ConfigureAutoImport?.Invoke(o.Recording.AutoImport);
        });

    public LiveRecordingImportJob Job()
    {
        var options = Options();
        var googleAccounts = new InstructorGoogleAccountService(
            AccountRepository ?? Accounts,
            Meetings,
            OAuth,
            new FakeStateStore(),
            Protector,
            Schedule,
            Alerts,
            Clock,
            options,
            Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()),
            ServiceLog);

        var meetingService = new SessionMeetingService(
            Meetings, Schedule, new FakeCatalog(), new MeetingLinkValidator(options), Protector, Clock, new ListLogger<SessionMeetingService>());

        return new LiveRecordingImportJob(
            Imports,
            Meetings,
            Schedule,
            Attacher,
            Recordings,
            Ingest,
            Assets,
            googleAccounts,
            meetingService,
            Alerts,
            Scheduler,
            Clock,
            options,
            JobLog);
    }

    public LiveRecordingTransferJob TransferJob() => new(Job());

    public RecordingImportService Service() => new(
        Imports, Accounts, Meetings, Schedule, Clock, Options(), new ListLogger<RecordingImportService>());

    /// <summary>One recurring tick, nothing else: what Hangfire runs every five minutes. Transfers it queues stay queued.</summary>
    public Task RunTickAsync() => Job().RunAsync(CancellationToken.None);

    /// <summary>A tick followed by every background transfer job it queued (the way a Hangfire server runs them) - the usual "one pass of the system".</summary>
    public async Task RunAsync()
    {
        await RunTickAsync();
        await DrainTransfersAsync();
    }

    /// <summary>Runs every queued transfer job, one after the other.</summary>
    public async Task DrainTransfersAsync()
    {
        while (Scheduler.Pending.Count > 0)
        {
            await TransferJob().RunAsync(Scheduler.Pending.Dequeue(), CancellationToken.None);
        }
    }

    // ---- Builders -------------------------------------------------------------------------------------

    public INSTRUCTOR_GOOGLE_ACCOUNT Account(string? hostedDomain = "school.example.test", string? scopes = null, bool checkedKind = true)
    {
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            InstructorId,
            "sub",
            "teacher@school.example.test",
            Protector.Encrypt("refresh-token-1"),
            scopes ?? string.Join(' ', GoogleScopes.CalendarEventsOwned, GoogleScopes.MeetSpaceReadonly, GoogleScopes.DriveMeetReadonly),
            Clock,
            hostedDomain);

        if (!checkedKind)
        {
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.ACCOUNT_KIND_CHECKED_AT_UTC))!
                .GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
            typeof(INSTRUCTOR_GOOGLE_ACCOUNT).GetProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.HOSTED_DOMAIN))!
                .GetSetMethod(nonPublic: true)!.Invoke(account, [null]);
        }

        Accounts.Accounts.Add(account);
        return account;
    }

    /// <summary>A class that ended <paramref name="endedAgo"/> ago (default an hour), with a platform-made Google room unless <paramref name="withRoom"/> is false.</summary>
    public LiveSessionContext Session(
        TimeSpan? endedAgo = null,
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        Guid? recordingEpisodeId = null,
        bool withRoom = true,
        MeetingProvider roomProvider = MeetingProvider.GoogleMeet,
        string title = "คาบทดสอบ",
        string roomUrl = RoomUrl,
        Guid? instructorUserId = null)
    {
        var teacher = instructorUserId ?? InstructorId;
        var ends = LiveTestData.Now - (endedAgo ?? TimeSpan.FromHours(1));
        var context = LiveTestData.Context(
            instructorUserId: teacher,
            status: status,
            startsAtUtc: ends.AddHours(-1),
            endsAtUtc: ends,
            title: title) with { RecordingEpisodeId = recordingEpisodeId };

        Schedule.Contexts.Add(context);
        Attacher.Ended.Add(new EndedLiveSession(
            context.SessionId,
            context.CourseId,
            teacher,
            context.Title,
            context.StartsAtUtc,
            context.EndsAtUtc,
            HasRecording: recordingEpisodeId is not null,
            IsCancelled: status == LiveSessionStatus.Cancelled));

        if (withRoom)
        {
            var meeting = SESSION_MEETING.Stage(context.SessionId);
            if (roomProvider == MeetingProvider.Manual)
            {
                meeting.SetManualLink(Protector.Encrypt(roomUrl));
            }
            else
            {
                meeting.RecordGoogleSynced(roomProvider, "evt-1", Protector.Encrypt(roomUrl), accountId: roomProvider == MeetingProvider.GoogleMeet ? Guid.NewGuid() : null, Clock);
            }

            Meetings.Meetings.Add(meeting);
        }

        return context;
    }

    /// <summary>An import that is due now (<c>Waiting</c>, first search at the class end plus the default delay).</summary>
    public SESSION_RECORDING_IMPORT Import(LiveSessionContext context, DateTime? firstSearchAtUtc = null)
    {
        var import = SESSION_RECORDING_IMPORT.Create(
            context.SessionId,
            context.CourseId,
            InstructorId,
            firstSearchAtUtc ?? context.EndsAtUtc.AddMinutes(10),
            context.EndsAtUtc.AddHours(12));
        Imports.Imports.Add(import);
        return import;
    }

    public static MeetRecording Finished(DateTime start, TimeSpan length, string? fileId = DriveFileId, string name = RecordingName) =>
        new(name, MeetRecordingState.FileGenerated, start, start + length, fileId);

    public void GoogleHasRecording(params MeetRecording[] found) =>
        Recordings.OnFind = () => Result.Success<IReadOnlyList<MeetRecording>>(found);

    public static void Force<T>(object entity, string property, T value) =>
        entity.GetType().GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(entity, [value]);

    public static DbUpdateConcurrencyException Conflict() => new("conflict");
}
