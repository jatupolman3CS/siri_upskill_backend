using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="ORDER"/> — see docs/DATABASE.md's "commerce" section and
/// docs/DECISIONS.md D-17 (UPPERCASE table/column names, this module's own scoped exception).</summary>
public sealed class ORDERConfiguration : IEntityTypeConfiguration<ORDER>
{
    public void Configure(EntityTypeBuilder<ORDER> builder)
    {
        builder.ToTable("ORDERS", "COMMERCE");

        builder.HasKey(o => o.ORDER_ID);

        builder.Property(o => o.ORDER_NO).HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.ORDER_NO).IsUnique();

        // Conceptual FK to identity.Users.Id — cross-module/schema, never a real DB FK constraint (same
        // reasoning as every cross-module user reference in this scaffold pass).
        builder.Property(o => o.USER_ID).IsRequired();

        builder.Property(o => o.SUBTOTAL_AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.DISCOUNT_AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.TAX_AMOUNT).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.TOTAL_AMOUNT).HasPrecision(18, 2).IsRequired();

        // char(3), not nvarchar — database.md's explicit carve-out from the nvarchar-always rule for
        // ISO 4217 currency codes (matches CourseConfiguration.Currency).
        builder.Property(o => o.CURRENCY).HasColumnType("char(3)").IsRequired();

        builder.Property(o => o.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Conceptual FK to PROMO_CODES.PROMO_CODE_ID — deliberately no real FK constraint even though
        // it is same-schema/same-module (unlike every other "conceptual FK" comment in this scaffold
        // pass, which is about cross-module/polymorphic references): an order must never become
        // unreadable/unrestorable because a promo code row was touched, and a promo code redeemed once
        // must remain a permanent, immutable fact about that order's history regardless of what happens
        // to the code afterward — enforcing it with a real FK would require deciding a cascade behavior
        // (Restrict here would block legitimate promo-code lifecycle operations for no real benefit,
        // since PROMO_CODES also has no delete method — see that entity's own doc comment).
        builder.Property(o => o.PROMO_CODE_ID);

        builder.Property(o => o.PAID_AT_UTC).HasPrecision(3);

        builder.Property(o => o.ROW_VERSION).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        builder.Property(o => o.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(o => o.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(o => o.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(o => o.UpdatedBy).HasColumnName("UPDATED_BY");

        // docs/DATABASE.md's day-one index list: IX_Orders_UserHistory (UserId, CreatedAtUtc DESC).
        builder.HasIndex(o => new { o.USER_ID, o.CreatedAtUtc }).HasDatabaseName("IX_ORDERS_USER_HISTORY");

        // Real (List<T>-backed) navigation must be wired explicitly — never rely on convention discovery
        // (this codebase already hit a shadow-FK bug once doing exactly this with Course.Sections; see
        // that migration's own history in CLAUDE.md).
        builder.Navigation(o => o.ORDER_ITEMS).HasField("_orderItems").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
