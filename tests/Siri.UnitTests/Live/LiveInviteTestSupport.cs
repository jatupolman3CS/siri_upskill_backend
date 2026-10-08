using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>An e-mail as the outbox stores it (fields read back from the real <see cref="EMAIL_OUTBOX_MESSAGE"/> entity).</summary>
internal sealed record OutboxEmail(string To, string Subject, string BodyHtml, string? TemplateKey, string? CalendarMethod, string? CalendarIcs)
{
    /// <summary>The ICS with RFC 5545 line folding undone, split into content lines.</summary>
    public IReadOnlyList<string> IcsLines => CalendarIcs is null ? [] : IcsText.UnfoldedLines(CalendarIcs);

    public int EventCount => IcsLines.Count(l => l == "BEGIN:VEVENT");

    public IReadOnlyList<string> Property(string name) =>
        IcsLines.Where(l => l.StartsWith(name + ":", StringComparison.Ordinal) || l.StartsWith(name + ";", StringComparison.Ordinal)).ToList();

    /// <summary>Every searchable text of the message — subject, body, calendar (unfolded, so a host split across a fold is still found)
    /// — in one string.</summary>
    public string Everything => $"{Subject}\n{BodyHtml}\n{string.Join("\n", IcsLines)}";
}

internal sealed record InAppItem(Guid UserId, string Type, string Title, string Body, string? LinkUrl);

internal static class IcsText
{
    public static IReadOnlyList<string> UnfoldedLines(string ics) =>
        ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// An in-memory stand-in for the shared <c>AppDbContext</c> as the invite service sees it: the invite repository, the meeting
/// repository, the e-mail outbox and the in-app notification outbox all stage into one unit of work that
/// <see cref="SaveChangesAsync"/> commits and <see cref="ClearTracking"/> throws away — exactly the property the contract's
/// "all-or-nothing per course" rule relies on. Rows are cloned in and out so a failed save genuinely leaves the committed state untouched
/// (an entity mutated in memory but never saved is forgotten, like a detached EF entity).
/// </summary>
internal sealed class FakeLiveDb : ISessionInviteRepository, ISessionMeetingRepository, IEmailOutbox, IUserNotificationOutbox
{
    private static readonly MethodInfo CloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Dictionary<Guid, SESSION_INVITE> _trackedInvites = [];
    private readonly Dictionary<Guid, SESSION_MEETING> _trackedMeetings = [];
    private readonly List<SESSION_INVITE> _addedInvites = [];
    private readonly List<SESSION_MEETING> _addedMeetings = [];
    private readonly List<OutboxEmail> _stagedEmails = [];
    private readonly List<InAppItem> _stagedNotifications = [];

    // ---- Committed state ---------------------------------------------------------------------------
    public List<SESSION_INVITE> Invites { get; } = [];

    public List<SESSION_MEETING> Meetings { get; } = [];

    public List<OutboxEmail> Emails { get; } = [];

    public List<InAppItem> Notifications { get; } = [];

    public int SaveCount { get; private set; }

    public int ClearTrackingCount { get; private set; }

    /// <summary>Makes the next <see cref="SaveChangesAsync"/> throw (then clears itself).</summary>
    public Exception? ThrowOnNextSave { get; set; }

    /// <summary>Runs at the start of the next <see cref="SaveChangesAsync"/> (then clears itself) — a rival worker committing in the gap
    /// between this unit's read and its write.</summary>
    public Action? BeforeNextCommit { get; set; }

    public SESSION_INVITE? InviteFor(Guid sessionId, Guid userId) =>
        Invites.SingleOrDefault(i => i.SESSION_ID == sessionId && i.USER_ID == userId);

    public IEnumerable<OutboxEmail> EmailsTo(string address) => Emails.Where(e => e.To == address);

    /// <summary>Puts a row straight into the committed state (arrange step).</summary>
    public void Seed(SESSION_INVITE invite) => Invites.Add(Clone(invite));

    public void Seed(SESSION_MEETING meeting) => Meetings.Add(Clone(meeting));

    private static T Clone<T>(T value) where T : class => (T)CloneMethod.Invoke(value, null)!;

    private SESSION_INVITE Track(SESSION_INVITE row)
    {
        if (!_trackedInvites.TryGetValue(row.SESSION_INVITE_ID, out var tracked))
        {
            tracked = Clone(row);
            _trackedInvites[row.SESSION_INVITE_ID] = tracked;
        }

        return tracked;
    }

