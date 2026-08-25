using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Persistence boundary for the <see cref="ASSIGNMENT"/> aggregate. Same "Repository+Service"
/// shape as <see cref="IQuizRepository"/> — see that interface's own doc comment.</summary>
public interface IAssignmentRepository
{
    Task<ASSIGNMENT?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken);

    /// <summary>Loads the assignment attached to a given episode, or <c>null</c> if it has none.</summary>
    Task<ASSIGNMENT?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken);

    /// <summary>Stages a new assignment for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(ASSIGNMENT assignment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
