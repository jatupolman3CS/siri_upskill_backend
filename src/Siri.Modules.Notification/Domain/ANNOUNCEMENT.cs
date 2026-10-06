using Siri.SharedKernel;

namespace Siri.Modules.Notification.Domain;

/// <summary>
/// An announcement broadcast to enrolled learners of a course by the instructor (P4-27, P6-05).
/// </summary>
public sealed class ANNOUNCEMENT
{
    private ANNOUNCEMENT()
    {
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid InstructorId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public bool SendEmail { get; private set; }
    public DateTime? ScheduledAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public int RecipientCount { get; private set; }
    public AnnouncementDispatchStatus DispatchStatus { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static ANNOUNCEMENT Create(
        Guid courseId,
        Guid instructorId,
        string title,
        string body,
        bool sendEmail,
        DateTime? scheduledAtUtc,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new ANNOUNCEMENT
        {
            Id = UuidV7.NewId(),
            CourseId = courseId,
            InstructorId = instructorId,
            Title = title,
            Body = body,
            SendEmail = sendEmail,
            ScheduledAtUtc = scheduledAtUtc,
            SentAtUtc = null,
            RecipientCount = 0,
            DispatchStatus = AnnouncementDispatchStatus.Pending,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    public void MarkSent(int recipientCount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        SentAtUtc = clock.UtcNow;
        RecipientCount = recipientCount;
        DispatchStatus = AnnouncementDispatchStatus.Sent;
    }
}
