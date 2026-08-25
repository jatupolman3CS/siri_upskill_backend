namespace Siri.Modules.Notification.Features.CreateAnnouncement;

public sealed record CreateAnnouncementCommand(
    Guid CourseId,
    string Title,
    string Body,
    bool SendEmail = false,
    DateTime? ScheduledAtUtc = null);

public sealed record AnnouncementResponse(
    Guid Id,
    Guid CourseId,
    Guid InstructorId,
    string Title,
    string Body,
    bool SendEmail,
    DateTime? ScheduledAtUtc,
    DateTime? SentAtUtc,
    int RecipientCount,
    DateTime CreatedAtUtc);
