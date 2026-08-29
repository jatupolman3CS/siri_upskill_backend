using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Seeding;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure.Seeding;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// Pure-logic tests for <see cref="CatalogSeedData"/> (task P1-30) — no database, no clock; just
/// asserting the fixed sample-catalog data set has the shape <see cref="CatalogSeeder"/> needs to persist
/// it safely (every cross-reference resolves, nothing is empty/non-positive). Same split
/// <c>IdentitySeedDataTests</c> already establishes between testing the data shape here and the
/// persistence behavior in the (Docker-gated) integration test.
/// </summary>
public class CatalogSeedDataTests
{
    [Fact]
    public void BuildCategories_ReturnsExactlyThreeWithUniqueNonEmptySlugsAndNames()
    {
        var categories = CatalogSeedData.BuildCategories();

        Assert.Equal(3, categories.Count);
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Slug)));
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.NameTh)));
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.NameEn)));
        Assert.All(categories, c => Assert.False(string.IsNullOrWhiteSpace(c.IconKey)));
        Assert.Equal(categories.Count, categories.Select(c => c.Slug).Distinct().Count());
    }

    [Fact]
    public void BuildInstructors_ReturnsExactlyFiveWithUniqueEmailsAndNonEmptyPersonaFields()
    {
        var instructors = CatalogSeedData.BuildInstructors();

        Assert.Equal(5, instructors.Count);
        Assert.Equal(instructors.Count, instructors.Select(i => i.IdentityEmail).Distinct().Count());
        Assert.All(instructors, i => Assert.False(string.IsNullOrWhiteSpace(i.DisplayName)));
        Assert.All(instructors, i => Assert.False(string.IsNullOrWhiteSpace(i.Headline)));
        Assert.All(instructors, i => Assert.False(string.IsNullOrWhiteSpace(i.Bio)));
    }

    /// <summary>
    /// The cross-module link <see cref="CatalogSeeder"/> depends on at seed time: every instructor
    /// persona here must name an email that <see cref="IdentitySeedData.BuildUsers"/> actually seeds with
    /// the Instructor role — otherwise <c>CatalogSeeder.ResolveInstructorUserId</c> throws at seed time
    /// with no way to know why until someone reads the exception message. Catching the mismatch here,
    /// pure and DB-free, is cheaper than catching it via a failed <c>--seed</c> run.
    /// </summary>
    [Fact]
    public void BuildInstructors_EveryEmail_IsASeededInstructorRoleAccountInIdentitySeedData()
    {
        var identityOptions = new SeedOptions
        {
            AdminEmail = "admin@example.test",
            AdminPassword = "a-real-admin-password-1",
            TestUserPassword = "a-real-test-user-password-1",
        };

        var identityInstructorEmails = IdentitySeedData.BuildUsers(identityOptions)
            .Where(u => u.RoleId == ROLE.InstructorId)
            .Select(u => u.Email)
            .ToHashSet();

        var catalogInstructorEmails = CatalogSeedData.BuildInstructors().Select(i => i.IdentityEmail);

        Assert.All(catalogInstructorEmails, email => Assert.Contains(email, identityInstructorEmails));
    }

    [Fact]
    public void BuildCourses_ReturnsExactlyTwenty()
    {
        Assert.Equal(20, CatalogSeedData.BuildCourses().Count);
    }

    [Fact]
    public void BuildCourses_EveryTitleIsUnique()
    {
        var courses = CatalogSeedData.BuildCourses();

        Assert.Equal(courses.Count, courses.Select(c => c.Title).Distinct().Count());
    }

    /// <summary>Guards the exact assumption <c>CatalogSeeder.BuildBaseSlug</c> relies on for idempotency
    /// — see that method's own comment for what happens if two seed titles ever did collide (skipped, not
    /// a crash), which this test exists specifically so nobody ever has to find out by accident.</summary>
    [Fact]
    public void BuildCourses_EveryTitle_TransliteratesToAUniqueNonEmptySlug()
    {
        var courses = CatalogSeedData.BuildCourses();
        var slugs = courses.Select(c => ThaiSlugGenerator.GenerateBaseSlug(c.Title)).ToList();

        Assert.All(slugs, slug => Assert.False(string.IsNullOrEmpty(slug)));
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    [Fact]
    public void BuildCourses_EveryCategorySlug_ExistsInBuildCategories()
    {
        var categorySlugs = CatalogSeedData.BuildCategories().Select(c => c.Slug).ToHashSet();
        var courses = CatalogSeedData.BuildCourses();

        Assert.All(courses, c => Assert.Contains(c.CategorySlug, categorySlugs));
    }

    [Fact]
    public void BuildCourses_EveryInstructorEmail_ExistsInBuildInstructors()
    {
        var instructorEmails = CatalogSeedData.BuildInstructors().Select(i => i.IdentityEmail).ToHashSet();
        var courses = CatalogSeedData.BuildCourses();

        Assert.All(courses, c => Assert.Contains(c.InstructorEmail, instructorEmails));
    }

    [Fact]
    public void BuildCourses_EveryCourse_HasPositivePriceAndAComparePriceAboveThePrice()
    {
        var courses = CatalogSeedData.BuildCourses();

        Assert.All(courses, c => Assert.True(c.Price > 0));
        Assert.All(courses, c => Assert.True(c.ComparePrice > c.Price));
    }

    /// <summary>The floor <c>COURSE.Publish</c>'s invariant needs — <see cref="CatalogSeeder"/> attaches
    /// placeholder media to every episode, so this only needs to confirm every course actually has at
    /// least one.</summary>
    [Fact]
    public void BuildCourses_EveryCourse_HasAtLeastOneSectionWithAtLeastOneEpisode()
    {
        var courses = CatalogSeedData.BuildCourses();

        Assert.All(courses, c => Assert.NotEmpty(c.Sections));
        Assert.All(courses, c => Assert.All(c.Sections, s => Assert.NotEmpty(s.Episodes)));
    }

    [Fact]
    public void BuildCourses_EveryCourse_HasAtLeastOneOutcomeAndOneRequirement()
    {
        var courses = CatalogSeedData.BuildCourses();

        Assert.All(courses, c => Assert.NotEmpty(c.Outcomes));
        Assert.All(courses, c => Assert.NotEmpty(c.Requirements));
    }

    [Fact]
    public void BuildCourses_EveryEpisode_HasAPositiveDuration()
    {
        var episodes = CatalogSeedData.BuildCourses().SelectMany(c => c.Sections).SelectMany(s => s.Episodes);

        Assert.All(episodes, e => Assert.True(e.DurationSeconds > 0));
    }

    [Fact]
    public void BuildCourses_DistributionAcrossCategories_MatchesTheDocumentedSevenSevenSixSplit()
    {
        var courses = CatalogSeedData.BuildCourses();
        var countsBySlug = courses.GroupBy(c => c.CategorySlug).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(7, countsBySlug["marketing-business"]);
        Assert.Equal(7, countsBySlug["programming-technology"]);
        Assert.Equal(6, countsBySlug["finance-investment"]);
    }
}
