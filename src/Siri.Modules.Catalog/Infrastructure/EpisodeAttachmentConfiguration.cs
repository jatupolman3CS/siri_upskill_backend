using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class EpisodeAttachmentConfiguration : IEntityTypeConfiguration<EPISODE_ATTACHMENT>
{
    public void Configure(EntityTypeBuilder<EPISODE_ATTACHMENT> builder)
    {
        builder.ToTable("EPISODE_ATTACHMENTS", "CATALOG");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.EpisodeId).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.SizeBytes).IsRequired();

        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.CreatedBy);
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.UpdatedBy);

        // GetEpisodeAttachmentsHandler filters Where(EpisodeId == ...) then OrderBy(CreatedAtUtc) — solo
        // EpisodeId index below doesn't cover the ORDER BY. Low-traffic (attachments per episode are
        // inherently few) but bundled into the same migration as the CourseReview/InstructorProfile index
        // fixes since it's already being touched.
        builder.HasIndex(a => new { a.EpisodeId, a.CreatedAtUtc });

        builder.HasOne<COURSE_EPISODE>()
            .WithMany()
            .HasForeignKey(a => a.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
