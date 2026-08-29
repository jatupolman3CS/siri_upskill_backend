namespace Siri.Modules.Catalog.Contracts;

public interface ILearningEnrollmentChecker
{
    Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken);
}