    // ---- ISessionInviteRepository ------------------------------------------------------------------
    public Task<IReadOnlyList<SESSION_INVITE>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var result = Invites.Where(i => sessionIds.Contains(i.SESSION_ID)).Select(Track).ToList();
        result.AddRange(_addedInvites.Where(i => sessionIds.Contains(i.SESSION_ID)));
        return Task.FromResult<IReadOnlyList<SESSION_INVITE>>(result);
    }

    public Task<IReadOnlyList<SESSION_INVITE>> GetInvitedAwaitingReminderAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_INVITE>>(Invites
            .Where(i => sessionIds.Contains(i.SESSION_ID)
                && i.STATUS == InviteStatus.Invited
                && (i.REMINDER_24H_SENT_AT_UTC is null || i.REMINDER_1H_SENT_AT_UTC is null))
            .Select(Track)
            .ToList());

    public Task<IReadOnlyList<Guid>> GetSessionIdsNeedingAttendeeSyncAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Invites
            .Where(i => sessionIds.Contains(i.SESSION_ID)
                && i.ROLE == LiveParticipantRole.Learner
                && ((i.STATUS == InviteStatus.Invited && i.GOOGLE_ATTENDEE_SYNCED_AT_UTC is null)
                    || (i.STATUS != InviteStatus.Invited && i.GOOGLE_ATTENDEE_SYNCED_AT_UTC is not null)))
            .Select(i => i.SESSION_ID)
            .Distinct()
            .ToList());

    public void Add(SESSION_INVITE invite) => _addedInvites.Add(invite);

    // ---- ISessionMeetingRepository -----------------------------------------------------------------
    public Task<SESSION_MEETING?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var added = _addedMeetings.FirstOrDefault(m => m.SESSION_ID == sessionId);
        if (added is not null)
        {
            return Task.FromResult<SESSION_MEETING?>(added);
        }

        var row = Meetings.FirstOrDefault(m => m.SESSION_ID == sessionId);
        if (row is null)
        {
            return Task.FromResult<SESSION_MEETING?>(null);
        }

        if (!_trackedMeetings.TryGetValue(row.SESSION_MEETING_ID, out var tracked))
        {
            tracked = Clone(row);
            _trackedMeetings[row.SESSION_MEETING_ID] = tracked;
        }

        return Task.FromResult<SESSION_MEETING?>(tracked);
    }

    Task<IReadOnlyList<SESSION_MEETING>> ISessionMeetingRepository.GetBySessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_MEETING>>(Meetings.Where(m => sessionIds.Contains(m.SESSION_ID)).Select(Clone).ToList());

    public Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(Meetings.Where(m => sessionIds.Contains(m.SESSION_ID)).Select(m => m.SESSION_ID).ToHashSet());

    public Task<IReadOnlyList<Guid>> GetDueSessionIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Meetings
            .Where(m => m.SYNC_STATUS is MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete
                && (m.NEXT_RETRY_AT_UTC is null || m.NEXT_RETRY_AT_UTC <= nowUtc))
            .Take(take)
            .Select(m => m.SESSION_ID)
            .ToList());

    public Task<IReadOnlyList<SESSION_MEETING>> GetResettableByInstructorAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_MEETING>>([]);

    public void Add(SESSION_MEETING meeting) => _addedMeetings.Add(meeting);

    // ---- Unit of work -------------------------------------------------------------------------------
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (BeforeNextCommit is { } rival)
        {
            BeforeNextCommit = null;
            rival();
        }

        if (ThrowOnNextSave is { } exception)
        {
            ThrowOnNextSave = null;
            throw exception;
        }

        // The unique (SESSION_ID, USER_ID) index: a duplicate fails the whole save, nothing is committed.
        foreach (var added in _addedInvites)
        {
            if (Invites.Any(i => i.SESSION_ID == added.SESSION_ID && i.USER_ID == added.USER_ID))
            {
                throw new InvalidOperationException("duplicate key value violates unique constraint IX_SESSION_INVITES_SESSION_USER");
            }
        }

        foreach (var tracked in _trackedInvites.Values)
        {
            var index = Invites.FindIndex(i => i.SESSION_INVITE_ID == tracked.SESSION_INVITE_ID);
            if (index >= 0)
            {
                Invites[index] = Clone(tracked);
            }
        }

        foreach (var added in _addedInvites)
        {
            Invites.Add(Clone(added));
            _trackedInvites[added.SESSION_INVITE_ID] = added;
        }

        _addedInvites.Clear();

        foreach (var tracked in _trackedMeetings.Values)
        {
            var index = Meetings.FindIndex(m => m.SESSION_MEETING_ID == tracked.SESSION_MEETING_ID);
            if (index >= 0)
            {
                Meetings[index] = Clone(tracked);
            }
        }

        foreach (var added in _addedMeetings)
        {
            Meetings.Add(Clone(added));
            _trackedMeetings[added.SESSION_MEETING_ID] = added;
        }

        _addedMeetings.Clear();

        Emails.AddRange(_stagedEmails);
        Notifications.AddRange(_stagedNotifications);
        _stagedEmails.Clear();
        _stagedNotifications.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    public void ClearTracking()
    {
        ClearTrackingCount++;
        _trackedInvites.Clear();
        _trackedMeetings.Clear();
        _addedInvites.Clear();
        _addedMeetings.Clear();
        _stagedEmails.Clear();
        _stagedNotifications.Clear();
    }

    // ---- IEmailOutbox ---------------------------------------------------------------------------------
    public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
        Enqueue(toEmail, subject, bodyHtml, templateKey, calendar: null);

    public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey, EmailCalendarPart? calendar)
    {
        // Validate exactly like the real outbox: the domain entity rejects a bad METHOD / empty / oversized / non-VCALENDAR part.
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue(toEmail, subject, bodyHtml, templateKey, calendar?.Method, calendar?.IcsContent);
        _stagedEmails.Add(new OutboxEmail(
            message.ToEmail, message.Subject, message.BodyHtml, message.TemplateKey, message.CalendarMethod, message.CalendarIcs));
    }

    // ---- IUserNotificationOutbox ----------------------------------------------------------------------
    public void Stage(Guid userId, string type, string title, string body, string? linkUrl) =>
        _stagedNotifications.Add(new InAppItem(userId, type, title, body, linkUrl));
}

