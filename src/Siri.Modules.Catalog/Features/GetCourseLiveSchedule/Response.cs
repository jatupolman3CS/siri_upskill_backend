using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLiveSession;

namespace Siri.Modules.Catalog.Features.GetCourseLiveSchedule;

/// <summary>
/// Everything the course builder's live-schedule panel needs to load in one call (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.5):
/// the course's delivery format and status, its enrollment policy, the Google attendee-sync opt-in, and every session in every status (cancelled ones included),
/// ordered by start time. <b>Carries no room link</b> — the state of each room lives at <c>GET /api/live/instructor/courses/{id}/meetings</c>.
/// </summary>
public sealed record CourseLiveScheduleResponse(
    Guid CourseId,
    DeliveryFormat DeliveryFormat,
    CourseStatus Status,
    DateTime? EnrollmentDeadlineUtc,
    int? MaxSeats,
    int SeatsUsed,
    bool GoogleAttendeeSyncEnabled,
    IReadOnlyList<LiveSessionResponse> Sessions);
