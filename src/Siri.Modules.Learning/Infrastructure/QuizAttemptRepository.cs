using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class QuizAttemptRepository(AppDbContext dbContext) : IQuizAttemptRepository
{
    public Task<QUIZ_ATTEMPT?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
        dbContext.QuizAttempts()
            .Include(a => a.ANSWERS)
            .FirstOrDefaultAsync(a => a.QUIZ_ATTEMPT_ID == attemptId, cancellationToken);

    public async Task<IReadOnlyList<QUIZ_ATTEMPT>> ListByEnrollmentAndQuizAsync(Guid enrollmentId, Guid quizId, CancellationToken cancellationToken) =>
        await dbContext.QuizAttempts()
            .Include(a => a.ANSWERS)
            .Where(a => a.ENROLLMENT_ID == enrollmentId && a.QUIZ_ID == quizId)
            .OrderBy(a => a.ATTEMPT_NO)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(QUIZ_ATTEMPT attempt) => dbContext.QuizAttempts().Add(attempt);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
