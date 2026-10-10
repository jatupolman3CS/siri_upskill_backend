using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

public sealed class SessionRecordingImportRepository(AppDbContext context) : ISessionRecordingImportRepository
{
    public async Task<SESSION_RECORDING_IMPORT?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // A row added earlier in the same unit of work is not in the database yet — look at the tracker first.
        var local = context.SessionRecordingImports().Local.FirstOrDefault(i => i.SESSION_ID == sessionId);
        if (local is not null)
        {
            return local;
        }

        // Uses IX_SESSION_RECORDING_IMPORTS_SESSION_ID.
        return await context.SessionRecordingImports()
            .FirstOrDefaultAsync(i => i.SESSION_ID == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<SESSION_RECORDING_IMPORT?> GetByIdAsync(Guid importId, CancellationToken cancellationToken) =>
        context.SessionRecordingImports()
            .FirstOrDefaultAsync(i => i.SESSION_RECORDING_IMPORT_ID == importId, cancellationToken);

    public async Task<IReadOnlyList<SESSION_RECORDING_IMPORT>> GetBySessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await context.SessionRecordingImports()
            .AsNoTracking()
            .Where(i => ids.Contains(i.SESSION_ID))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var existing = await context.SessionRecordingImports()
            .AsNoTracking()
            .Where(i => ids.Contains(i.SESSION_ID))
            .Select(i => i.SESSION_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return existing.ToHashSet();
    }

    public async Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        // Uses IX_SESSION_RECORDING_IMPORTS_DUE (STATUS, NEXT_ATTEMPT_AT_UTC); the Transferring branch is a handful of rows by construction.
        return await context.SessionRecordingImports()
            .AsNoTracking()
            .Where(i =>
                ((i.STATUS == RecordingImportStatus.Waiting || i.STATUS == RecordingImportStatus.Processing)
                    && i.NEXT_ATTEMPT_AT_UTC != null && i.NEXT_ATTEMPT_AT_UTC <= nowUtc)
                || (i.STATUS == RecordingImportStatus.Transferring && i.LEASE_UNTIL_UTC != null && i.LEASE_UNTIL_UTC <= nowUtc))
            .OrderBy(i => i.CreatedAtUtc)
            .ThenBy(i => i.SESSION_RECORDING_IMPORT_ID)
            .Take(Math.Max(take, 1))
            .Select(i => i.SESSION_RECORDING_IMPORT_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(SESSION_RECORDING_IMPORT import) => context.SessionRecordingImports().Add(import);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => context.ChangeTracker.Clear();
}