/// <summary>Plain recording in-app outbox for tests that do not need a unit of work (the alert-sender tests).</summary>
internal sealed class RecordingInApp : IUserNotificationOutbox
{
    public List<InAppItem> Items { get; } = [];

    public void Stage(Guid userId, string type, string title, string body, string? linkUrl) =>
        Items.Add(new InAppItem(userId, type, title, body, linkUrl));
}

internal sealed class FakeLearning : ILearningAccessContract
{
    public Dictionary<Guid, HashSet<Guid>> Enrolled { get; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlySet<Guid>>(Enrolled.TryGetValue(courseId, out var set) ? new HashSet<Guid>(set) : []);
    }

    public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeContactBook : IUserContactReader
{
    public Dictionary<Guid, (string Email, string DisplayName)> Users { get; } = [];

    /// <summary>When set, a batch lookup containing this user throws (simulates a failing dependency for one course).</summary>
    public Guid? ThrowForUser { get; set; }

    public int BatchCalls { get; private set; }

    public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.TryGetValue(userId, out var user) ? user.Email : null);

    public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.TryGetValue(userId, out var user) ? ((string?)user.Email, (string?)user.DisplayName) : (null, null));

    public Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        BatchCalls++;
        var ids = userIds.ToList();
        if (ThrowForUser is { } poison && ids.Contains(poison))
        {
            throw new InvalidOperationException("contact lookup failed");
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(
            ids.Where(Users.ContainsKey).ToDictionary(id => id, id => Users[id]));
    }
}

/// <summary>Everything the invite service needs, wired to in-memory fakes, with arrange helpers.</summary>
internal sealed class InviteHarness
{
    public static readonly string[] MeetingHosts = ["meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com"];

    public const string PublicBaseUrl = "https://app.example.test";

    public InviteHarness(DateTime? now = null)
    {
        Clock = new FakeClock(now ?? LiveTestData.Now);
    }

    public FakeClock Clock { get; }

    public FakeLiveDb Db { get; } = new();

    public FakeSchedule Schedule { get; } = new();

    public FakeLearning Learning { get; } = new();

    public FakeContactBook Contacts { get; } = new();

    public ListLogger<SessionInviteService> Logger { get; } = new();

    public Guid InstructorUserId { get; } = Guid.NewGuid();

    public SessionInviteService Service() =>
        new(Db, Db, Schedule, Learning, Contacts, Db, Db, LiveTestData.OptionsOf(), Clock, Logger);

    /// <summary>A scheduled session of <paramref name="courseId"/> starting <paramref name="startsIn"/> from now (negative = already started).</summary>
    public LiveSessionContext AddSession(
        Guid courseId,
        TimeSpan startsIn,
        string title = "คาบเรียนสด",
        string courseTitle = "คอร์สทดสอบ",
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        string? description = null,
        Guid? instructorUserId = null,
        TimeSpan? duration = null)
    {
        var starts = Clock.UtcNow + startsIn;
        var context = LiveTestData.Context(
                instructorUserId: instructorUserId ?? InstructorUserId,
                status: status,
                startsAtUtc: starts,
                endsAtUtc: starts + (duration ?? TimeSpan.FromHours(2)),
                title: title,
                courseTitle: courseTitle,
                description: description)
            with
            {
                CourseId = courseId,
            };

        Schedule.Contexts.Add(context);
        return context;
    }

