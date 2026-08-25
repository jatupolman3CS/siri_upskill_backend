using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="Category"/> — see docs/DATABASE.md's "catalog" section.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", "catalog");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Slug).HasMaxLength(100).IsRequired();
        // Unique, and case-insensitive for free: the DB's documented collation
        // (Thai_100_CI_AS_SC_UTF8 — "CI" = case-insensitive) applies to this index with no override
        // needed, matching the validator's lowercase-only slug format.
        builder.HasIndex(c => c.Slug).IsUnique();

        builder.Property(c => c.NameTh).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(c => c.IconKey).HasMaxLength(100);

        builder.Property(c => c.SortOrder).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        // Self-referencing FK. Restrict (not Cascade, and not merely "the default"): SQL Server
        // rejects ON DELETE CASCADE on a self-referencing FK outright ("may cause cycles or multiple
        // cascade paths"), and semantically a parent-with-children must never be deletable anyway —
        // the Delete handler checks this explicitly; Restrict is the DB-level backstop for the race
        // where a child is inserted between that check and the delete's own SaveChangesAsync.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves both the flat tree query (ordered by parent then sibling order) and sibling lookups
        // (Reorder's "load this parent's current siblings", Delete's "does this have children").
        builder.HasIndex(c => new { c.ParentId, c.SortOrder });

        builder.Property(c => c.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasPrecision(3);
    }
}
