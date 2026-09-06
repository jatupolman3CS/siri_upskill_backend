using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Siri.Persistence.Interceptors;

/// <summary>
/// Gives every <c>byte[]</c> concurrency token a fresh value on insert and on update.
/// <para>
/// SQL Server did this for us: <c>IsRowVersion()</c> mapped to the <c>rowversion</c> type, which the
/// engine itself bumps on every write. PostgreSQL has no equivalent column type, so the rotation has
/// to happen here (task P0-41). The obvious Npgsql alternative — the system <c>xmin</c> column — was
/// rejected deliberately: it is a <c>uint</c>, and these tokens are part of a public API contract.
/// <c>GetCourseBuilder</c>/<c>AutosaveCourse</c> hand the token to the Angular course builder as a
/// base64 string (<c>readonly rowVersion: string</c> in <c>course-api.models.ts</c>); switching to a
/// number would break that client for no gain. Keeping <c>byte[]</c> keeps the wire format identical
/// to what SQL Server produced.
/// </para>
/// <para>
/// Only <see cref="Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty.IsConcurrencyToken"/>
/// properties typed <c>byte[]</c> are touched, so this is opt-in per entity through each
/// <c>IEntityTypeConfiguration</c> — nothing changes for entities without a token.
/// </para>
/// <para>
/// <b>Only <c>CurrentValue</c> is written, never <c>OriginalValue</c>.</b> EF builds the
/// <c>UPDATE ... WHERE</c> predicate from the original value, so that is precisely the conflict
/// check; overwriting it would silently disable optimistic concurrency everywhere. It also has to
/// stay writable by callers: <c>AutosaveCourseHandler</c> assigns <c>OriginalValue</c> from the token
/// the client echoed back, which is how a stale editor gets a 409 instead of clobbering newer work.
/// </para>
/// <para>
/// Values are written through <c>EntityEntry.Property(name).CurrentValue</c> rather than the CLR
/// setter, so domain entities keep their <c>private set</c> — same technique
/// <see cref="AuditableEntityInterceptor"/> already uses. Register this interceptor <em>after</em>
/// that one: it converts a delete of an <c>ISoftDelete</c> entity into a <c>Modified</c> entry, and
/// running second means those rows rotate their token too.
/// </para>
/// </summary>
public sealed class ConcurrencyTokenInterceptor : SaveChangesInterceptor
{
    /// <summary>Matches the 8 bytes SQL Server's <c>rowversion</c> produced, so persisted tokens and
    /// the base64 strings clients round-trip stay the same size as before the migration.</summary>
    public const int TokenLengthBytes = 8;

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

    internal static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Metadata.GetProperties())
            {
                if (!property.IsConcurrencyToken || property.ClrType != typeof(byte[]))
                {
                    continue;
                }

                entry.Property(property.Name).CurrentValue = RandomNumberGenerator.GetBytes(TokenLengthBytes);
            }
        }
    }
}
