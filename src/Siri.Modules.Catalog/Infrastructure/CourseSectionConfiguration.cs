using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>EF Core mapping for <see cref="CourseSection"/>. No <see cref="Siri.Persistence.Conventions.ISoftDelete"/>
/// — see <c>CourseConfiguration</c>'s doc comment for the soft-delete-doesn't-cascade-to-children gap
/// this implies.</summary>
public sealed class CourseSectionConfiguration : IEntityTypeConfiguration<CourseSection>
{
    public void Configure(EntityTypeBuilder<CourseSection> builder)
    {
        builder.ToTable("CourseSections", "catalog");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SortOrder).IsRequired();

        // Cascade: the real ownership edge — a section cannot outlive its course. Contrast with
        // Category's self-referencing FK (P1-01), which uses Restrict because deleting a
        // category-with-children must be an explicit, guarded admin action, not an accidental cascade;
        // here, a Course row being hard-deleted (bypassing the normal soft-delete interceptor path —
        // see CourseConfiguration's doc comment) genuinely should take its sections with it.
        // .WithMany(c => c.Sections), not a bare .WithMany() — Course.Sections is a real navigation
        // property (backed by a private field, wired up from CourseConfiguration's side below); leaving
        // the navigation unreferenced here made EF's convention discovery treat it as a *second*,
        // separate relationship and invent a phantom shadow FK (caught by reading the generated
        // migration before applying it — see CourseConfiguration's own note on this).
        builder.HasOne<Course>()
            .WithMany(c => c.Sections)
            .HasForeignKey(s => s.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same navigation-vs-shadow-FK reasoning as the Course relationship above, one level down:
        // CourseSection.Episodes is a real navigation, so the CourseEpisode side must reference it too
        // (see CourseEpisodeConfiguration) — this side owns the .Navigation()/.HasField() wiring since
        // Episodes lives on this entity type.
        builder.Navigation(s => s.Episodes).HasField("_episodes").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Mirrors CategoryConfiguration's (ParentId, SortOrder) index shape exactly — serves both the
        // ordered-children query and "does this course have any sections" lookups.
        builder.HasIndex(s => new { s.CourseId, s.SortOrder });

        builder.Property(s => s.CreatedAtUtc).HasPrecision(3).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasPrecision(3);
    }
}
