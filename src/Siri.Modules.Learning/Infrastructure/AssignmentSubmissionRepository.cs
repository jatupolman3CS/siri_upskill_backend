using Microsoft.EntityFrameworkCore;
using Siri.SharedKernel;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class AssignmentSubmissionRepository(AppDbContext dbContext) : IAssignmentSubmissionRepository
{
    public Task<ASSIGNMENT_SUBMISSION?> GetByIdAsync(Guid submissionId, CancellationToken cancellationToken) =>
        dbContext.AssignmentSubmissions().FirstOrDefaultAsync(s => s.ASSIGNMENT_SUBMISSION_ID == submissionId, cancellationToken);

    public Task<ASSIGNMENT_SUBMISSION?> GetLatestByEnrollmentAndAssignmentAsync(Guid enrollmentId, Guid assignmentId, CancellationToken cancellationToken) =>
        dbContext.AssignmentSubmissions()
            .Where(s => s.ENROLLMENT_ID == enrollmentId && s.ASSIGNMENT_ID == assignmentId)
            .OrderByDescending(s => s.SUBMITTED_AT_UTC)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<ASSIGNMENT_SUBMISSION>> ListByAssignmentAsync(Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.AssignmentSubmissions().Where(s => s.ASSIGNMENT_ID == assignmentId);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(s => s.SUBMITTED_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<ASSIGNMENT_SUBMISSION>.Create(items, totalCount, page, pageSize);
    }

    public void Add(ASSIGNMENT_SUBMISSION submission) => dbContext.AssignmentSubmissions().Add(submission);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
