using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Persistence.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> entities with Created/Updated metadata on every
/// <c>SaveChanges</c>/<c>SaveChangesAsync</c>, and converts a hard delete of an
/// <see cref="ISoftDelete"/> entity into a flag update instead — matching database.md's
/// "ห้าม Hard delete ข้อมูลการเงินหรือสิทธิ์เรียน — ใช้สถานะ" rule at the persistence layer so
/// no module has to remember to do it by hand.
/// Values are written through <c>EntityEntry.Property(name).CurrentValue</c> (EF's change-tracker
/// metadata), not the interface accessor, so entities may keep these setters non-public.
/// </summary>
public sealed class AuditableEntityInterceptor(IClock clock, IUserContext userContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var userId = userContext.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditable.CreatedAtUtc)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = userId;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(IAuditable.UpdatedAtUtc)).CurrentValue = now;
                    entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = userId;
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Property(nameof(ISoftDelete.IsDeleted)).CurrentValue = true;
            entry.Property(nameof(ISoftDelete.DeletedAtUtc)).CurrentValue = now;
        }
    }
}
