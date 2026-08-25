using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="EPISODE_PROGRESS"/> — see docs/DATABASE.md's "learning" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md). Table name is deliberately
/// <c>EPISODE_PROGRESS</c>, not <c>EPISODE_PROGRESSES</c> — see <see cref="EPISODE_PROGRESS"/>'s own doc
/// comment.
/// <para>
/// <see cref="EPISODE_PROGRESS.ENROLLMENT_ID"/> is this module's one <c>Cascade</c> FK to
/// <see cref="ENROLLMENT"/> — see <see cref="EPISODE_PROGRESS"/>'s own doc comment for why (true
/// composition child, same reasoning <c>Siri.Modules.Payout.Domain.PAYOUT_BATCH_ITEM.BATCH_ID</c>'s FK
/// uses in its own module). <see cref="EPISODE_PROGRESS.EPISODE_ID"/> gets no <c>HasOne()</c>/FK at all —
/// cross-module/cross-schema to <c>catalog.CourseEpisodes</c>.
/// </para>
/// <para>
/// One index, not two — the unique constraint on <c>(EnrollmentId, EpisodeId)</c> already has
/// <c>EnrollmentId</c> as its leftmost column, so it already serves docs/DATABASE.md's day-one
/// <c>IX_EpisodeProgress_Resume (EnrollmentId) INCLUDE (EpisodeId, LastPositionSeconds, IsCompleted)</c>
/// query pattern on its own (<c>EpisodeId</c> is already a full index key column here, which covers it at
/// least as well as an <c>INCLUDE</c> would) — adding <see cref="EPISODE_PROGRESS.LAST_POSITION_SECONDS"/>/
/// <see cref="EPISODE_PROGRESS.IS_COMPLETED"/> as <c>INCLUDE</c> columns on this same unique index makes it
/// fully covering for that "resume" read too, so no second index is needed. Same "one richer index instead
/// of two overlapping ones" consolidation <c>CourseConfiguration</c>'s own doc comment already establishes
/// for its own day-one index (contrast <c>EnrollmentConfiguration</c>'s own doc comment, where the day-one
/// index does NOT overlap enough with the inline ones to consolidate).
/// </para>
/// </summary>
public sealed class EpisodeProgressConfiguration : IEntityTypeConfiguration<EPISODE_PROGRESS>
{
    public void Configure(EntityTypeBuilder<EPISODE_PROGRESS> builder)
    {
        builder.ToTable("EPISODE_PROGRESS", "LEARNING");

        builder.HasKey(x => x.EPISODE_PROGRESS_ID);
        builder.Property(x => x.EPISODE_PROGRESS_ID).HasColumnName("EPISODE_PROGRESS_ID");

        builder.Property(x => x.ENROLLMENT_ID).HasColumnName("ENROLLMENT_ID").IsRequired();
        // Cascade — see this class's own doc comment.
        builder.HasOne<ENROLLMENT>()
            .WithMany()
            .HasForeignKey(x => x.ENROLLMENT_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.EPISODE_ID).HasColumnName("EPISODE_ID").IsRequired();

        builder.Property(x => x.LAST_POSITION_SECONDS).HasColumnName("LAST_POSITION_SECONDS").IsRequired();
        builder.Property(x => x.WATCHED_SECONDS).HasColumnName("WATCHED_SECONDS").IsRequired();
        builder.Property(x => x.IS_COMPLETED).HasColumnName("IS_COMPLETED").IsRequired();
        builder.Property(x => x.COMPLETED_AT_UTC).HasColumnName("COMPLETED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UPDATED_AT_UTC).HasColumnName("UPDATED_AT_UTC").HasPrecision(3).IsRequired();

        // Unique per (enrollment, episode) AND this module's "resume" covering index — see this class's
        // own doc comment for why one index serves both purposes here.
        builder.HasIndex(x => new { x.ENROLLMENT_ID, x.EPISODE_ID })
            .IsUnique()
            .IncludeProperties(x => new { x.LAST_POSITION_SECONDS, x.IS_COMPLETED })
            .HasDatabaseName("IX_EPISODE_PROGRESS_RESUME");
    }
}
