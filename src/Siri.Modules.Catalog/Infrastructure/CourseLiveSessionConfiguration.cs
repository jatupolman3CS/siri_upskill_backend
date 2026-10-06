using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="COURSE_LIVE_SESSION"/> — docs/contracts/P11-01-catalog-live-sessions.md
/// §2.2. No <see cref="Siri.Persistence.Conventions.ISoftDelete"/> — see <see cref="COURSE_LIVE_SESSION"/>'s
/// own doc comment.</summary>
public sealed class CourseLiveSessionConfiguration : IEntityTypeConfiguration<COURSE_LIVE_SESSION>
{
    public void Configure(EntityTypeBuilder<COURSE_LIVE_SESSION> builder)
    {
        builder.ToTable("COURSE_LIVE_SESSIONS", "CATALOG");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.StartsAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.EndsAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.SortOrder).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.CancelReason).HasMaxLength(500);

        // Cascade: genuine composition, same reasoning CourseSectionConfiguration's own comment gives for
        // COURSE_SECTION — a session without a course owner has no meaning, and a hard-deleted COURSE
        // (bypassing the soft-delete interceptor path) should take its sessions with it. Only one FK edge
        // to COURSES exists on this table (unlike COURSE_EPISODES' historical two-path case — see
        // COURSE_EPISODE's own configuration for that precedent), so there is no "multiple cascade paths"
        // risk here (confirmed: this table's only other FK targets COURSE_EPISODES, not COURSES).
        // .WithMany(c => c.LiveSessions), not a bare .WithMany() — same shadow-FK-avoidance reasoning
        // CourseSectionConfiguration's own comment gives.
        builder.HasOne<COURSE>()
            .WithMany(c => c.LiveSessions)
            .HasForeignKey(s => s.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, nullable: a different aggregate (COURSE_EPISODE lives under COURSE_SECTION, not under
        // this session) — an episode being removed (COURSE.RemoveEpisode, blocked once Published/Archived
        // but still possible while the course is Draft) must not silently cascade-delete the session that
        // references it as its recording. No back-navigation needed on COURSE_EPISODE's side — nothing
        // there needs to walk to the session that recorded it.
        builder.HasOne<COURSE_EPISODE>()
            .WithMany()
            .HasForeignKey(s => s.RecordingEpisodeId)
            .OnDelete(DeleteBehavior.NoAction);

        // First concurrency token below the COURSE aggregate root itself — same reasoning COURSE.RowVersion's
        // own configuration comment gives, opted in per-entity via ConcurrencyTokenInterceptor.
        builder.Property(s => s.RowVersion).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        // Ordered schedule per course (public read model, builder list) — leftmost prefix also serves
        // "does this course have any live sessions" lookups.
        builder.HasIndex(s => new { s.CourseId, s.StartsAtUtc });

        // Cross-course lookup for upcoming Scheduled sessions (ILiveScheduleReader.GetUpcomingSessionsAsync
        // — consumed by the future live-meeting-sync/live-invite-reconcile jobs, P11-03/04). PostgreSQL
        // boolean-literal filtered-index syntax per database.md ("\"IS_DELETED\" = false", not SQL
        // Server's "[IS_DELETED] = 0") — STATUS is quoted because ApplyUppercaseNamingConventions folds
        // the actual column name to that identifier; 'Scheduled' is the enum's string value, not an
        // identifier, so it stays unquoted.
        builder.HasIndex(s => s.StartsAtUtc).HasFilter("\"STATUS\" = 'Scheduled'");

        builder.Property(s => s.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasPrecision(3);
    }
}
