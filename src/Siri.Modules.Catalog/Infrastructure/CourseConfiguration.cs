using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="COURSE"/> — see docs/DATABASE.md's "catalog" section.
/// <para>
/// <see cref="COURSE.InstructorId"/> now has a real FK to <c>InstructorProfiles.Id</c> (task P1-03) —
/// same module/schema, so this was always going to get one once that table existed (P1-02's own
/// sequencing gap, closed here). <c>NoAction</c>, not <c>Cascade</c>: an instructor's courses must not
/// vanish if their profile row is ever removed (it never is in practice — no delete endpoint exists for
/// <c>INSTRUCTOR_PROFILE</c> — but the DB-level rule should hold regardless of what today's application
/// code happens to do), matching database.md's blanket "ห้าม cascade delete" instinct even outside the
/// explicitly-named Orders/Payments/RevenueSplits/Enrollments/Certificates list.
/// <see cref="COURSE.TrailerMediaAssetId"/> is a different case — targets <c>media.MediaAssets</c>, a
/// different module/schema/project (<c>Siri.Modules.Media</c>, confirmed still an empty stub, already
/// checked by <c>Siri.ArchitectureTests</c>'s module-boundary test) — this one gets no FK constraint
/// <em>ever</em>: a database-level FK spanning two modules' schemas is exactly the physical coupling
/// docs/ARCHITECTURE.md §1 keeps modules as separate projects to avoid (independent service extraction
/// later). Existence/readiness of a trailer media asset is an application-layer concern for whichever
/// later handler writes it.
/// </para>
/// <para>
/// Soft-delete (<see cref="Siri.Persistence.Conventions.ISoftDelete"/>) applies only to <see cref="COURSE"/>
/// itself — not <see cref="COURSE_SECTION"/>/<see cref="COURSE_EPISODE"/>/<see cref="COURSE_OUTCOME"/>/
/// <see cref="COURSE_REQUIREMENT"/> (matches docs/DATABASE.md's explicit soft-delete table list: "COURSE,
/// Post, Discussion"). <c>ApplySoftDeleteQueryFilter</c> adds its <c>WHERE IsDeleted = 0</c> filter per
/// entity type individually — it does <em>not</em> cascade to children, so a soft-deleted course's
/// sections/episodes/outcomes/requirements are still visible to a query against those tables directly.
/// Whoever writes the first standalone query against <c>CourseSections()</c>/<c>CourseEpisodes()</c>/etc.
/// (P1-04/P1-06/P1-07) must join through and filter by the parent <see cref="COURSE.IsDeleted"/> itself.
/// </para>
/// </summary>
public sealed class CourseConfiguration : IEntityTypeConfiguration<COURSE>
{
    public void Configure(EntityTypeBuilder<COURSE> builder)
    {
        builder.ToTable("COURSES", "CATALOG");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Slug).HasMaxLength(200).IsRequired();
        // Filtered so a soft-deleted course's slug can be reused — without this, a hidden (IsDeleted)
        // row would permanently squat the slug for every future course.
        builder.HasIndex(c => c.Slug).IsUnique().HasFilter("[IS_DELETED] = 0");

        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Subtitle).HasMaxLength(300);
        builder.Property(c => c.Description).HasMaxLength(4000);

        builder.Property(c => c.InstructorId).IsRequired();
        // Real FK now that InstructorProfiles exists (P1-03) — see this class's own doc comment for why
        // NoAction. EF's convention also adds a non-unique index on InstructorId for this relationship
        // (no existing index already covers it as a leftmost prefix), which doubles as exactly what a
        // future "my courses" instructor-dashboard query (P4) will need.
        builder.HasOne<INSTRUCTOR_PROFILE>()
            .WithMany()
            .HasForeignKey(c => c.InstructorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(c => c.CategoryId).IsRequired();

        builder.Property(c => c.Level).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.Language).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(c => c.ThumbnailUrl).HasMaxLength(1000);
        builder.Property(c => c.TrailerMediaAssetId);

        builder.Property(c => c.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(c => c.ComparePrice).HasPrecision(18, 2);
        // char(3), not nvarchar — database.md's explicit carve-out from the nvarchar-always rule for
        // ISO 4217 currency codes.
        builder.Property(c => c.Currency).HasColumnType("char(3)").IsRequired();

        builder.Property(c => c.AccessDurationDays);

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.PublishedAtUtc).HasPrecision(3);
        // P1-05: not in docs/DATABASE.md's original Courses column sketch — added the same way P1-02
        // added CourseOutcomes/CourseRequirements beyond that sketch, justified by a real need (an
        // instructor can't act on a rejection with no reason attached to it).
        builder.Property(c => c.RejectionReason).HasMaxLength(1000);

        builder.Property(c => c.TotalDurationSeconds).IsRequired();
        builder.Property(c => c.EpisodeCount).IsRequired();
        builder.Property(c => c.RatingAverage).HasPrecision(3, 2).IsRequired();
        builder.Property(c => c.RatingCount).IsRequired();
        builder.Property(c => c.EnrollmentCount).IsRequired();

        builder.Property(c => c.SeoTitle).HasMaxLength(200);
        builder.Property(c => c.SeoDescription).HasMaxLength(500);

        // First use of a concurrency token in this codebase — database.md calls for rowversion on
        // tables that get concurrently edited; Courses is explicitly one (multiple instructor/admin
        // edits, autosave from the future course builder, ...).
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.Property(c => c.IsDeleted).IsRequired();
        builder.Property(c => c.DeletedAtUtc).HasPrecision(3);

        // docs/DATABASE.md's "Index ที่ต้องมีตั้งแต่วันแรก" (day-one) section — more authoritative and
        // fully-specified than the inline IX(Status, PublishedAtUtc)/IX(CategoryId, Status) shorthand in
        // the catalog schema sketch. This single covering index serves both via leftmost-prefix, so the
        // redundant simpler pair is skipped.
        builder.HasIndex(c => new { c.Status, c.CategoryId, c.PublishedAtUtc })
            .IncludeProperties(c => new { c.Title, c.Slug, c.Price, c.RatingAverage, c.ThumbnailUrl });

        builder.Property(c => c.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).HasPrecision(3);

        // The FK side of Sections/Outcomes/Requirements is configured from each child's own
        // IEntityTypeConfiguration (CourseSectionConfiguration/CourseOutcomeConfiguration/
        // CourseRequirementConfiguration), matching this module's CategoryConfiguration style of
        // configuring from the "many" side — but Sections/Outcomes/Requirements are real navigation
        // properties (computed from private List<T> backing fields, same shape User.Roles uses), and
        // EF needs to be told how to *read* them via reflection-unfriendly computed properties. Without
        // this, EF Core's convention discovery still finds the navigation independently and creates a
        // second, phantom relationship with its own shadow FK column (caught by reading the generated
        // migration in full before applying — do this for every new aggregate, not just this one).
        builder.Navigation(c => c.Sections).HasField("_sections").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(c => c.Outcomes).HasField("_outcomes").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(c => c.Requirements).HasField("_requirements").UsePropertyAccessMode(PropertyAccessMode.Field);

        // FULLTEXT index on Title/Subtitle/Description (docs/DATABASE.md's catalog block) is NOT here —
        // EF Core has no fluent API for Full-Text Search at all (a SQL Server feature outside EF's
        // provider-agnostic model, not a missing convenience method), so it lives in the hand-written raw
        // SQL migration AddCourseFullTextIndex instead (task P1-06). Confirmed the Contabo SQL Server
        // instance actually has the feature installed before building this — see that migration's own
        // doc comment.
    }
}
