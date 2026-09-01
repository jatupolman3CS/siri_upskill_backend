using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="WATCH_EVENT"/> — see docs/DATABASE.md's "learning" section and this
/// module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="WATCH_EVENT.WATCH_EVENT_ID"/> is a <c>bigint IDENTITY</c>, not a UUIDv7 <see cref="Guid"/> —
/// see <see cref="WATCH_EVENT"/>'s own doc comment for why. <c>ValueGeneratedOnAdd()</c>
/// below is called explicitly even though it is already EF Core's convention default for an integer key
/// (this codebase prefers explicit configuration over relying on convention discovery — same instinct
/// database.md's "ต้องระบุ .HasForeignKey()/.WithMany(nav) ชัดเจนเสมอ ห้ามพึ่ง convention discovery" applies
/// to relationships). This is the provider-agnostic core EF Core API (<c>Microsoft.EntityFrameworkCore</c>),
/// NOT the SQL-Server-specific <c>UseIdentityColumn()</c> (<c>Microsoft.EntityFrameworkCore.SqlServer</c>) —
/// this module's <c>.csproj</c> deliberately does not reference the SqlServer package (same
/// provider-agnostic reasoning <c>Siri.Modules.Catalog.csproj</c>'s own comment gives), so only the
/// provider-agnostic form is available here; the relational SQL Server provider (wired centrally in
/// <c>Siri.Persistence</c>) is what actually turns this into a real <c>IDENTITY(1,1)</c> column at
/// migration-generation time.
/// </para>
/// <para>
/// <see cref="WATCH_EVENT.ENROLLMENT_ID"/> gets a real (<c>NoAction</c>) FK to <see cref="ENROLLMENT"/> —
/// see <see cref="WATCH_EVENT"/>'s own doc comment for why not <c>Cascade</c>.
/// <see cref="WATCH_EVENT.EPISODE_ID"/> gets no <c>HasOne()</c>/FK at all — cross-module/cross-schema to
/// <c>catalog.CourseEpisodes</c>. The <c>(EnrollmentId, OccurredAtUtc)</c> index is this configuration's
/// own addition (not spelled out in docs/DATABASE.md's day-one index section, which has no entry for this
/// table) — justified the same way <c>QuizConfiguration</c>'s own doc comment justifies its own
/// not-explicitly-listed <c>EPISODE_ID</c> index: "does this enrollment's watch history, time-ordered"
/// is exactly the lookup a future resume/analytics-rollup consumer of <see cref="IWatchEventRepository"/>
/// needs.
/// </para>
/// </summary>
public sealed class WatchEventConfiguration : IEntityTypeConfiguration<WATCH_EVENT>
{
    public void Configure(EntityTypeBuilder<WATCH_EVENT> builder)
    {
        builder.ToTable("WATCH_EVENTS", "LEARNING");

        builder.HasKey(x => x.WATCH_EVENT_ID);
        builder.Property(x => x.WATCH_EVENT_ID)
            .HasColumnName("WATCH_EVENT_ID")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ENROLLMENT_ID).HasColumnName("ENROLLMENT_ID").IsRequired();
        // NoAction — see WATCH_EVENT's own doc comment for why an append-only log never cascades.
        builder.HasOne<ENROLLMENT>()
            .WithMany()
            .HasForeignKey(x => x.ENROLLMENT_ID)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.EPISODE_ID).HasColumnName("EPISODE_ID").IsRequired();

        builder.Property(x => x.EVENT_TYPE).HasColumnName("EVENT_TYPE").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.POSITION_SECONDS).HasColumnName("POSITION_SECONDS").IsRequired();
        builder.Property(x => x.OCCURRED_AT_UTC).HasColumnName("OCCURRED_AT_UTC").HasPrecision(3).IsRequired();

        builder.HasIndex(x => new { x.ENROLLMENT_ID, x.OCCURRED_AT_UTC }).HasDatabaseName("IX_WATCH_EVENTS_ENROLLMENT_ID_OCCURRED_AT_UTC");

        // Query-performance audit (2026-09): three separate LearningAnalyticsContract query shapes
        // (GetEpisodeDropOffRollupAsync, the WatchEvents-Enrollments join inside
        // GetDailyCourseActivityAsync, and PurgeOldWatchEventsAsync) all filter this table by
        // OCCURRED_AT_UTC range/threshold alone — none of them has an ENROLLMENT_ID predicate. The
        // (ENROLLMENT_ID, OCCURRED_AT_UTC) index above is ENROLLMENT_ID-leftmost, so it cannot seek a
        // date-only predicate, and this is this database's largest table by far (one row per video-
        // heartbeat event — see this class's own doc comment). A second index led by OCCURRED_AT_UTC
        // alone lets the nightly analytics rollup and purge job (AnalyticsRollupJob) seek its date window
        // instead of scanning the whole table.
        builder.HasIndex(x => x.OCCURRED_AT_UTC).HasDatabaseName("IX_WATCH_EVENTS_OCCURRED_AT_UTC");
    }
}
