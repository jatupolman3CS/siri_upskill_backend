using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

// Fakes and a harness for the P11-05 learner/instructor/join-gate tests. Everything is in memory and records what it was asked to do.

/// <summary>A schedule reader that implements the full P11-03 query surface with the same semantics as Catalog's real one (overlap windows, include-cancelled,
/// instructor paging) so the services can be tested against it.</summary>
internal sealed class JoinFakeSchedule : ILiveScheduleReader
{
    public List<LiveSessionContext> Contexts { get; } = [];

    public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionInfo>>(Contexts
            .Where(c => c.CourseId == courseId)
            .OrderBy(c => c.StartsAtUtc)
            .Select(c => new LiveSessionInfo(c.SessionId, c.CourseId, c.Title, c.StartsAtUtc, c.EndsAtUtc, c.Status, c.RecordingEpisodeId))
            .ToList());

    public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>(Contexts.Where(c => sessionIds.Contains(c.SessionId)).ToList());

    public Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds, DateTime? fromUtc, DateTime? toUtc, bool includeCancelled, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>(Contexts
            .Where(c => courseIds.Contains(c.CourseId)
                && (fromUtc is null || c.EndsAtUtc > fromUtc)
                && (toUtc is null || c.StartsAtUtc < toUtc)
                && (includeCancelled || c.Status == LiveSessionStatus.Scheduled))
            .OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId)
            .Take(LiveScheduleLimits.MaxWindowItems)
            .ToList());

    public Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(
        Guid instructorUserId, DateTime? fromUtc, DateTime? toUtc, bool includeCancelled, bool newestFirst, int skip, int take, CancellationToken cancellationToken)
    {
        var filtered = Contexts
            .Where(c => c.InstructorUserId == instructorUserId
                && (fromUtc is null || c.EndsAtUtc > fromUtc)
                && (toUtc is null || c.StartsAtUtc < toUtc)
                && (includeCancelled || c.Status == LiveSessionStatus.Scheduled))
            .ToList();

        var ordered = (newestFirst
                ? filtered.OrderByDescending(c => c.StartsAtUtc).ThenByDescending(c => c.SessionId)
                : filtered.OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId))
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, LiveScheduleLimits.MaxPageSize))
            .ToList();

        return Task.FromResult(new LiveSessionContextPage(ordered, filtered.Count));
    }
}

/// <summary>Enrollment facts as the Learning contract reports them: only <b>active, unexpired</b> pairs are listed here — a lapsed or revoked enrollment is simply absent.</summary>
internal sealed class JoinFakeLearning : ILearningAccessContract
{
    public HashSet<(Guid UserId, Guid CourseId)> ActiveEnrollments { get; } = [];

    public int HasActiveEnrollmentCalls { get; private set; }

    public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        HasActiveEnrollmentCalls++;
        return Task.FromResult(ActiveEnrollments.Contains((userId, courseId)));
    }

    public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> GetActiveEnrolledCourseIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(ActiveEnrollments.Where(e => e.UserId == userId).Select(e => e.CourseId).ToList());

    public Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(ActiveEnrollments.Where(e => e.CourseId == courseId).Select(e => e.UserId).ToHashSet());
}

/// <summary>An append-only join-log store that separates "staged" from "committed", so tests can tell whether a row was saved before a link was returned.</summary>
internal sealed class InMemoryJoinLogRepository : ISessionJoinLogRepository
{
    public List<SESSION_JOIN_LOG> Staged { get; } = [];

    public List<SESSION_JOIN_LOG> Committed { get; } = [];

    public int SaveCount { get; private set; }

    /// <summary>Set to make <see cref="SaveChangesAsync"/> throw (a failing commit).</summary>
    public Exception? ThrowOnSave { get; set; }

