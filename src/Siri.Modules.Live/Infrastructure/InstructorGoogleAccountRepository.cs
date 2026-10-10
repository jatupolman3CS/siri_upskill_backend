using Microsoft.EntityFrameworkCore;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

public sealed class InstructorGoogleAccountRepository(AppDbContext context) : IInstructorGoogleAccountRepository
{
    public Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        context.InstructorGoogleAccounts()
            .FirstOrDefaultAsync(a => a.INSTRUCTOR_USER_ID == instructorUserId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetRecordingCandidateInstructorIdsAsync(CancellationToken cancellationToken)
    {
        var meetScope = GoogleScopes.MeetSpaceReadonly;
        var driveScope = GoogleScopes.DriveMeetReadonly;

        return await context.InstructorGoogleAccounts()
            .AsNoTracking()
            .Where(a => a.REFRESH_TOKEN_ENCRYPTED != null
                && a.REVOKED_AT_UTC == null
                && a.HOSTED_DOMAIN != null
                && a.HOSTED_DOMAIN != ""
                && a.SCOPES.Contains(meetScope)
                && a.SCOPES.Contains(driveScope))
            .Select(a => a.INSTRUCTOR_USER_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordValidationAsync(INSTRUCTOR_GOOGLE_ACCOUNT account, DateTime validatedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var accountId = account.INSTRUCTOR_GOOGLE_ACCOUNT_ID;

        // Set-based, so it is a single UPDATE of one column: no row version in the WHERE (a parallel refresher or a reconnect cannot make it fail), nothing queued on
        // the shared context (nothing for an unrelated SaveChanges to flush). A revoked account is not stamped - "validated" would be a lie. Like every ExecuteUpdate
        // it bypasses the auditing/concurrency interceptors, which is intended: this is bookkeeping, not an edit of the credential.
        await context.InstructorGoogleAccounts()
            .Where(a => a.INSTRUCTOR_GOOGLE_ACCOUNT_ID == accountId && a.REVOKED_AT_UTC == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.LAST_VALIDATED_AT_UTC, validatedAtUtc), cancellationToken)
            .ConfigureAwait(false);

        SyncTrackedValidation(context.Entry(account), validatedAtUtc);
    }

    /// <summary>Brings the tracked entity in line with what was just written to the database - as its <em>original</em> value, so the change tracker sees nothing to
    /// save. Other pending edits of the same entity are left exactly as they were.</summary>
    internal static void SyncTrackedValidation(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<INSTRUCTOR_GOOGLE_ACCOUNT> entry, DateTime validatedAtUtc)
    {
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        var property = entry.Property(a => a.LAST_VALIDATED_AT_UTC);
        property.CurrentValue = validatedAtUtc;
        property.OriginalValue = validatedAtUtc;
        property.IsModified = false;
    }

    public void Add(INSTRUCTOR_GOOGLE_ACCOUNT account) => context.InstructorGoogleAccounts().Add(account);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
