using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="MENU_ITEM"/> — see docs/DATABASE.md's "cms" section and
/// <see cref="BANNER"/>'s own doc comment for the UPPERCASE naming exception this follows too
/// (docs/DECISIONS.md D-17); see <see cref="BannerConfiguration"/>'s own doc comment for why most
/// properties below need no explicit <c>HasColumnName</c>.
/// </summary>
public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MENU_ITEM>
{
    public void Configure(EntityTypeBuilder<MENU_ITEM> builder)
    {
        builder.ToTable("MENU_ITEMS", "cms");

        builder.HasKey(m => m.MENU_ITEM_ID);

        builder.Property(m => m.LABEL).HasMaxLength(100).IsRequired();
        builder.Property(m => m.URL).HasMaxLength(1000).IsRequired();
        builder.Property(m => m.SORT_ORDER).IsRequired();
        builder.Property(m => m.IS_ACTIVE).IsRequired();

        // Self-referencing FK. Restrict, not Cascade: SQL Server rejects ON DELETE CASCADE on a
        // self-referencing FK outright ("may cause cycles or multiple cascade paths"), and semantically a
        // menu item with children should not be deletable anyway. Same precedent
        // Siri.Modules.Catalog.Infrastructure.CategoryConfiguration's own comment explains in full for
        // Category.ParentId — a future Delete operation is expected to check for children itself, with this
        // as the DB-level backstop for the race where a child is inserted between that check and the
        // delete's own SaveChangesAsync.
        builder.HasOne<MENU_ITEM>()
            .WithMany()
            .HasForeignKey(m => m.PARENT_ID)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves both the flat tree query and sibling lookups (reorder validation, the delete "has
        // children" guard) — same shape CategoryConfiguration's (ParentId, SortOrder) index serves for
        // Category.
        builder.HasIndex(m => new { m.PARENT_ID, m.SORT_ORDER });

        // IAuditable — C# property names stay PascalCase (see BANNER's own doc comment for why); only the
        // column names are UPPERCASE, so these need an explicit override the properties above don't.
        builder.Property(m => m.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(m => m.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(m => m.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(m => m.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
