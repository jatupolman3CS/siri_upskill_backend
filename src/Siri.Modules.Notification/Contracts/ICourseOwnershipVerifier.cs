namespace Siri.Modules.Notification.Contracts;

public interface ICourseOwnershipVerifier
{
    Task<bool> IsInstructorOwnerOfCourseAsync(
        Guid courseId,
        Guid instructorUserId,
        CancellationToken cancellationToken);
}
