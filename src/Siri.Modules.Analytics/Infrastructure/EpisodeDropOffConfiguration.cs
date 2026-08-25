using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Analytics.Domain;

namespace Siri.Modules.Analytics.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="EPISODE_DROP_OFF"/> — see docs/DATABASE.md's "analytics" section and
/// <see cref="DAILY_COURSE_STAT"/>'s own doc comment for the module-wide UPPERCASE naming exception
/// (docs/DECISIONS.md D-17).
/// </summary>
public sealed class EpisodeDropOffConfiguration : IEntityTypeConfiguration<EPISODE_DROP_OFF>
{
    public void Configure(EntityTypeBuilder<EPISODE_DROP_OFF> builder)
    {
        builder.ToTable("EPISODE_DROP_OFF", "ANALYTICS");

        // Composite PK, no synthetic Id column — same "always upserted by the nightly job" shape as
        // DAILY_COURSE_STAT.
        builder.HasKey(x => new { x.DATE, x.EPISODE_ID });

        builder.Property(x => x.START_COUNT).IsRequired();
        builder.Property(x => x.COMPLETE_COUNT).IsRequired();
        // decimal(5,2) — same 0.00-100.00 percentage reasoning as DAILY_COURSE_STAT.COMPLETION_RATE.
        builder.Property(x => x.AVG_WATCH_PERCENT).HasPrecision(5, 2).IsRequired();

        // IEpisodeDropOffRepository.GetForEpisodeAsync queries by (EPISODE_ID, DATE range) — same
        // leftmost-prefix reasoning as DailyCourseStatConfiguration's own index comment (the composite PK
        // here is keyed (DATE, EPISODE_ID), not (EPISODE_ID, DATE)).
        builder.HasIndex(x => new { x.EPISODE_ID, x.DATE });
    }
}
