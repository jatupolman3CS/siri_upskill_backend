using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Domain;
using Siri.Modules.Notification.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>What one <see cref="SessionInviteService.ReconcileAsync"/> pass did.</summary>
/// <param name="CoursesProcessed">Courses examined and committed (or found already in sync).</param>
/// <param name="CoursesFailed">Courses that threw — rolled back as a whole, retried next run.</param>
/// <param name="EmailsStaged">Outbox e-mails created.</param>
/// <param name="BudgetExhausted">The per-run e-mail budget ran out; the remaining work waits for the next run.</param>
public sealed record InviteReconcileResult(int CoursesProcessed, int CoursesFailed, int EmailsStaged, bool BudgetExhausted);

/// <summary>What one <see cref="SessionInviteService.SendRemindersAsync"/> pass did.</summary>
public sealed record ReminderRunResult(int RemindersSent, int InstructorAlertsSent, int Skipped, int UnitsFailed);

/// <summary>
/// The invite engine of P11-04 (docs/contracts/P11-04-live-invites-ics-reminders.md §4.4/§4.5): a <b>diff-based reconcile</b> between
/// "who should know about which upcoming session" (the course's active enrollments + its instructor, from
/// <see cref="ILearningAccessContract"/>) and "who has been told" (<see cref="SESSION_INVITE"/>), plus the 24 h / 1 h reminders.
/// It is deliberately <b>not</b> hooked into the payment/enrollment flow: a purchase, a revoke, a refund, a new session or a
/// reschedule all surface as a difference the next run finds — so it needs no cross-module event and cannot be lost by a crash.
/// <para>
/// <b>Idempotent by construction.</b> Every e-mail is tied to a state change of a <see cref="SESSION_INVITE"/> row, and the e-mail
/// outbox row, the in-app notification and the invite state are committed by one <c>SaveChanges</c> per course (all-or-nothing).
/// Running twice in a row therefore sends nothing the second time; two instances racing collide on the unique
/// <c>(SESSION_ID, USER_ID)</c> index and the loser's course is retried next run.
/// </para>
/// <para>
/// <b>The room link never leaves the platform.</b> Nothing here ever reads a room URL: every e-mail and calendar file carries the
/// platform join URL (<c>{Live:PublicBaseUrl}/live/{sessionId}/join</c>) and free text typed by an instructor is scrubbed of
/// meeting-host links first (<see cref="MeetingUrlScrubber"/>).
/// </para>
/// </summary>
public sealed partial class SessionInviteService(
    ISessionInviteRepository invites,
    ISessionMeetingRepository meetings,
    ILiveScheduleReader schedule,
    ILearningAccessContract learning,
    IUserContactReader contacts,
    IEmailOutbox emailOutbox,
    IUserNotificationOutbox notifications,
    IOptions<LiveOptions> options,
    IClock clock,
    ILogger<SessionInviteService> logger)
{
    /// <summary>Courses examined per run; more courses rotate through over successive runs.</summary>
    public const int MaxCoursesPerRun = 100;

    /// <summary>E-mails staged per run (the outbox sender drains 100 a minute; this keeps one run from burying it).</summary>
    public const int MaxEmailsPerRun = 300;

    /// <summary>How far into the past sessions are still looked at (so a cancel of a just-finished session still goes out).</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromDays(3);

    /// <summary>The platform name used as the calendar <c>ORGANIZER</c>.</summary>
    public const string OrganizerName = "SIRI UpSkill";

    private const int RotationMinutes = 2;

    private LiveOptions Options => options.Value;

    /// <summary>One reconcile pass over every course with sessions in the window.</summary>
    public async Task<InviteReconcileResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var run = RunContext.Create(Options, clock.UtcNow);

        var contexts = await LiveScheduleWindow
            .GetAllAsync(schedule, run.Now - LookBack, run.Now.AddDays(Options.InviteLookaheadDays), includeCancelled: true, cancellationToken)
            .ConfigureAwait(false);
        if (contexts.Count == 0)
        {
            return new InviteReconcileResult(0, 0, 0, false);
        }

        var meetingRows = await meetings
            .GetBySessionIdsAsync(contexts.Select(c => c.SessionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        var meetingBySession = meetingRows.ToDictionary(m => m.SESSION_ID);

        // Courses whose next session is soonest come first; when there are more than MaxCoursesPerRun the window of courses
        // rotates every run, so every course is reached within pageCount runs.
        var groups = contexts
            .GroupBy(c => c.CourseId)
            .OrderBy(g => g.Min(c => c.StartsAtUtc))
            .ThenBy(g => g.Key)
            .ToList();
        var pageCount = (groups.Count + MaxCoursesPerRun - 1) / MaxCoursesPerRun;
        var page = pageCount <= 1 ? 0 : (int)((run.Now.Ticks / (TimeSpan.TicksPerMinute * RotationMinutes)) % pageCount);

        var processed = 0;
        var failed = 0;
        var emails = 0;
        var exhausted = false;

        foreach (var group in groups.Skip(page * MaxCoursesPerRun).Take(MaxCoursesPerRun))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (emails >= MaxEmailsPerRun)
            {
                exhausted = true;
                break;
            }

            try
            {
                var outcome = await ReconcileCourseAsync(run, group.OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId).ToList(), meetingBySession, MaxEmailsPerRun - emails, cancellationToken)
                    .ConfigureAwait(false);
                emails += outcome.EmailsStaged;
                exhausted |= outcome.BudgetExhausted;
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // All-or-nothing per course: forget everything staged for it (invites, outbox rows, notifications) so nothing
                // half-applied is ever saved. Only ids and the exception type are logged — never an address.
                invites.ClearTracking();
                failed++;
                logger.LogError("Live invite reconcile failed for course {CourseId} ({ExceptionType}); it is retried next run.", group.Key, ex.GetType().Name);
            }
        }

        logger.LogInformation(
            "Live invite reconcile: {Processed} course(s), {Failed} failed, {Emails} e-mail(s) staged{Exhausted}.",
            processed,
            failed,
            emails,
            exhausted ? " (budget exhausted)" : string.Empty);

        return new InviteReconcileResult(processed, failed, emails, exhausted);
    }

    // ---- One course -------------------------------------------------------------------------------

    private async Task<CourseOutcome> ReconcileCourseAsync(
        RunContext run,
        IReadOnlyList<LiveSessionContext> sessions,
        IReadOnlyDictionary<Guid, SESSION_MEETING> meetingBySession,
        int emailBudget,
        CancellationToken cancellationToken)
    {
        var anchor = sessions[0];
        var owner = anchor.InstructorUserId;

        var enrolled = await learning.GetActiveEnrolledUserIdsAsync(anchor.CourseId, cancellationToken).ConfigureAwait(false);
        var learners = new HashSet<Guid>(enrolled);
        learners.Remove(owner); // the instructor is a participant, not a learner of their own course

        var existing = await invites.GetBySessionIdsAsync(sessions.Select(s => s.SessionId).ToArray(), cancellationToken).ConfigureAwait(false);
        var invite = existing.ToDictionary(i => (i.SESSION_ID, i.USER_ID));
        var contextBySession = sessions.ToDictionary(s => s.SessionId);
        var changed = 0;

        // A. Everyone who should be invited to an upcoming session has a row; a withdrawn invite of someone eligible again is revived.
        var participants = learners.Append(owner).ToList();
        foreach (var session in sessions.Where(s => SessionInvitePolicy.IsUpcoming(s, run.Now)))
        {
            foreach (var userId in participants)
            {
                if (!invite.TryGetValue((session.SessionId, userId), out var row))
                {
                    row = SESSION_INVITE.Create(
                        session.SessionId,
                        userId,
                        userId == owner ? LiveParticipantRole.Instructor : LiveParticipantRole.Learner,
                        clock);
                    invites.Add(row);
                    invite[(session.SessionId, userId)] = row;
                    changed++;
                }
                else if (row.STATUS == InviteStatus.Cancelled)
                {
                    row.Reinvite();
                    changed++;
                }
            }
        }

        // B-E. Decide, per participant, which e-mails are owed (no I/O yet).
        var works = new List<UserWork>();
        foreach (var byUser in invite.Values.GroupBy(i => i.USER_ID).OrderBy(g => g.Key))
        {
            var rows = byUser.ToList();
            var work = Classify(run, byUser.Key, rows, owner, learners, contextBySession, meetingBySession);
            if (work.HasAnything)
            {
                works.Add(work);
            }
        }

        // Contacts for everyone who is owed an e-mail, in one batch.
        var needContact = works.Where(w => w.NeedsContact).Select(w => w.UserId).ToArray();
        var contactMap = needContact.Length == 0
            ? new Dictionary<Guid, (string Email, string DisplayName)>()
            : (await contacts.GetUsersContactInfoAsync(needContact, cancellationToken).ConfigureAwait(false))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Apply — stop between participants when the e-mail budget is used up (the rest is picked up next run).
        var emails = 0;
        var exhausted = false;
        foreach (var work in works)
        {
            if (emails > 0 && emails + work.EstimatedEmails > emailBudget)
            {
                exhausted = true;
                break;
            }

            string? email = null;
            string? name = null;
            if (work.NeedsContact && contactMap.TryGetValue(work.UserId, out var contact) && IsDeliverableEmail(contact.Email))
            {
                email = contact.Email.Trim();
                name = string.IsNullOrWhiteSpace(contact.DisplayName) ? null : LiveEmailTemplates.Clean(run.Settings, contact.DisplayName);
            }

            emails += Apply(run, anchor, work, email, name, ref changed);
        }

        if (changed > 0)
        {
            await invites.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Done with this course's entities — drop them so the next course starts from a clean tracker.
        invites.ClearTracking();
        return new CourseOutcome(emails, exhausted);
    }

    /// <summary>Sorts one participant's invites into the work each needs (contract §4.4 B–E). Pure apart from reading entity state.</summary>
    private static UserWork Classify(
        RunContext run,
        Guid userId,
        IReadOnlyList<SESSION_INVITE> rows,
        Guid owner,
        IReadOnlySet<Guid> learners,
        IReadOnlyDictionary<Guid, LiveSessionContext> contextBySession,
        IReadOnlyDictionary<Guid, SESSION_MEETING> meetingBySession)
    {
        var isInstructor = userId == owner;
        var eligible = isInstructor || learners.Contains(userId);
        var work = new UserWork(userId, isInstructor ? LiveParticipantRole.Instructor : LiveParticipantRole.Learner)
        {
            IsFirstInvite = SessionInvitePolicy.IsFirstInvite(rows),
        };

        foreach (var row in rows)
        {
            var context = contextBySession[row.SESSION_ID];
            meetingBySession.TryGetValue(row.SESSION_ID, out var meeting);
            var item = new InviteItem(row, context, meeting);

            if (!eligible)
            {
                // B. Lost access (refund, expiry, revoke): withdraw what was sent, drop what never was.
                if (row.ROLE == LiveParticipantRole.Learner)
                {
                    if (row.STATUS == InviteStatus.Invited && context.EndsAtUtc > run.Now)
                    {
                        work.Lapsed.Add(item);
                    }
                    else if (row.STATUS == InviteStatus.Pending)
                    {
                        work.SilentCancel.Add(item);
                    }
                }

                continue;
            }

            switch (row.STATUS)
            {
                case InviteStatus.Pending:
                    if (context.Status == LiveSessionStatus.Cancelled)
                    {
                        work.SilentCancel.Add(item); // never sent, and the session is gone
                    }
                    else if (SessionInvitePolicy.IsUpcoming(context, run.Now))
                    {
                        // The instructor's batch waits until the room provider is decided (the meeting job's first pass).
                        if (!isInstructor || meeting?.PROVIDER is not null)
                        {
                            work.NewBatch.Add(item);
                        }
                    }

                    break;

                case InviteStatus.Invited:
                    if (context.Status == LiveSessionStatus.Cancelled)
                    {
                        work.Cancelled.Add(item); // C
                    }
                    else if (SessionInvitePolicy.IsUpcoming(context, run.Now) && SessionInvitePolicy.NeedsUpdate(row, item.MeetingSequence))
                    {
                        work.Updated.Add(item); // D
                    }

                    break;
            }
        }

        return work;
    }

    /// <summary>Stages the e-mails/notifications for one participant and moves their invites to the matching state. Returns how many
    /// e-mails were staged. <paramref name="email"/> is <c>null</c> when they cannot be reached.</summary>
    private int Apply(RunContext run, LiveSessionContext anchor, UserWork work, string? email, string? name, ref int changed)
    {
        var emails = 0;
        var courseTitle = anchor.CourseTitle;
        var courseSlug = anchor.CourseSlug;
        var isInstructor = work.Role == LiveParticipantRole.Instructor;

        foreach (var item in work.SilentCancel)
        {
            item.Invite.MarkCancelled(item.NextSequence, clock);
            changed++;
        }

        // B. Lapsed learner: one CANCEL document (chunked) removes the course's sessions from their calendar.
        if (work.Lapsed.Count > 0)
        {
            if (email is null)
            {
                foreach (var item in work.Lapsed)
                {
                    item.Invite.MarkCancelled(item.NextSequence, clock);
                    changed++;
                }
            }
            else
            {
                foreach (var chunk in work.Lapsed.OrderBy(i => i.Context.StartsAtUtc).Chunk(LiveEmailTemplates.MaxSessionsPerEmail))
                {
                    var sequences = chunk.Select(i => i.NextSequence).ToArray();
                    var message = LiveEmailTemplates.LearnerLapsed(run.Settings, courseTitle, courseSlug);
                    var ics = run.BuildIcs(IcsMethod.Cancel, chunk.Select((i, k) => run.ToEvent(courseTitle, i.Context, sequences[k], cancelled: true)).ToList(), email, name);
                    Stage(email, work.UserId, message, IcsMethod.Cancel, ics);
                    emails++;

                    for (var k = 0; k < chunk.Length; k++)
                    {
                        chunk[k].Invite.MarkCancelled(sequences[k], clock);
                        changed++;
                    }
                }
            }
        }

        // C. Cancelled session: one CANCEL e-mail per session (nothing for an instructor whose room is a Google event — Google
        // already removed it from their own calendar).
        foreach (var item in work.Cancelled.OrderBy(i => i.Context.StartsAtUtc))
        {
            var sequence = item.NextSequence;
            if (isInstructor && SessionInvitePolicy.IsGoogleEvent(item.Meeting))
            {
                // silent
            }
            else if (email is null)
            {
                // unreachable: nothing to send
            }
            else
            {
                var message = isInstructor
                    ? LiveEmailTemplates.InstructorSessionCancelled(run.Settings, courseTitle, item.Context)
                    : LiveEmailTemplates.LearnerSessionCancelled(run.Settings, courseTitle, courseSlug, item.Context);
                var ics = run.BuildIcs(IcsMethod.Cancel, [run.ToEvent(courseTitle, item.Context, sequence, cancelled: true)], email, name);
                Stage(email, work.UserId, message, IcsMethod.Cancel, ics);
                emails++;
            }

            item.Invite.MarkCancelled(sequence, clock);
            changed++;
        }

        // D. Moved/renamed session: REQUEST with a higher SEQUENCE updates the existing calendar entry; reminders restart.
        foreach (var item in work.Updated.OrderBy(i => i.Context.StartsAtUtc))
        {
            var sequence = item.NextSequence;
            if (isInstructor && SessionInvitePolicy.IsGoogleEvent(item.Meeting))
            {
                CompleteInvite(run, item, sequence, resetReminders: true);
            }
            else if (email is null)
            {
                item.Invite.MarkSkipped(NoContactCode);
            }
            else
            {
                var message = isInstructor
                    ? LiveEmailTemplates.InstructorSessionUpdated(run.Settings, courseTitle, item.Context)
                    : LiveEmailTemplates.LearnerSessionUpdated(run.Settings, courseTitle, courseSlug, item.Context);
                var ics = run.BuildIcs(IcsMethod.Request, [run.ToEvent(courseTitle, item.Context, sequence, cancelled: false)], email, name);
                Stage(email, work.UserId, message, IcsMethod.Request, ics);
                emails++;
                CompleteInvite(run, item, sequence, resetReminders: true);
            }

            changed++;
        }

        // E. New sessions: ONE e-mail per participant per course (a table + one PUBLISH calendar with a VEVENT per session).
        if (work.NewBatch.Count > 0)
        {
            if (email is null)
            {
                foreach (var item in work.NewBatch)
                {
                    item.Invite.MarkSkipped(NoContactCode);
                    changed++;
                }
            }
            else
            {
                var isFirst = work.IsFirstInvite;
                foreach (var chunk in work.NewBatch.OrderBy(i => i.Context.StartsAtUtc).ThenBy(i => i.Context.SessionId).Chunk(LiveEmailTemplates.MaxSessionsPerEmail))
                {
                    var sequences = chunk.Select(i => i.NextSequence).ToArray();
                    var contexts = chunk.Select(i => i.Context).ToList();

                    LiveMessage message;
                    string? ics;
                    if (isInstructor)
                    {
                        var ready = chunk.ToDictionary(i => i.Context.SessionId, i => i.Meeting?.IsUsable == true);
                        message = LiveEmailTemplates.InstructorBatch(run.Settings, courseTitle, contexts, c => ready[c.SessionId]);

                        // A Google event is already on the instructor's own calendar — a calendar file from us would duplicate it.
                        var events = chunk
                            .Select((i, k) => (Item: i, Sequence: sequences[k]))
                            .Where(x => !SessionInvitePolicy.IsGoogleEvent(x.Item.Meeting))
                            .Select(x => run.ToEvent(courseTitle, x.Item.Context, x.Sequence, cancelled: false))
                            .ToList();
                        ics = events.Count == 0 ? null : run.BuildIcs(IcsMethod.Publish, events, null, null);
                    }
                    else
                    {
                        message = LiveEmailTemplates.LearnerInviteBatch(run.Settings, courseTitle, courseSlug, contexts, isFirst);
                        ics = run.BuildIcs(IcsMethod.Publish, chunk.Select((i, k) => run.ToEvent(courseTitle, i.Context, sequences[k], cancelled: false)).ToList(), null, null);
                    }

                    Stage(email, work.UserId, message, ics is null ? null : IcsMethod.Publish, ics);
                    emails++;
                    isFirst = false;

                    for (var k = 0; k < chunk.Length; k++)
                    {
                        CompleteInvite(run, chunk[k], sequences[k], resetReminders: false);
                        changed++;
                    }
                }
            }
        }

        return emails;
    }

    /// <summary>The invite has just been (re)announced with <paramref name="sequence"/>: record it, and silence the reminders whose
    /// moment has already passed (the e-mail that went out is their notice).</summary>
    private void CompleteInvite(RunContext run, InviteItem item, int sequence, bool resetReminders)
    {
        item.Invite.MarkInvited(sequence, clock);
        if (resetReminders)
        {
            item.Invite.ResetReminders();
        }

        SessionInvitePolicy.SuppressElapsedReminders(item.Invite, item.Context, run.Now, clock);
    }

    private void Stage(string toEmail, Guid userId, LiveMessage message, IcsMethod? method, string? ics)
    {
        var calendar = method is null || ics is null ? null : new EmailCalendarPart(IcsCalendarBuilder.MethodToken(method.Value), ics);
        emailOutbox.Enqueue(toEmail, message.Subject, message.BodyHtml, message.TemplateKey, calendar);
        notifications.Stage(userId, message.InApp.Type, message.InApp.Title, message.InApp.Body, message.InApp.LinkPath);
    }

    /// <summary>Error code stored on an invite that could not be sent because the participant has no usable e-mail address.</summary>
    public const string NoContactCode = "no_contact";

    /// <summary>A single plain mailbox with a domain — what the outbox sender would accept. Anything else is treated as "no contact"
    /// (it would only fail later in the sender, repeatedly).</summary>
    internal static bool IsDeliverableEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var value = email.Trim();
        if (value.Length > 320 || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '<' or '>' or '"' or ';' or ','))
        {
            return false;
        }

        var at = value.LastIndexOf('@');
        return at > 0 && at < value.Length - 1 && MailAddress.TryCreate(value, out _);
    }

    // ---- Working types ---------------------------------------------------------------------------

    private sealed record CourseOutcome(int EmailsStaged, bool BudgetExhausted);

    private sealed record InviteItem(SESSION_INVITE Invite, LiveSessionContext Context, SESSION_MEETING? Meeting)
    {
        public int MeetingSequence => Meeting?.ICS_SEQUENCE ?? 0;

        /// <summary>The SEQUENCE for the next calendar file about this invite — computed from the invite's current state, so read it
        /// before changing the invite.</summary>
        public int NextSequence => Invite.NextSequence(MeetingSequence);
    }

    private sealed class UserWork(Guid userId, LiveParticipantRole role)
    {
        public Guid UserId { get; } = userId;

        public LiveParticipantRole Role { get; } = role;

        public bool IsFirstInvite { get; init; }

        public List<InviteItem> Lapsed { get; } = [];

        public List<InviteItem> Cancelled { get; } = [];

        public List<InviteItem> Updated { get; } = [];

        public List<InviteItem> NewBatch { get; } = [];

        public List<InviteItem> SilentCancel { get; } = [];

        public bool HasAnything => Lapsed.Count + Cancelled.Count + Updated.Count + NewBatch.Count + SilentCancel.Count > 0;

        /// <summary>Whether any of the work needs the participant's address (a silent change does not).</summary>
        public bool NeedsContact => Lapsed.Count + Cancelled.Count + Updated.Count + NewBatch.Count > 0;

        /// <summary>An upper bound on the e-mails this participant is owed — used to respect the per-run budget.</summary>
        public int EstimatedEmails =>
            ChunkCount(Lapsed.Count) + Cancelled.Count + Updated.Count + ChunkCount(NewBatch.Count);

        private static int ChunkCount(int count) =>
            count == 0 ? 0 : (count + LiveEmailTemplates.MaxSessionsPerEmail - 1) / LiveEmailTemplates.MaxSessionsPerEmail;
    }

    /// <summary>Everything one run needs that does not change while it runs.</summary>
    private sealed record RunContext(DateTime Now, LiveTemplateSettings Settings, string OrganizerEmail, string UidHost)
    {
        public static RunContext Create(LiveOptions options, DateTime now)
        {
            var settings = LiveTemplateSettings.From(options);
            var host = Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) ? uri.IdnHost : "siri.invalid";
            var organizer = string.IsNullOrWhiteSpace(options.OrganizerEmail) ? $"no-reply@{host}" : options.OrganizerEmail.Trim();
            return new RunContext(now, settings, organizer, host);
        }

        public IcsEvent ToEvent(string courseTitle, LiveSessionContext session, int sequence, bool cancelled) =>
            new(
                session.SessionId,
                sequence,
                $"{LiveEmailTemplates.Clean(Settings, courseTitle)} — {LiveEmailTemplates.Clean(Settings, session.Title)}",
                LiveEmailTemplates.CleanOptional(Settings, session.Description),
                session.StartsAtUtc,
                session.EndsAtUtc,
                Settings.JoinUrl(session.SessionId),
                cancelled);

        public string BuildIcs(IcsMethod method, IReadOnlyList<IcsEvent> events, string? attendeeEmail, string? attendeeName) =>
            IcsCalendarBuilder.Build(method, events, OrganizerEmail, OrganizerName, UidHost, attendeeEmail, attendeeName, Now, Settings.JoinWindowBeforeMinutes);
    }
}
