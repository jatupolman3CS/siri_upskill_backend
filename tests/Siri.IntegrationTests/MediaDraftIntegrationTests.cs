using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AttachEpisodeMedia;
using Siri.Modules.Catalog.Features.AutosaveCourse;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>Exercises draft media persistence against PostgreSQL, including EF child tracking.</summary>
public sealed class MediaDraftIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly IClock _clock = new SystemClock();

    [Fact]
    public async Task CreateSectionAndEpisode_TrackedCourse_InsertsBothChildren()
    {
        var seed = await SeedAsync();
        Guid sectionId;
        Guid episodeId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var section = await new CreateCourseSectionHandler(db).HandleAsync(seed.UserId, seed.CourseId,
                new CreateCourseSectionCommand("Uploaded videos"), CancellationToken.None);
            Assert.True(section.IsSuccess);
            sectionId = section.Value.Id;
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var episode = await new CreateCourseEpisodeHandler(db).HandleAsync(seed.UserId, seed.CourseId,
                sectionId, new CreateCourseEpisodeCommand("First upload", null, true), CancellationToken.None);
            Assert.True(episode.IsSuccess);
            episodeId = episode.Value.Id;
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            var section = Assert.Single(course.Sections);
            Assert.Equal(sectionId, section.Id);
            Assert.Equal(episodeId, Assert.Single(section.Episodes).Id);
            Assert.Equal(1, course.EpisodeCount);
        }
    }

    [Fact]
    public async Task Autosave_NewGraph_InsertsChildrenAndReturnsPersistentIdsAndProviderDuration()
    {
        var seed = await SeedAsync();
        AutosaveCourseResponse response;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var command = Command(seed) with
            {
                TrailerMediaAssetId = seed.AssetId,
                Outcomes = ["Learn uploads"], Requirements = ["A browser"],
                Sections = [new(null, "New section", 0, [new(null, "New episode", null, 0, false, seed.AssetId, 9999)])],
            };
            var result = await Handler(db).HandleAsync(seed.UserId, seed.CourseId, command, CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            response = result.Value;
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            var section = Assert.Single(course.Sections);
            var episode = Assert.Single(section.Episodes);
            var returnedSection = Assert.Single(response.Sections!);
            Assert.Equal(section.Id, returnedSection.Id);
            Assert.Equal(episode.Id, Assert.Single(returnedSection.Episodes).Id);
            Assert.Equal(seed.AssetId, course.TrailerMediaAssetId);
            Assert.Equal(seed.AssetId, episode.MediaAssetId);
            Assert.Equal(95, episode.DurationSeconds);
            Assert.Equal(95, course.TotalDurationSeconds);
            Assert.Single(course.Outcomes);
            Assert.Single(course.Requirements);
        }
    }

    [Fact]
    public async Task Autosave_ExistingGraph_PreservesIdsReordersAndDetachesRemovedMedia()
    {
        var seed = await SeedAsync(withEpisodes: true);
        Guid[] episodeIds;
        Guid sectionId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            var section = Assert.Single(course.Sections);
            sectionId = section.Id;
            episodeIds = section.Episodes.OrderBy(episode => episode.SortOrder).Select(episode => episode.Id).ToArray();
            var command = Command(seed) with
            {
                Sections = [new(sectionId, "Saved section", 0,
                    [new(episodeIds[0], "First", null, 1, false), new(episodeIds[1], "Second", null, 0, false)])],
            };
            var result = await Handler(db).HandleAsync(seed.UserId, seed.CourseId, command, CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(episodeIds.Reverse(), Assert.Single(result.Value.Sections!).Episodes.Select(episode => episode.Id));
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            var section = Assert.Single(course.Sections);
            Assert.Equal(sectionId, section.Id);
            Assert.Equal(episodeIds.Reverse(), section.Episodes.OrderBy(episode => episode.SortOrder).Select(episode => episode.Id));
            Assert.All(section.Episodes, episode => Assert.Null(episode.MediaAssetId));
            Assert.Null(course.TrailerMediaAssetId);
            Assert.Equal(0, course.TotalDurationSeconds);
        }
    }

    [Fact]
    public async Task Autosave_StaleReorder_RollsBackTemporaryPositionsAndPreservesWinningDraft()
    {
        var seed = await SeedAsync(withEpisodes: true);
        Guid sectionId;
        Guid[] originalIds;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            var section = Assert.Single(course.Sections);
            sectionId = section.Id;
            originalIds = section.Episodes.OrderBy(episode => episode.SortOrder).Select(episode => episode.Id).ToArray();
            var winner = await Handler(db).HandleAsync(seed.UserId, seed.CourseId, Command(seed) with
            {
                Title = "Winning draft", Sections = null, TrailerMediaAssetId = seed.AssetId,
            }, CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await Handler(db).HandleAsync(seed.UserId, seed.CourseId, Command(seed) with
            {
                Sections = [new(sectionId, "Stale section", 0,
                    [new(originalIds[1], "Second", null, 0, false), new(originalIds[0], "First", null, 1, false)])],
            }, CancellationToken.None);
            Assert.True(stale.IsFailure);
            Assert.Equal("conflict", stale.Error.Code);
        }
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await LoadCourseAsync(db, seed.CourseId);
            Assert.Equal("Winning draft", course.Title);
            var episodes = Assert.Single(course.Sections).Episodes.OrderBy(episode => episode.SortOrder).ToArray();
            Assert.Equal(originalIds, episodes.Select(episode => episode.Id));
            Assert.Equal(new[] { 0, 1 }, episodes.Select(episode => episode.SortOrder));
            Assert.Equal(seed.AssetId, episodes[0].MediaAssetId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Autosave_AnotherUsersEpisodeOrTrailer_RejectsWithoutChangingDraft(bool useTrailer)
    {
        var seed = await SeedAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var foreignAsset = MEDIA_ASSET.Create("BunnyStream", Guid.NewGuid().ToString(), Guid.NewGuid(), true);
        db.MediaAssets().Add(foreignAsset);
        await db.SaveChangesAsync();
        var command = Command(seed) with
        {
            Title = "Unauthorized change",
            TrailerMediaAssetId = useTrailer ? foreignAsset.MEDIA_ASSET_ID : null,
            Sections = useTrailer ? [] : [new(null, "Section", 0, [new(null, "Episode", null, 0, false, foreignAsset.MEDIA_ASSET_ID)])],
        };
        var result = await Handler(db).HandleAsync(seed.UserId, seed.CourseId, command, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        // Even a later save in the same request scope cannot persist a partially mutated draft.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("Upload draft", (await LoadCourseAsync(db, seed.CourseId)).Title);
    }

    [Fact]
    public async Task AttachEpisodeMedia_ClientSuppliesDuration_UsesProviderDuration()
    {
        var seed = await SeedAsync(withEpisodes: true);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await LoadCourseAsync(db, seed.CourseId);
        var section = Assert.Single(course.Sections);
        var episode = section.Episodes.First();
        var result = await new AttachEpisodeMediaHandler(db, new MediaAssetContractService(db)).AttachAsync(
            seed.UserId, seed.CourseId, section.Id, episode.Id, new(seed.AssetId, 9999), CancellationToken.None);
        Assert.True(result.IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal(95, (await db.CourseEpisodes().SingleAsync(item => item.Id == episode.Id)).DurationSeconds);
    }

    private AutosaveCourseHandler Handler(AppDbContext db) => new(
        db,
        _clock,
        new MediaAssetContractService(db),
        new TeachingMaterialStorage(
            new InMemoryFileStorage(),
            new AcceptAllVirusScanner(),
            Options.Create(new EpisodeAttachmentOptions()),
            _clock,
            NullLogger<TeachingMaterialStorage>.Instance));

    private static AutosaveCourseCommand Command(Seed seed) => new(
        "Upload draft", null, null, seed.CategoryId, CourseLevel.Beginner, CourseLanguage.Thai,
        null, 100m, null, null, null, null, [], [], [], seed.RowVersion);

    private static Task<COURSE> LoadCourseAsync(AppDbContext db, Guid id) => db.Courses()
        .Include(course => course.Sections).ThenInclude(section => section.Episodes)
        .Include(course => course.Outcomes).Include(course => course.Requirements)
        .SingleAsync(course => course.Id == id);

    private async Task<Seed> SeedAsync(bool withEpisodes = false)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = Guid.NewGuid();
        var category = CATEGORY.Create($"upload-{Guid.NewGuid():N}", "Upload", "Upload", null, null, 0);
        var instructor = INSTRUCTOR_PROFILE.Apply(userId, "Upload tester", "Test", "Test");
        instructor.Approve(_clock);
        var course = COURSE.Create($"upload-{Guid.NewGuid():N}", "Upload draft", instructor.Id, category.Id,
            CourseLevel.Beginner, CourseLanguage.Thai, 100m);
        var asset = MEDIA_ASSET.Create("BunnyStream", Guid.NewGuid().ToString(), userId, true);
        asset.MarkReady(asset.PROVIDER_ASSET_ID, 95, null, _clock);
        if (withEpisodes)
        {
            var section = course.AddSection("Section");
            var first = course.AddEpisode(section.Id, "First", null, false);
            course.AddEpisode(section.Id, "Second", null, false);
            course.AttachEpisodeMedia(first.Id, asset.MEDIA_ASSET_ID, 95);
            course.SetTrailer(asset.MEDIA_ASSET_ID);
        }
        db.Categories().Add(category);
        db.InstructorProfiles().Add(instructor);
        db.Courses().Add(course);
        db.MediaAssets().Add(asset);
        await db.SaveChangesAsync();
        return new(userId, course.Id, category.Id, asset.MEDIA_ASSET_ID, course.RowVersion);
    }

    private sealed record Seed(Guid UserId, Guid CourseId, Guid CategoryId, Guid AssetId, byte[] RowVersion);
}
