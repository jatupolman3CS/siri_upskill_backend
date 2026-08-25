namespace Siri.Persistence.Conventions;

/// <summary>
/// Marks an entity as wanting Created/Updated audit stamps applied automatically by
/// <see cref="Interceptors.AuditableEntityInterceptor"/> on every <c>SaveChanges</c>.
/// <para>
/// The interceptor writes these via EF's change-tracker property metadata
/// (<c>entry.Property(name).CurrentValue = ...</c>), not through the interface accessor, so an
/// implementing entity may keep its setters <c>private</c> (per backend.md's invariant-protection
/// rule) and still satisfy this contract by implementing it explicitly.
/// </para>
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }

    Guid? CreatedBy { get; set; }

    DateTime? UpdatedAtUtc { get; set; }

    Guid? UpdatedBy { get; set; }
}
