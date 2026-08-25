namespace Siri.Persistence.Conventions;

/// <summary>
/// Marks an entity as soft-deletable: <c>SaveChanges</c> never issues a SQL <c>DELETE</c> for these
/// (see database.md — "ห้าม Hard delete ข้อมูลการเงินหรือสิทธิ์เรียน — ใช้สถานะ"), and
/// <see cref="ModelBuilderExtensions.ApplySoftDeleteQueryFilter"/> adds a global query filter so
/// deleted rows never show up in normal queries. Modules still choose their own richer status enum
/// (Cancelled/Refunded/Revoked, ...) where the domain calls for one; this is the generic fallback.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }
}
