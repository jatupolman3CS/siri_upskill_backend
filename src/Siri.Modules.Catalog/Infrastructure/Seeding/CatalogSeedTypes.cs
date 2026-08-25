using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure.Seeding;

/// <summary>One root category to seed (task P1-30) — plain, behavior-free record, same "the data set
/// stays pure and unit-testable" reasoning <c>Siri.Modules.Identity.Infrastructure.Seeding.SeedUserSpec</c>
/// already establishes.</summary>
public sealed record CategorySeedSpec(string Slug, string NameTh, string NameEn, string IconKey);

/// <summary>One instructor persona to seed (task P1-30). <see cref="IdentityEmail"/> must be one of the
/// Instructor-role accounts <c>IdentitySeedData.BuildUsers</c> creates — <see cref="CatalogSeeder"/>
/// resolves it to a real user id via the map <c>IdentitySeeder.SeedAsync</c> returns, rather than this
/// module inventing a disconnected id nothing can ever log in as (see <c>CatalogSeeder</c>'s own doc
/// comment).</summary>
public sealed record InstructorSeedSpec(string IdentityEmail, string DisplayName, string Headline, string Bio);

/// <summary>One video lesson to seed inside a <see cref="CourseSectionSeedSpec"/>. <see cref="DurationSeconds"/>
/// backs a placeholder <c>MediaAssetId</c> <see cref="CatalogSeeder"/> attaches (task P1-30: no real Bunny
/// Stream video exists — <c>Siri.Modules.Media</c> is still an empty stub, the same forward-reference gap
/// <c>Course.TrailerMediaAssetId</c>'s own doc comment already describes) — only there so
/// <c>Course.Publish</c>'s "≥1 episode with media" invariant is satisfied with a believable duration, never
/// a real playable asset.</summary>
public sealed record CourseEpisodeSeedSpec(string Title, int DurationSeconds);

/// <summary>One syllabus section to seed inside a <see cref="CourseSeedSpec"/>.</summary>
public sealed record CourseSectionSeedSpec(string Title, IReadOnlyList<CourseEpisodeSeedSpec> Episodes);

/// <summary>One course to seed (task P1-30). <see cref="CategorySlug"/>/<see cref="InstructorEmail"/>
/// reference <see cref="CategorySeedSpec.Slug"/>/<see cref="InstructorSeedSpec.IdentityEmail"/> — validated
/// to actually match by <c>CatalogSeedDataTests</c>. Every course seeded this way is in Thai
/// (<c>CourseLanguage.Thai</c>) and ends up <c>Published</c> (see <c>CatalogSeeder</c>) — sample catalog
/// data is only useful if it is actually visible in search/sitemap/course-detail, not sitting in Draft.</summary>
public sealed record CourseSeedSpec(
    string CategorySlug,
    string InstructorEmail,
    string Title,
    string Subtitle,
    string Description,
    CourseLevel Level,
    decimal Price,
    decimal ComparePrice,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<CourseSectionSeedSpec> Sections);