    /// <summary>A learner with an active enrollment and a reachable address.</summary>
    public (Guid UserId, string Email) AddLearner(Guid courseId, string? email = null, string name = "ผู้เรียนทดสอบ")
    {
        var userId = Guid.NewGuid();
        var address = email ?? $"learner-{userId:N}@example.test";
        Contacts.Users[userId] = (address, name);
        Enroll(courseId, userId);
        return (userId, address);
    }

    public void Enroll(Guid courseId, Guid userId)
    {
        if (!Learning.Enrolled.TryGetValue(courseId, out var set))
        {
            Learning.Enrolled[courseId] = set = [];
        }

        set.Add(userId);
    }

    public void Unenroll(Guid courseId, Guid userId) => Learning.Enrolled[courseId].Remove(userId);

    public string AddInstructorContact(string email = "teacher@example.test")
    {
        Contacts.Users[InstructorUserId] = (email, "ผู้สอนทดสอบ");
        return email;
    }

    /// <summary>A meeting row for <paramref name="sessionId"/> with a pasted link (Manual provider, usable room).</summary>
    public SESSION_MEETING AddManualMeeting(Guid sessionId, string meetUrl = "https://zoom.us/j/987654321?pwd=SECRETPASSCODE")
    {
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.SetManualLink(LiveTestData.Protector().Encrypt(meetUrl));
        Db.Seed(meeting);
        return meeting;
    }

    /// <summary>A meeting row whose provider is decided but that has no room yet (<c>AwaitingLink</c>).</summary>
    public SESSION_MEETING AddAwaitingLinkMeeting(Guid sessionId)
    {
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.ResolveAsAwaitingLink();
        Db.Seed(meeting);
        return meeting;
    }

    /// <summary>A meeting row that is a Google Calendar event on the instructor's own calendar.</summary>
    public SESSION_MEETING AddGoogleMeeting(Guid sessionId, string meetUrl = "https://meet.google.com/abc-defg-hij")
    {
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "event-1", LiveTestData.Protector().Encrypt(meetUrl), Guid.NewGuid(), Clock);
        Db.Seed(meeting);
        return meeting;
    }

    /// <summary>Applies <paramref name="change"/> to the committed meeting row of <paramref name="sessionId"/>.</summary>
    public void ChangeMeeting(Guid sessionId, Action<SESSION_MEETING> change)
    {
        var index = Db.Meetings.FindIndex(m => m.SESSION_ID == sessionId);
        var copy = (SESSION_MEETING)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Db.Meetings[index], null)!;
        change(copy);
        Db.Meetings[index] = copy;
    }

    /// <summary>Applies <paramref name="change"/> to the committed invite of (<paramref name="sessionId"/>, <paramref name="userId"/>).</summary>
    public void ChangeInvite(Guid sessionId, Guid userId, Action<SESSION_INVITE> change)
    {
        var index = Db.Invites.FindIndex(i => i.SESSION_ID == sessionId && i.USER_ID == userId);
        var copy = (SESSION_INVITE)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Db.Invites[index], null)!;
        change(copy);
        Db.Invites[index] = copy;
    }

    /// <summary>An invite that was sent <paramref name="sentAgo"/> before now with ICS <paramref name="sequence"/>, put straight into the database.</summary>
    public SESSION_INVITE SeedInvited(Guid sessionId, Guid userId, LiveParticipantRole role, TimeSpan sentAgo, int sequence = 0)
    {
        var sentAt = new FakeClock(Clock.UtcNow - sentAgo);
        var invite = SESSION_INVITE.Create(sessionId, userId, role, sentAt);
        invite.MarkInvited(sequence, sentAt);
        Db.Seed(invite);
        return invite;
    }

    /// <summary>Replaces a session's context (a reschedule / cancellation / rename as the Catalog reader would now report it).</summary>
    public LiveSessionContext UpdateSession(Guid sessionId, Func<LiveSessionContext, LiveSessionContext> change)
    {
        var index = Schedule.Contexts.FindIndex(c => c.SessionId == sessionId);
        var updated = change(Schedule.Contexts[index]);
        Schedule.Contexts[index] = updated;
        return updated;
    }

    public static string DecodeBody(OutboxEmail email) => email.BodyHtml;

    public static bool ContainsMeetingHost(string text) =>
        MeetingHosts.Any(host => text.Contains(host, StringComparison.OrdinalIgnoreCase));
}
