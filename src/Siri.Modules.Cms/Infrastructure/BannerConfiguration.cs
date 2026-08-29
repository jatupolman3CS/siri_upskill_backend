using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="BANNER"/> — see docs/DATABASE.md's "cms" section and <see cref="BANNER"/>'s
/// own doc comment for the UPPERCASE naming exception this follows (docs/DECISIONS.md D-17).
/// <para>
/// Most properties below need no explicit <c>HasColumnName</c> call: the C# property itself is already the
/// exact UPPERCASE name the column should have (e.g. <see cref="BANNER.PLACEMENT"/> → column
/// <c>PLACEMENT</c>) — which is exactly what EF's own default convention (column name = CLR property name)
/// already produces with zero configuration. The four <see cref="Siri.Persistence.Conventions.IAuditable"/>
/// properties at the bottom are the one place that default convention would get it wrong (PascalCase C#
/// name, UPPERCASE desired column) — see the explicit <c>HasColumnName</c> calls on those specifically.
/// This same reasoning applies to every other Cms <c>*Configuration</c> class in this module; only this one
/// spells it out in full.
/// </para>
/// </summary>
public sealed class BannerConfiguration : IEntityTypeConfiguration<BANNER>
{
    public void Configure(EntityTypeBuilder<BANNER> builder)
    {
        builder.ToTable("BANNERS", "CMS");

        builder.HasKey(b => b.BANNER_ID);

        builder.Property(b => b.PLACEMENT).HasMaxLength(100).IsRequired();
        builder.Property(b => b.IMAGE_URL).HasMaxLength(1000).IsRequired();
        builder.Property(b => b.MOBILE_IMAGE_URL).HasMaxLength(1000);
        builder.Property(b => b.LINK_URL).HasMaxLength(1000);
        builder.Property(b => b.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(b => b.SORT_ORDER).IsRequired();
        builder.Property(b => b.STARTS_AT_UTC).HasPrecision(3);
        builder.Property(b => b.ENDS_AT_UTC).HasPrecision(3);
        builder.Property(b => b.IS_ACTIVE).IsRequired();

        // Serves the eventual "active banners for this placement, in display order" public read
        // (Application.IBannerRepository.GetActiveByPlacementAsync) via leftmost-prefix.
        builder.HasIndex(b => new { b.PLACEMENT, b.IS_ACTIVE, b.SORT_ORDER });

        // IAuditable — C# property names stay PascalCase (see BANNER's own doc comment for why); only the
        // column names are UPPERCASE, so these need an explicit override the properties above don't.
        builder.Property(b => b.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(b => b.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(b => b.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(b => b.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
