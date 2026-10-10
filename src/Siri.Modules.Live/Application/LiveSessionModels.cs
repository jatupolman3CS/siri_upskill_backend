using Siri.Modules.Catalog.Contracts;

namespace Siri.Modules.Live.Application;

// DTOs of the learner/instructor Live API (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4, JSON shapes in
// docs/contracts/P11-FE-live-dto-appendix.md section B). camelCase JSON, enums as strings (global JsonStringEnumConverter), UTC instants.
// Only JoinLiveSessionResponse and InstructorSessionDetailResponse carry a room URL; no other record in this file may ever get one.

/// <summary>One class in <see cref="MySessionsResponse"/>. <c>DisplayState</c>/<c>CanJoin</c> are decided by the server at <c>ServerTimeUtc</c> and
/// never recomputed by the client.</summary>
/// <param name="Status"><c>Scheduled</c> or <c>Cancelled</c>.</param>
/// <param name="CanJoin"><c>DisplayState == Live</c>: the join window is open and the class has not ended.</param>
/// <param name="JoinOpensAtUtc">Start time minus <c>Live:JoinWindowBeforeMinutes</c>.</param>
/// <param name="RoomReady">The room exists and can be entered (the link itself is never exposed here).</param>
/// <param name="InviteStatus">The caller's calendar-invitation status (<c>Pending</c>/<c>Invited</c>/<c>Cancelled</c>/<c>Skipped</c>), or <c>null</c> when there is none.</param>
public sealed record MySessionItem(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    string Status,
    string? CancelReason,
    bool CanJoin,
    DateTime JoinOpensAtUtc,
    bool RoomReady,
    bool HasRecording,
    Guid? RecordingEpisodeId,
    string? InviteStatus);

/// <param name="Timezone">Always <c>Asia/Bangkok</c> — the zone the platform shows live times in.</param>
/// <param name="ServerTimeUtc">The server clock when the response was computed; the client derives its clock offset from it.</param>
/// <param name="HasAttendedAnySession"><c>true</c> once the caller has been handed a room link for this course (drives the refund notice).</param>
public sealed record MySessionsResponse(
    Guid CourseId,
    string CourseSlug,
    string Timezone,
    DateTime ServerTimeUtc,
    bool HasAttendedAnySession,
    IReadOnlyList<MySessionItem> Sessions);

public sealed record MyUpcomingSessionItem(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string CourseSlug,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    bool CanJoin,
    DateTime JoinOpensAtUtc);

public sealed record MyUpcomingSessionsResponse(DateTime ServerTimeUtc, IReadOnlyList<MyUpcomingSessionItem> Items);

/// <summary>The answer of the join gate. <b>One of the only two responses that carry the room URL</b> — it is sent with <c>Cache-Control: no-store</c>
/// and must not be logged, stored or forwarded by any caller.</summary>
public sealed record JoinLiveSessionResponse(
    Guid SessionId,
    string MeetUrl,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime ServerTimeUtc);

/// <summary>A generated calendar file for one session — never contains the room URL (it points back to the platform's join page).</summary>
public sealed record LiveCalendarFile(string FileName, string Content);

/// <summary>Which sessions an instructor's list shows.</summary>
public enum InstructorSessionScope
{
    /// <summary>Not finished and not cancelled, soonest first.</summary>
    Upcoming,

    /// <summary>Started before now (including cancelled ones), latest first.</summary>
    Past,

    /// <summary>Every session, latest first.</summary>
    All,
}

/// <summary>Which learners a session roster shows.</summary>
public enum RosterFilter
{
    All,
    Joined,
    NotJoined,
}

public sealed record InstructorSessionListItem(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    int ExpectedLearners,
    int JoinedLearners,
    InstructorMeetingSummary Meeting,
    Guid? RecordingEpisodeId,
    string? CancelReason,
    RecordingImportInfo RecordingImport);

/// <summary>The owning instructor's view of one session. <b>One of the only two responses that carry the room URL</b> (<see cref="MeetUrl"/>); sent with
/// <c>Cache-Control: no-store</c>.</summary>
/// <param name="MeetUrl"><c>null</c> while the session has no usable room.</param>
/// <param name="EnrolledCount">Learners who currently hold an active enrollment of the course.</param>
/// <param name="ExpectedLearners">Learners whose invitation is <c>Invited</c>.</param>
/// <param name="JoinedLearners">Distinct learners who were handed the room link.</param>
/// <param name="RecordingImport">How this session's recording gets onto the platform (P11-13): never a Drive id, asset id, URL or message.</param>
public sealed record InstructorSessionDetailResponse(
    Guid SessionId,
    Guid CourseId,
    string CourseTitle,
    string CourseSlug,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    string Status,
    string? CancelReason,
    Guid? RecordingEpisodeId,
    string? MeetUrl,
    InstructorMeetingSummary Meeting,
    int EnrolledCount,
    int ExpectedLearners,
    int JoinedLearners,
    DateTime ServerTimeUtc,
    RecordingImportInfo RecordingImport);

/// <summary>One learner on a session roster. Least privilege: only a masked e-mail (<c>a***@g***.com</c>) is ever returned, never the full address.</summary>
public sealed record RosterItem(
    Guid UserId,
    string? DisplayName,
    string? EmailMasked,
    string? InviteStatus,
    bool Joined,
    DateTime? FirstJoinedAtUtc,
    int JoinCount);
