using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="REDIRECT"/> — see docs/DATABASE.md's "cms" section and
/// <see cref="BANNER"/>'s own doc comment for the UPPERCASE naming exception this follows too
/// (docs/DECISIONS.md D-17); see <see cref="BannerConfiguration"/>'s own doc comment for why most
/// properties below need no explicit <c>HasColumnName</c>.
/// </summary>
public sealed class RedirectConfiguration : IEntityTypeConfiguration<REDIRECT>
{
    public void Configure(EntityTypeBuilder<REDIRECT> builder)
    {
        builder.ToTable("REDIRECTS", "cms");

        builder.HasKey(r => r.REDIRECT_ID);

        builder.Property(r => r.FROM_PATH).HasMaxLength(500).IsRequired();
        builder.HasIndex(r => r.FROM_PATH).IsUnique();

        builder.Property(r => r.TO_PATH).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.STATUS_CODE).IsRequired();

        // IAuditable — C# property names stay PascalCase (see BANNER's own doc comment for why); only the
        // column names are UPPERCASE, so these need an explicit override the properties above don't.
        builder.Property(r => r.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(r => r.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(r => r.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(r => r.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
