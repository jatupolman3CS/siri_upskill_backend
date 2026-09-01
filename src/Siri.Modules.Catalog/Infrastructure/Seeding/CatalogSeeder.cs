using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure.Seeding;

/// <summary>
/// Task P1-30: creates the fixed sample-catalog data set (<see cref="CatalogSeedData"/>) against whatever
/// database <see cref="AppDbContext"/> is currently configured for — same "never assume a specific
/// connection target" stance <see cref="Identity.Infrastructure.Seeding.IdentitySeeder"/> already takes.
/// Invoked from <c>Siri.Api/Program.cs</c>'s <c>--seed</c> CLI flag, always immediately after
/// <c>IdentitySeeder</c> in the same run (its own doc comment explains why: this class needs the 5
/// Instructor accounts' real user ids, which only <c>IdentitySeeder</c> can hand it).
/// <para>
/// <b>Idempotent per-row, same shape <c>IdentitySeeder</c> already establishes</b>: categories are
/// looked up by <see cref="CATEGORY.Slug"/>, instructor profiles by <see cref="INSTRUCTOR_PROFILE.UserId"/>,
/// courses by their generated <see cref="COURSE.Slug"/> — each checked individually before creating,
/// never enumerating/wiping a whole table. Running this twice against the same database creates zero
/// duplicate rows the second time.
/// </para>
/// <para>
/// <b>Deliberately bypasses the real apply/approve and create/submit/publish flows</b> (seed/sample data
/// only, same explicit exception <c>IdentitySeeder</c>'s own doc comment takes for account creation):
/// builds each <see cref="INSTRUCTOR_PROFILE"/> via <see cref="INSTRUCTOR_PROFILE.Apply"/> then immediately
/// <see cref="INSTRUCTOR_PROFILE.Approve"/>s it, and each <see cref="COURSE"/> via <see cref="COURSE.Create"/>
/// plus its own public Add*/Update*/Set* methods, then immediately <see cref="COURSE.Publish"/>es it —
/// sample catalog data sitting in Draft/Pending would not actually be visible anywhere, defeating the
/// point of seeding it.
/// </para>
/// </summary>
public sealed class CatalogSeeder(AppDbContext dbContext, IClock clock, ILogger<CatalogSeeder> logger)
{
    private const string FallbackSlugBase = "course";
    private const int MaxSlugBaseLength = 190; // Courses.Slug is nvarchar(200) — same headroom CreateCourseHandler leaves

    /// <param name="instructorUserIdsByEmail">The map <c>IdentitySeeder.SeedAsync</c> returns — every
    /// email in <see cref="CatalogSeedData.BuildInstructors"/> must be a key in here, or this throws
    /// (see <see cref="ResolveInstructorUserId"/>).</param>
    public async Task SeedAsync(IReadOnlyDictionary<string, Guid> instructorUserIdsByEmail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructorUserIdsByEmail);

        var categoryIdBySlug = await SeedCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var instructorProfileIdByEmail = await SeedInstructorProfilesAsync(instructorUserIdsByEmail, cancellationToken).ConfigureAwait(false);
        var coursesCreated = await SeedCoursesAsync(categoryIdBySlug, instructorProfileIdByEmail, cancellationToken).ConfigureAwait(false);