    public void Add(SESSION_JOIN_LOG log) => Staged.Add(log);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnSave is { } exception)
        {
            throw exception;
        }

        SaveCount++;
        Committed.AddRange(Staged);
        Staged.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetJoinedLearnerIdsAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Committed
            .Where(l => l.SESSION_ID == sessionId && l.ROLE == LiveParticipantRole.Learner)
            .Select(l => l.USER_ID)
            .Distinct()
            .ToList());

    public Task<IReadOnlyDictionary<Guid, LearnerJoinSummary>> GetLearnerJoinSummariesAsync(
        Guid sessionId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, LearnerJoinSummary>>(Committed
            .Where(l => l.SESSION_ID == sessionId && l.ROLE == LiveParticipantRole.Learner && userIds.Contains(l.USER_ID))
            .GroupBy(l => l.USER_ID)
            .ToDictionary(g => g.Key, g => new LearnerJoinSummary(g.Min(l => l.JOINED_AT_UTC), g.Count())));
}

internal sealed class FakeInviteReader : ISessionInviteReader
{
    public Dictionary<(Guid SessionId, Guid UserId), (InviteStatus Status, LiveParticipantRole Role)> Invites { get; } = [];

    public Task<IReadOnlyDictionary<Guid, InviteStatus>> GetStatusesForUserAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, InviteStatus>>(Invites
            .Where(p => p.Key.UserId == userId && sessionIds.Contains(p.Key.SessionId))
            .ToDictionary(p => p.Key.SessionId, p => p.Value.Status));

    public Task<IReadOnlyDictionary<Guid, InviteStatus>> GetLearnerStatusesForSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, InviteStatus>>(Invites
            .Where(p => p.Key.SessionId == sessionId && p.Value.Role == LiveParticipantRole.Learner)
            .ToDictionary(p => p.Key.UserId, p => p.Value.Status));
}

internal sealed class FakeAttendanceReader : ILiveAttendanceReader
{
    public HashSet<(Guid UserId, Guid CourseId)> Attended { get; } = [];

    public Dictionary<Guid, LiveSessionStats> Stats { get; } = [];

    public int StatsCalls { get; private set; }

    public List<IReadOnlyCollection<Guid>> StatsRequests { get; } = [];

    public Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(courseIds.Where(c => Attended.Contains((userId, c))).ToHashSet());

    public Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        StatsCalls++;
        StatsRequests.Add(sessionIds.ToArray());
        return Task.FromResult<IReadOnlyDictionary<Guid, LiveSessionStats>>(sessionIds.ToDictionary(
            id => id,
            id => Stats.TryGetValue(id, out var stats) ? stats : new LiveSessionStats(id, 0, 0, false)));
    }
}

internal sealed class FakeContactReader : IUserContactReader
{
    public Dictionary<Guid, (string Email, string DisplayName)> Contacts { get; } = [];

    public List<IReadOnlyCollection<Guid>> BatchRequests { get; } = [];

    public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The roster must use the batch lookup, never one call per user.");

    public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The roster must use the batch lookup, never one call per user.");

    public Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        BatchRequests.Add(ids);
        return Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(
            ids.Where(Contacts.ContainsKey).ToDictionary(id => id, id => Contacts[id]));
    }
}

internal sealed class FakeCourseSummaries : ICourseSummaryReader
{
    public Dictionary<Guid, CourseSummaryInfo> Summaries { get; } = [];

    public Task<IReadOnlyDictionary<Guid, CourseSummaryInfo>> GetCourseSummariesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CourseSummaryInfo>>(courseIds.Where(Summaries.ContainsKey).ToDictionary(id => id, id => Summaries[id]));
}

/// <summary>
/// Wires the P11-05 services over the fakes above. Time is a <see cref="FakeClock"/> (default <see cref="LiveTestData.Now"/>); the room link of a session is a real
/// AES-GCM ciphertext made by the same protector the services use, so "decrypts back" and "never leaks" are tested for real.
/// </summary>
internal sealed class JoinHarness
{
    public const string RoomUrl = "https://meet.google.com/abc-defg-hij?authuser=SECRET-ROOM-TOKEN";

