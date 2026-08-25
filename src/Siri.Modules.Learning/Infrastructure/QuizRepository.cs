using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class QuizRepository(AppDbContext dbContext) : IQuizRepository
{
    public Task<QUIZ?> GetByIdAsync(Guid quizId, CancellationToken cancellationToken) =>
        dbContext.Quizzes()
            .Include(q => q.QUESTIONS)
            .ThenInclude(q => q.OPTIONS)
            .FirstOrDefaultAsync(q => q.QUIZ_ID == quizId, cancellationToken);

    public Task<QUIZ?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
        dbContext.Quizzes()
            .Include(q => q.QUESTIONS)
            .ThenInclude(q => q.OPTIONS)
            .FirstOrDefaultAsync(q => q.EPISODE_ID == episodeId, cancellationToken);

    public void Add(QUIZ quiz) => dbContext.Quizzes().Add(quiz);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
