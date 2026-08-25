using Siri.SharedKernel;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Persistence boundary for the <see cref="ASSIGNMENT_SUBMISSION"/> aggregate — a separate
/// aggregate from <see cref="ASSIGNMENT"/> (see that class's own doc comment). Same "Repository+Service"
/// shape as <see cref="IQuizRepository"/> — see that interface's own doc comment.</summary>
public interface IAssignmentSubmissionRepository
{
    Task<ASSIGNMENT_SUBMISSION?> GetByIdAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Submissions against a given assignment, offset-paginated (database.md: "รายการที่โตได้
    /// ต้อง paginate เสมอ" — same shape Catalog's admin review queues use), most recently submitted
    /// first.</summary>
    Task<PagedResult<ASSIGNMENT_SUBMISSION>> ListByAssignmentAsync(Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Stages a new submission for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(ASSIGNMENT_SUBMISSION submission);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
