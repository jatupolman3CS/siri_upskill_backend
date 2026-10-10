using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="LIVE_SESSION_ATTACHMENT"/> — same column shape as
/// <see cref="EpisodeAttachmentConfiguration"/>, keyed by <c>SessionId</c> instead of <c>EpisodeId</c>.</summary>
public sealed class LiveSessionAttachmentConfiguration : IEntityTypeConfiguration<LIVE_SESSION_ATTACHMENT>
{
    public void Configure(EntityTypeBuilder<LIVE_SESSION_ATTACHMENT> builder)
    {
        builder.ToTable("LIVE_SESSION_ATTACHMENTS", "CATALOG");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.SessionId).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.SizeBytes).IsRequired();

        builder.Property(a => a.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(a => a.CreatedBy);
        builder.Property(a => a.UpdatedAtUtc).HasPrecision(3);
        builder.Property(a => a.UpdatedBy);

        // The list handler filters by SessionId and orders by CreatedAtUtc; the composite also serves a plain
        // "does this session have files" lookup through its leftmost column.
        builder.HasIndex(a => new { a.SessionId, a.CreatedAtUtc });

        // Cascade is safe here (unlike money/entitlement tables): the file is plain content, and sessions are
        // only ever Cancelled, never deleted, so this only fires if a COURSE is hard-deleted outside the soft-delete
        // path — in which case the rows should go with it (the R2 objects are then orphaned; see TeachingMaterialStorage).
        builder.HasOne<COURSE_LIVE_SESSION>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
