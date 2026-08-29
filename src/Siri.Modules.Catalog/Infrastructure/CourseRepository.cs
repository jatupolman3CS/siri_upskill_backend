using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CourseRepository(AppDbContext dbContext) : ICourseRepository
{
    public Task<COURSE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Courses().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<COURSE?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Courses().FirstOrDefaultAsync(c => c.Slug == slug, cancellationToken);

    public Task<COURSE?> GetWithDetailsByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .Include(c => c.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Episodes.OrderBy(e => e.SortOrder))
            .Include(c => c.Outcomes.OrderBy(o => o.SortOrder))
            .Include(c => c.Requirements.OrderBy(r => r.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<COURSE?> GetWithDetailsBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .Include(c => c.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Episodes.OrderBy(e => e.SortOrder))
            .Include(c => c.Outcomes.OrderBy(o => o.SortOrder))
            .Include(c => c.Requirements.OrderBy(r => r.SortOrder))
            .FirstOrDefaultAsync(c => c.Slug == slug, cancellationToken);

    public Task<COURSE?> GetBuilderByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Courses()
            .Include(c => c.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Episodes.OrderBy(e => e.SortOrder))
            .Include(c => c.Outcomes.OrderBy(o => o.SortOrder))
            .Include(c => c.Requirements.OrderBy(r => r.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<COURSE>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
        await dbContext.Courses()
            .Where(c => c.InstructorId == instructorId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<COURSE>> GetPendingModerationAsync(CancellationToken cancellationToken) =>
        await dbContext.Courses()
            .Where(c => c.Status == CourseStatus.InReview)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(COURSE course, CancellationToken cancellationToken)
    {
        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(COURSE course, CancellationToken cancellationToken)
    {
        dbContext.Courses().Update(course);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(COURSE course, CancellationToken cancellationToken)
    {
        dbContext.Courses().Remove(course);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation().ConfigureAwait(false);
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