        // Categories/instructor profiles below each Add() to the still-unsaved context directly (client-
        // generated UuidV7 ids mean the in-memory dictionaries this method threads together are already
        // correct before anything hits the database) — one SaveChangesAsync at the end for everything,
        // same single-commit shape IdentitySeeder uses.
        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Seed: sample catalog data ready ({CourseCount} course(s) created this run, of {TotalCourseCount} total sample courses).",
            coursesCreated,
            CatalogSeedData.BuildCourses().Count);
    }

    private async Task<Dictionary<string, Guid>> SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var specs = CatalogSeedData.BuildCategories();
        var slugs = specs.Select(s => s.Slug).ToArray();
        var namesTh = specs.Select(s => s.NameTh).ToArray();
        var namesEn = specs.Select(s => s.NameEn).ToArray();

        var existingCategories = await dbContext.Categories()
            .AsNoTracking()
            .Where(c => slugs.Contains(c.Slug) || namesTh.Contains(c.NameTh) || namesEn.Contains(c.NameEn))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var categoryIdBySlug = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingCategories)
        {
            categoryIdBySlug[existing.Slug] = existing.Id;
            var matchingSpec = specs.FirstOrDefault(s =>
                string.Equals(s.Slug, existing.Slug, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.NameTh, existing.NameTh, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.NameEn, existing.NameEn, StringComparison.OrdinalIgnoreCase));
            if (matchingSpec is not null)
            {
                categoryIdBySlug[matchingSpec.Slug] = existing.Id;
            }
        }

        var createdCount = 0;

        foreach (var spec in specs)
        {
            if (categoryIdBySlug.ContainsKey(spec.Slug))
            {
                continue;
            }

            var category = CATEGORY.Create(spec.Slug, spec.NameTh, spec.NameEn, spec.IconKey, parentId: null, sortOrder: categoryIdBySlug.Count);
            dbContext.Categories().Add(category);
            categoryIdBySlug[spec.Slug] = category.Id;
            createdCount++;
        }

        logger.LogInformation(
            "Seed: {CreatedCount} sample categor(y/ies) created, {ExistingCount} already existed.",
            createdCount,
            specs.Count - createdCount);

        return categoryIdBySlug;
    }

    private async Task<Dictionary<string, Guid>> SeedInstructorProfilesAsync(
        IReadOnlyDictionary<string, Guid> instructorUserIdsByEmail, CancellationToken cancellationToken)
    {
        var specs = CatalogSeedData.BuildInstructors();
        var userIds = specs.Select(s => ResolveInstructorUserId(instructorUserIdsByEmail, s.IdentityEmail)).ToArray();

        var existingProfileIdByUserId = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.Id, cancellationToken)
            .ConfigureAwait(false);

        var profileIdByEmail = new Dictionary<string, Guid>();
        var createdCount = 0;

        foreach (var spec in specs)
        {
            var userId = ResolveInstructorUserId(instructorUserIdsByEmail, spec.IdentityEmail);

            if (existingProfileIdByUserId.TryGetValue(userId, out var existingProfileId))
            {
                profileIdByEmail[spec.IdentityEmail] = existingProfileId;
                continue;
            }

            var profile = INSTRUCTOR_PROFILE.Apply(userId, spec.DisplayName, spec.Headline, spec.Bio);
            profile.Approve(clock);

            dbContext.InstructorProfiles().Add(profile);
            profileIdByEmail[spec.IdentityEmail] = profile.Id;
            createdCount++;
        }

        logger.LogInformation(
            "Seed: {CreatedCount} sample instructor profile(s) created, {ExistingCount} already existed.",
            createdCount,
            specs.Count - createdCount);

        return profileIdByEmail;
    }

    private async Task<int> SeedCoursesAsync(
        IReadOnlyDictionary<string, Guid> categoryIdBySlug,
        IReadOnlyDictionary<string, Guid> instructorProfileIdByEmail,
        CancellationToken cancellationToken)
    {
        var specs = CatalogSeedData.BuildCourses();
        var allSlugs = specs.Select(BuildBaseSlug).ToArray();

        var existingSlugs = (await dbContext.Courses()
                .AsNoTracking()
                .Where(c => allSlugs.Contains(c.Slug))
                .Select(c => c.Slug)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var createdCount = 0;

        foreach (var spec in specs)
        {
            var slug = BuildBaseSlug(spec);

            // Guards both "already seeded on a previous run" and the (unexercised, since
            // CatalogSeedDataTests asserts all 20 base slugs are distinct) theoretical case of two seed
            // specs transliterating to the same slug — either way, skipping rather than throwing keeps
            // this safe to re-run.
            if (existingSlugs.Contains(slug))
            {
                continue;
            }

            if (!categoryIdBySlug.TryGetValue(spec.CategorySlug, out var categoryId))
            {
                throw new InvalidOperationException(
                    $"CatalogSeedData course '{spec.Title}' references category slug '{spec.CategorySlug}', which CatalogSeedData.BuildCategories does not define.");
            }

            if (!instructorProfileIdByEmail.TryGetValue(spec.InstructorEmail, out var instructorId))
            {
                throw new InvalidOperationException(
                    $"CatalogSeedData course '{spec.Title}' references instructor email '{spec.InstructorEmail}', which CatalogSeedData.BuildInstructors does not define.");
            }

            var course = COURSE.Create(slug, spec.Title, instructorId, categoryId, spec.Level, CourseLanguage.Thai, spec.Price);
            course.UpdateBasicInfo(spec.Title, spec.Subtitle, spec.Description);
            course.SetPricing(spec.Price, spec.ComparePrice);

            foreach (var outcome in spec.Outcomes)
            {
                course.AddOutcome(outcome);
            }

            foreach (var requirement in spec.Requirements)
            {
                course.AddRequirement(requirement);
            }

            // Only the very first episode of the whole course is a free preview — matches how a real
            // course on this kind of marketplace typically hooks a prospective buyer with one sample
            // lesson, not one per section.
            var isFirstEpisode = true;
            foreach (var sectionSpec in spec.Sections)
            {
                var section = course.AddSection(sectionSpec.Title);

                foreach (var episodeSpec in sectionSpec.Episodes)
                {
                    // Through COURSE, not COURSE_SECTION/COURSE_EPISODE directly — keeps
                    // EpisodeCount/TotalDurationSeconds in sync automatically (COURSE.RecalculateEpisodeStats,
                    // called internally by both of these). Placeholder media — see CatalogSeedData's own
                    // doc comment for why (no real Bunny Stream asset exists yet); only here to satisfy
                    // COURSE.Publish's "≥1 episode with media" invariant with a believable duration.
                    var episode = course.AddEpisode(section.Id, episodeSpec.Title, description: null, isFreePreview: isFirstEpisode);
                    course.AttachEpisodeMedia(episode.Id, UuidV7.NewId(), episodeSpec.DurationSeconds);
                    isFirstEpisode = false;
                }
            }

            course.Publish(clock);

            dbContext.Courses().Add(course);
            existingSlugs.Add(slug);
            createdCount++;
        }

        logger.LogInformation(
            "Seed: {CreatedCount} sample course(s) created, {ExistingCount} already existed.",
            createdCount,
            specs.Count - createdCount);

        await BackfillEpisodeStatsAsync(allSlugs, cancellationToken).ConfigureAwait(false);

        return createdCount;
    }

    /// <summary>
    /// One-time self-heal for sample courses created by a build of this seeder older than
    /// <c>COURSE.RecalculateEpisodeStats</c>: those rows were built by calling
    /// <c>COURSE_SECTION.AddEpisode</c>/<c>COURSE_EPISODE.AttachMedia</c> directly, which never touched
    /// <c>COURSE.EpisodeCount</c>/<c>TotalDurationSeconds</c>, so they are stuck at their zero defaults in
    /// the database despite genuinely having episodes with real durations.
    /// <para>
    /// Re-fetches every sample course that already exists (tracked, with its Sections/Episodes) and
    /// recalculates those two columns — a no-op for a database that has never been touched by the old,
    /// buggy seeder path, since every course this inspects would already report the correct totals and
    /// EF's change tracking only writes back properties whose value actually changed. Cheap enough (a
    /// handful of rows, dev/seed-only) to just always run rather than tracking "did I already fix this
    /// row" as separate state. Freshly-created-this-run courses are not included here — they went through
    /// <see cref="COURSE.AddEpisode(Guid,string,string?,bool)"/>/<see cref="COURSE.AttachEpisodeMedia"/>
    /// above already and are correct from the moment they were built.
    /// </para>
    /// </summary>
    private async Task BackfillEpisodeStatsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        var existingCourses = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Where(c => slugs.Contains(c.Slug))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var course in existingCourses)
        {
            course.RecalculateEpisodeStats();
        }
    }

    private static string BuildBaseSlug(CourseSeedSpec spec)
    {
        var baseSlug = ThaiSlugGenerator.GenerateBaseSlug(spec.Title);

        if (string.IsNullOrEmpty(baseSlug))
        {
            // Never expected to trigger for this module's own fixed, all-Thai seed titles
            // (CatalogSeedDataTests asserts every title produces a non-empty slug) — kept anyway for the
            // same defensive reason CreateCourseHandler keeps it for arbitrary real user input.
            baseSlug = FallbackSlugBase;
        }
        else if (baseSlug.Length > MaxSlugBaseLength)
        {
            baseSlug = baseSlug[..MaxSlugBaseLength];
        }

        return baseSlug;
    }

    private static Guid ResolveInstructorUserId(IReadOnlyDictionary<string, Guid> instructorUserIdsByEmail, string email)
    {
        if (!instructorUserIdsByEmail.TryGetValue(email, out var userId))
        {
            throw new InvalidOperationException(
                $"CatalogSeedData references instructor '{email}', but IdentitySeeder did not report a user id for that email. " +
                "Has IdentitySeedData.BuildUsers been kept in sync with CatalogSeedData.BuildInstructors?");
        }

        return userId;
    }
}
