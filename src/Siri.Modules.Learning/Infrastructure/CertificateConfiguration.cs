using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="CERTIFICATE"/> — see docs/DATABASE.md's "learning" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="CERTIFICATE.ENROLLMENT_ID"/> is a real (<c>NoAction</c>) FK to <see cref="ENROLLMENT"/>,
/// unique (one certificate per enrollment) — see <see cref="CERTIFICATE"/>'s own doc comment for why this
/// stays <c>NoAction</c> rather than <see cref="EPISODE_PROGRESS"/>'s <c>Cascade</c>.
/// </para>
/// </summary>
public sealed class CertificateConfiguration : IEntityTypeConfiguration<CERTIFICATE>
{
    public void Configure(EntityTypeBuilder<CERTIFICATE> builder)
    {
        builder.ToTable("CERTIFICATES", "LEARNING");

        builder.HasKey(x => x.CERTIFICATE_ID);
        builder.Property(x => x.CERTIFICATE_ID).HasColumnName("CERTIFICATE_ID");

        builder.Property(x => x.ENROLLMENT_ID).HasColumnName("ENROLLMENT_ID").IsRequired();
        // NoAction — see CERTIFICATE's own doc comment for why (money/entitlement-adjacent record,
        // CLAUDE.md ground rule #5 names this table explicitly).
        builder.HasOne<ENROLLMENT>()
            .WithMany()
            .HasForeignKey(x => x.ENROLLMENT_ID)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.ENROLLMENT_ID).IsUnique().HasDatabaseName("IX_CERTIFICATES_ENROLLMENT_ID");

        builder.Property(x => x.SERIAL_NO).HasColumnName("SERIAL_NO").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.SERIAL_NO).IsUnique().HasDatabaseName("IX_CERTIFICATES_SERIAL_NO");

        builder.Property(x => x.VERIFY_CODE).HasColumnName("VERIFY_CODE").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.VERIFY_CODE).IsUnique().HasDatabaseName("IX_CERTIFICATES_VERIFY_CODE");

        builder.Property(x => x.ISSUED_AT_UTC).HasColumnName("ISSUED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.PDF_STORAGE_KEY).HasColumnName("PDF_STORAGE_KEY").HasMaxLength(500);
        builder.Property(x => x.REVOKED_AT_UTC).HasColumnName("REVOKED_AT_UTC").HasPrecision(3);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