    public FakeClock Clock { get; } = new(LiveTestData.Now);

    public JoinFakeSchedule Schedule { get; } = new();

    public JoinFakeLearning Learning { get; } = new();

    public InMemorySessionMeetingRepository Meetings { get; } = new();

    public InMemoryJoinLogRepository JoinLogs { get; } = new();

    public FakeInviteReader Invites { get; } = new();

    public FakeAttendanceReader Attendance { get; } = new();

    public FakeContactReader Contacts { get; } = new();

    public FakeCourseSummaries CourseSummaries { get; } = new();

    public ISensitiveDataProtector Protector { get; } = LiveTestData.Protector();

    public ListLogger<SessionJoinService> JoinLogger { get; } = new();

    public ListLogger<SessionMeetingService> MeetingLogger { get; } = new();

    public IOptions<LiveOptions> Options { get; private set; } = LiveTestData.OptionsOf();

    public JoinHarness WithOptions(Action<LiveOptions> configure)
    {
        Options = LiveTestData.OptionsOf(configure);
        return this;
    }

    public SessionMeetingService MeetingService() => new(
        Meetings,
        Schedule,
        new FakeCatalog(),
        new MeetingLinkValidator(Options),
        Protector,
        Clock,
        MeetingLogger);

    public SessionJoinService JoinService() => new(
        Schedule, Learning, Meetings, JoinLogs, MeetingService(), Clock, Options, JoinLogger);

    public LiveLearnerQueries LearnerQueries() => new(
        Schedule, Learning, CourseSummaries, Meetings, Invites, Attendance, JoinService(), Clock, Options);

    public InMemoryRecordingImportRepository RecordingImports { get; } = new();

    public InMemoryAccountRepository Accounts { get; } = new();

    public RecordingImportService RecordingImportService() => new(
        RecordingImports, Accounts, Meetings, Schedule, Clock, Options, new ListLogger<RecordingImportService>());

    public LiveInstructorQueries InstructorQueries() => new(
        Schedule, Learning, Meetings, MeetingService(), JoinLogs, Invites, Attendance, Contacts, Clock, Options, RecordingImportService());

    /// <summary>Adds a session to the schedule (defaults: starts in one day, lasts two hours, scheduled).</summary>
    public LiveSessionContext AddSession(
        Guid? instructorUserId = null,
        Guid? sessionId = null,
        Guid? courseId = null,
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        DateTime? startsAtUtc = null,
        DateTime? endsAtUtc = null,
        string title = "คาบที่ 1",
        string courseTitle = "คอร์สทดสอบ",
        string courseSlug = "test-course",
        Guid? recordingEpisodeId = null,
        string? cancelReason = null,
        string? description = null)
    {
        var starts = startsAtUtc ?? LiveTestData.Now.AddDays(1);
        var context = new LiveSessionContext(
            sessionId ?? Guid.NewGuid(),
            courseId ?? Guid.NewGuid(),
            courseTitle,
            courseSlug,
            title,
            description,
            starts,
            endsAtUtc ?? starts.AddHours(2),
            status,
            cancelReason,
            recordingEpisodeId,
            InstructorProfileId: Guid.NewGuid(),
            instructorUserId ?? Guid.NewGuid(),
            "Test Instructor",
            GoogleAttendeeSyncEnabled: false);

        Schedule.Contexts.Add(context);
        return context;
    }

    /// <summary>Gives the session a usable room whose link is <paramref name="url"/>.</summary>
    public SESSION_MEETING AddMeeting(Guid sessionId, string url = RoomUrl)
    {
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.SetManualLink(Protector.Encrypt(url));
        Meetings.Add(meeting);
        return meeting;
    }

    public void Enroll(Guid userId, Guid courseId) => Learning.ActiveEnrollments.Add((userId, courseId));
}
