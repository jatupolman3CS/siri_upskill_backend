using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>EF Core mapping for <see cref="MEDIA_UPLOAD_SESSION"/> — see docs/DATABASE.md's "media"
/// section, and <see cref="MEDIA_ASSET"/>'s own doc comment for the UPPERCASE naming exception (D-17).</summary>
public sealed class MEDIA_UPLOAD_SESSIONConfiguration : IEntityTypeConfiguration<MEDIA_UPLOAD_SESSION>
{
    public void Configure(EntityTypeBuilder<MEDIA_UPLOAD_SESSION> builder)
    {
        builder.ToTable("MEDIA_UPLOAD_SESSIONS", "MEDIA");

        builder.HasKey(s => s.MEDIA_UPLOAD_SESSION_ID);
        builder.Property(s => s.MEDIA_UPLOAD_SESSION_ID).HasColumnName("MEDIA_UPLOAD_SESSION_ID");

        builder.Property(s => s.MEDIA_ASSET_ID).HasColumnName("MEDIA_ASSET_ID").IsRequired();

        // Cascade — the real ownership edge, a pure child of its asset (see this entity's own doc
        // comment). Neither side has a navigation property (mirrors Category/InstructorProfile's
        // FK-column-only style, not Course/CourseSection's navigation-collection style), so a bare
        // .WithMany() is correct here, not a shadow-FK risk (that only happens when a real navigation
        // property exists somewhere and isn't referenced — see CourseConfiguration's own note on this).
        builder.HasOne<MEDIA_ASSET>()
            .WithMany()
            .HasForeignKey(s => s.MEDIA_ASSET_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.UPLOAD_URL).HasColumnName("UPLOAD_URL").HasMaxLength(1000).IsRequired();
        builder.Property(s => s.EXPIRES_AT_UTC).HasColumnName("EXPIRES_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(s => s.STATUS).HasColumnName("STATUS").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(s => s.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(s => s.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(s => s.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
