using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="POST"/> — see docs/DATABASE.md's "cms" section and <see cref="POST"/>'s
/// own doc comment for the UPPERCASE naming exception (docs/DECISIONS.md D-17) and the
/// <see cref="Siri.Persistence.Conventions.ISoftDelete"/>/content-sanitization reasoning; see
/// <see cref="BannerConfiguration"/>'s own doc comment for why most properties below need no explicit
/// <c>HasColumnName</c>.
/// </summary>
public sealed class PostConfiguration : IEntityTypeConfiguration<POST>
{
    public void Configure(EntityTypeBuilder<POST> builder)
    {
        builder.ToTable("POSTS", "cms");

        builder.HasKey(p => p.POST_ID);

        builder.Property(p => p.SLUG).HasMaxLength(200).IsRequired();
        // Filtered so a soft-deleted post's slug can be reused — without this, a hidden (IsDeleted) row
        // would permanently squat the slug for every future post. Same reasoning
        // Siri.Modules.Catalog.Infrastructure.CourseConfiguration's own comment gives for Courses.Slug;
        // the filter references IS_DELETED (this table's actual column name), not IsDeleted.
        builder.HasIndex(p => p.SLUG).IsUnique().HasFilter("[IS_DELETED] = 0");

        builder.Property(p => p.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(p => p.EXCERPT).HasMaxLength(500).IsRequired();

        // TODO(later task): CONTENT_HTML is untrusted until a server-side allowlist sanitizer runs on both
        // save (Application.PostService's Create/Update) and render — see POST's own doc comment and
        // .claude/rules/security.md. Not implemented in this scaffold pass.
        // Deliberately no HasMaxLength here, unlike every other string column in this module: this holds a
        // full rich-text article body, which genuinely has no reasonable fixed cap the way a short title/
        // excerpt does. The SQL Server provider's default for an nvarchar column with no HasMaxLength is
        // nvarchar(max), which is exactly what's wanted — this column is never queried through a b-tree
        // index; a future full-text index, if ever needed, would use a raw-SQL FULLTEXT INDEX migration the
        // same way Siri.Persistence.Migrations.AddCourseFullTextIndex did for Courses, not a length cap.
        builder.Property(p => p.CONTENT_HTML).IsRequired();

        builder.Property(p => p.COVER_IMAGE_URL).HasMaxLength(1000);

        // No FK — cross-module/schema reference to identity.Users.Id, same convention
        // Siri.Modules.Catalog.Infrastructure.CourseConfiguration's own comment explains in full for
        // Course.TrailerMediaAssetId.
        builder.Property(p => p.AUTHOR_USER_ID).IsRequired();

        builder.Property(p => p.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.PUBLISHED_AT_UTC).HasPrecision(3);

        builder.Property(p => p.SEO_TITLE).HasMaxLength(200);
        builder.Property(p => p.SEO_DESCRIPTION).HasMaxLength(500);

        // Serves the eventual public "published posts, newest first" read — same
        // (Status, ..., PublishedAtUtc)-leftmost-prefix reasoning CourseConfiguration's day-one index gives.
        builder.HasIndex(p => new { p.STATUS, p.PUBLISHED_AT_UTC });

        // ISoftDelete — C# property names stay PascalCase (see POST's own doc comment for why); only the
        // column names are UPPERCASE.
        builder.Property(p => p.IsDeleted).HasColumnName("IS_DELETED").IsRequired();
        builder.Property(p => p.DeletedAtUtc).HasColumnName("DELETED_AT_UTC").HasPrecision(3);

        // IAuditable — same reasoning.
        builder.Property(p => p.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(p => p.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(p => p.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
