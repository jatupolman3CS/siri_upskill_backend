using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real HTTP-level proof of P1-06's public course search — same self-contained
/// <see cref="WebApplication"/> approach every other Catalog integration test file already establishes.
/// Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc comment.
/// <para>
/// <b>Known residual risk beyond the usual "no Docker on this dev machine" gap</b>: the
/// <see cref="ContainersFixture"/>'s MSSQL container image
/// (<c>mcr.microsoft.com/mssql/server:2022-latest</c>) was never confirmed to have Full-Text Search
/// available out of the box the way the real Contabo instance was confirmed for this task — modern
/// SQL Server-on-Linux images are expected to include it, but this was not independently verified.
/// <see cref="SearchCourses_WithSearchText_MatchesAndRanksByRelevance"/> is the one test in this file
/// that actually depends on it; every other test here filters/sorts without a search term and never
/// touches <c>FREETEXTTABLE</c> at all, so they don't share that particular risk.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class SearchCoursesTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "search-courses-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public SearchCoursesTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Seo:PublicBaseUrl"] = "https://example.test",
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log",
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateLifetime = true,
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---- Fixture setup (direct-domain, not HTTP — see other Catalog test files' own doc comments) ----

    private static async Task<Category> CreateCategoryAsync(AppDbContext dbContext)
    {
        var category = Category.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test Category", null, null, 0);
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    private static async Task<InstructorProfile> CreateApprovedInstructorAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();
        // Unique per instructor — the instructor-facet test asserts each facet carries ITS OWN
        // instructor's name, which a shared constant name would let pass even if ids were crossed.
        var profile = InstructorProfile.Apply(Guid.NewGuid(), $"Instructor {Guid.NewGuid():N}", null, "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    /// <summary>Builds and publishes a course directly against the domain — no HTTP course-CRUD/publish
    /// flow needed here (those have their own dedicated integration tests); this file's subject is
    /// search/filter/sort/facets over courses that are already Published.</summary>
    private static async Task<Course> CreatePublishedCourseAsync(
        IServiceProvider services, AppDbContext dbContext, Guid instructorId, Guid categoryId, string title, decimal price)
    {
        var clock = services.GetRequiredService<IClock>();
        var course = Course.Create($"course-{Guid.NewGuid():N}", title, instructorId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, price);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);

        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    private static async Task<Course> CreateDraftCourseAsync(AppDbContext dbContext, Guid instructorId, Guid categoryId, string title)
    {
        var course = Course.Create($"course-{Guid.NewGuid():N}", title, instructorId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    private async Task<SearchCoursesResponse> SearchAsync(string queryString)
    {
        using var response = await _client.GetAsync($"/api/catalog/courses/search{queryString}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SearchCoursesResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    // ---- Tests ------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchCourses_NoQueryText_OnlyReturnsPublishedCourses()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var published = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Published Course", 990m);
        var draft = await CreateDraftCourseAsync(dbContext, instructor.Id, category.Id, "Draft Course");

        var response = await SearchAsync("");

        Assert.Contains(response.Results.Items, c => c.Id == published.Id);
        Assert.DoesNotContain(response.Results.Items, c => c.Id == draft.Id);
    }

    [Fact]
    public async Task SearchCourses_FilterByCategory_ReturnsOnlyThatCategory()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var categoryA = await CreateCategoryAsync(dbContext);
        var categoryB = await CreateCategoryAsync(dbContext);
        var courseA = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, categoryA.Id, "Course A", 990m);
        var courseB = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, categoryB.Id, "Course B", 990m);

        var response = await SearchAsync($"?categoryId={categoryA.Id}");

        Assert.Contains(response.Results.Items, c => c.Id == courseA.Id);
        Assert.DoesNotContain(response.Results.Items, c => c.Id == courseB.Id);
    }

    [Fact]
    public async Task SearchCourses_FilterByPriceRange_ExcludesCoursesOutsideRange()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var cheap = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Cheap Course", 100m);
        var expensive = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Expensive Course", 5000m);

        var response = await SearchAsync("?minPrice=500&maxPrice=10000");

        Assert.DoesNotContain(response.Results.Items, c => c.Id == cheap.Id);
        Assert.Contains(response.Results.Items, c => c.Id == expensive.Id);
    }

    [Fact]
    public async Task SearchCourses_MinRatingAboveZero_ExcludesEveryCourseSinceNoneHaveRatingsYet()
    {
        // RatingAverage has no public setter yet (P1-08/CourseStatsUpdater's job — see Course.cs's own
        // doc comment) — every course is 0.00 today, so this is the only meaningful assertion this
        // filter can make until that lands: minRating=0 includes everything, anything above excludes
        // everything.
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Some Course", 990m);

        var atZero = await SearchAsync("?minRating=0");
        var aboveZero = await SearchAsync("?minRating=0.01");

        Assert.Contains(atZero.Results.Items, c => c.Id == course.Id);
        Assert.DoesNotContain(aboveZero.Results.Items, c => c.Id == course.Id);
    }

    [Fact]
    public async Task SearchCourses_SortByPriceAsc_OrdersFromCheapestToMostExpensive()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Mid", 500m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Cheapest", 100m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Priciest", 900m);

        var response = await SearchAsync($"?categoryId={category.Id}&sort=PriceAsc");

        var prices = response.Results.Items.Select(c => c.Price).ToList();
        Assert.Equal(prices.OrderBy(p => p), prices);
    }

    [Fact]
    public async Task SearchCourses_Pagination_ReturnsRequestedPageSizeAndAccurateTotalCount()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        for (var i = 0; i < 5; i++)
        {
            await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, $"Course {i}", 990m);
        }

        var response = await SearchAsync($"?categoryId={category.Id}&page=1&pageSize=2");

        Assert.Equal(2, response.Results.Items.Count);
        Assert.Equal(5, response.Results.TotalCount);
        Assert.Equal(1, response.Results.Page);
        Assert.Equal(2, response.Results.PageSize);
    }

    [Fact]
    public async Task SearchCourses_Facets_CountCoursesPerCategoryAndLevel()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var categoryA = await CreateCategoryAsync(dbContext);
        var categoryB = await CreateCategoryAsync(dbContext);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, categoryA.Id, "A1", 990m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, categoryA.Id, "A2", 990m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, categoryB.Id, "B1", 990m);

        // Deliberately filtered to categoryA — facets should still report both categories (computed
        // before this request's own category filter, see SearchCoursesResponse's own doc comment).
        var response = await SearchAsync($"?categoryId={categoryA.Id}");

        Assert.Equal(2, response.Facets.Categories.Single(f => f.CategoryId == categoryA.Id).Count);
        Assert.Equal(1, response.Facets.Categories.Single(f => f.CategoryId == categoryB.Id).Count);
    }

    [Fact]
    public async Task SearchCourses_FilterByInstructor_ReturnsOnlyThatInstructorsCourses()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructorA = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var instructorB = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseA = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructorA.Id, category.Id, "Course by A", 990m);
        var courseB = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructorB.Id, category.Id, "Course by B", 990m);

        var response = await SearchAsync($"?instructorId={instructorA.Id}");

        Assert.Contains(response.Results.Items, c => c.Id == courseA.Id);
        Assert.DoesNotContain(response.Results.Items, c => c.Id == courseB.Id);
    }

    [Fact]
    public async Task SearchCourses_InstructorFacet_CountsCoursesPerInstructorAndCarriesDisplayName()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructorA = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var instructorB = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructorA.Id, category.Id, "A1", 990m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructorA.Id, category.Id, "A2", 990m);
        await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructorB.Id, category.Id, "B1", 990m);

        // Deliberately filtered to instructorA — facets should still report both instructors (computed
        // before this request's own filters, same convention as the category-facet test above).
        var response = await SearchAsync($"?instructorId={instructorA.Id}");

        var facetA = response.Facets.Instructors.Single(f => f.InstructorId == instructorA.Id);
        var facetB = response.Facets.Instructors.Single(f => f.InstructorId == instructorB.Id);
        Assert.Equal(2, facetA.Count);
        Assert.Equal(1, facetB.Count);
        Assert.Equal(instructorA.DisplayName, facetA.DisplayName);
        Assert.Equal(instructorB.DisplayName, facetB.DisplayName);
    }

    [Fact]
    public async Task SearchCourses_ResultIncludesInstructorDisplayName()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Some Course", 990m);

        var response = await SearchAsync("");

        var item = response.Results.Items.Single(c => c.Id == course.Id);
        Assert.Equal(instructor.DisplayName, item.InstructorDisplayName);
    }

    /// <summary>The one test in this file that actually needs a working FTS index — see this class's
    /// own doc comment for the residual risk that isn't shared by any other test here.</summary>
    [Fact]
    public async Task SearchCourses_WithSearchText_MatchesAndRanksByRelevance()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var matching = await CreatePublishedCourseAsync(
            scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Advanced Python Programming", 990m);
        var nonMatching = await CreatePublishedCourseAsync(
            scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Introduction to Watercolor Painting", 990m);

        var response = await SearchAsync("?q=Python");

        Assert.Contains(response.Results.Items, c => c.Id == matching.Id);
        Assert.DoesNotContain(response.Results.Items, c => c.Id == nonMatching.Id);
    }
}
