using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>EF Core mapping for <see cref="MEDIA_ASSET"/> — see docs/DATABASE.md's "media" section, and
/// that entity's own doc comment for the UPPERCASE table/column naming exception (D-17).</summary>
public sealed class MEDIA_ASSETConfiguration : IEntityTypeConfiguration<MEDIA_ASSET>
{
    public void Configure(EntityTypeBuilder<MEDIA_ASSET> builder)
    {
        builder.ToTable("MEDIA_ASSETS", "MEDIA");

        builder.HasKey(a => a.MEDIA_ASSET_ID);
        builder.Property(a => a.MEDIA_ASSET_ID).HasColumnName("MEDIA_ASSET_ID");

        builder.Property(a => a.PROVIDER).HasColumnName("PROVIDER").HasMaxLength(50).IsRequired();
        builder.Property(a => a.PROVIDER_ASSET_ID).HasColumnName("PROVIDER_ASSET_ID").HasMaxLength(200).IsRequired();

        // docs/DATABASE.md: "IX(Provider, ProviderAssetId) UQ" — one asset row per provider-side id.
        builder.HasIndex(a => new { a.PROVIDER, a.PROVIDER_ASSET_ID }).IsUnique();

        builder.Property(a => a.PLAYBACK_ID).HasColumnName("PLAYBACK_ID").HasMaxLength(200);

        builder.Property(a => a.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(a => a.DURATION_SECONDS).HasColumnName("DURATION_SECONDS");
        builder.Property(a => a.DRM_ENABLED).HasColumnName("DRM_ENABLED").IsRequired();
        builder.Property(a => a.THUMBNAIL_URL).HasColumnName("THUMBNAIL_URL").HasMaxLength(1000);

        // No FK constraint — cross-module (identity.Users), see MEDIA_ASSET's own doc comment.
        builder.Property(a => a.UPLOADED_BY_USER_ID).HasColumnName("UPLOADED_BY_USER_ID").IsRequired();

        builder.Property(a => a.READY_AT_UTC).HasColumnName("READY_AT_UTC").HasPrecision(3);
        builder.Property(a => a.ERROR_MESSAGE).HasColumnName("ERROR_MESSAGE").HasMaxLength(2000);

        // IAuditable properties stay PascalCase in C# (see MEDIA_ASSET's own doc comment) — only the
        // mapped column name goes uppercase, same split Course/InstructorProfile already use.
        builder.Property(a => a.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(a => a.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
