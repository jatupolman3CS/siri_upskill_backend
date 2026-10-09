using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.ApproveCourse;
using Siri.Modules.Catalog.Features.SubmitCourseForReview;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Contracts;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

public sealed class CoursePublicationIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("Uploading", false)]
    [InlineData("Processing", false)]
    [InlineData("Failed", false)]
    [InlineData("Missing", false)]
    [InlineData("Foreign", false)]
    [InlineData("NoVideo", false)]
    [InlineData("Processing", true)]
    [InlineData("Foreign", true)]
    [InlineData("Ready", false)]
    [InlineData("Ready", true)]
    public async Task ReviewAndApproval_ValidateActualMediaBeforeChangingState(string mediaState, bool trailer)
    {
        foreach (var approving in new[] { false, true })
        {
            var clock = new SystemClock();
            var userId = Guid.NewGuid();
            Guid courseId;
            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var category = CATEGORY.Create($"publication-{Guid.NewGuid():N}", "Publication", "Publication", null, null, 0);
                var owner = INSTRUCTOR_PROFILE.Apply(userId, "Instructor", "Test", "Test");
                owner.Approve(clock);
                var course = COURSE.Create($"publication-{Guid.NewGuid():N}", "Publication test", owner.Id, category.Id,
                    CourseLevel.Beginner, CourseLanguage.Thai, 100m);
                var ready = MEDIA_ASSET.Create("BunnyStream", Guid.NewGuid().ToString(), userId, true);
                ready.MarkReady(ready.PROVIDER_ASSET_ID, 12, null, clock);
                db.MediaAssets().Add(ready);
                var section = course.AddSection("Lessons");
                var first = course.AddEpisode(section.Id, "Ready lesson", null, false);
                course.AttachEpisodeMedia(first.Id, ready.MEDIA_ASSET_ID, 12);
                Guid? testedId = null;
                if (mediaState == "Missing") testedId = Guid.NewGuid();
                else if (mediaState != "NoVideo")
                {
                    var media = MEDIA_ASSET.Create("BunnyStream", Guid.NewGuid().ToString(), mediaState == "Foreign" ? Guid.NewGuid() : userId, true);
                    if (mediaState == "Processing") media.MarkProcessing();
                    else if (mediaState == "Failed") media.MarkFailed("Provider rejected file");
                    else if (mediaState is "Ready" or "Foreign") media.MarkReady(media.PROVIDER_ASSET_ID, 15, null, clock);
                    db.MediaAssets().Add(media);
                    testedId = media.MEDIA_ASSET_ID;
                }
                if (trailer) course.SetTrailer(testedId);
                else
                {
                    var second = course.AddEpisode(section.Id, "Video under test", null, false);
                    if (testedId is { } id) course.AttachEpisodeMedia(second.Id, id, 15);
                }
                if (approving) course.SubmitForReview(clock);
                db.Categories().Add(category);
                db.InstructorProfiles().Add(owner);
                db.Courses().Add(course);
                await db.SaveChangesAsync();
                courseId = course.Id;
            }

            var cache = new RecordingCache();
            var searchIndex = new RecordingCourseSearchIndex();
            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var contract = new MediaAssetContractService(db);
                var success = approving
                    ? (await new ApproveCourseHandler(db, clock, cache, contract, new NullLiveMeetingReadinessReader(), SearchIndexer(db, searchIndex)).HandleAsync(courseId, CancellationToken.None)).IsSuccess
                    : (await new SubmitCourseForReviewHandler(db, contract, clock, new NullLiveMeetingReadinessReader()).HandleAsync(userId, courseId, CancellationToken.None)).IsSuccess;
                Assert.Equal(mediaState == "Ready", success);
                // An approval that publishes the course also tells the search index; one that is refused (or a mere submit) must not.
                Assert.Equal(approving && mediaState == "Ready", searchIndex.Upserted.Any(document => document.CourseId == courseId));
            }
            using (var scope = fixture.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var course = await db.Courses().SingleAsync(item => item.Id == courseId);
                var expected = mediaState == "Ready"
                    ? (approving ? CourseStatus.Published : CourseStatus.InReview)
                    : (approving ? CourseStatus.InReview : CourseStatus.Draft);
                Assert.Equal(expected, course.Status);
                Assert.Equal(approving && mediaState == "Ready", course.PublishedAtUtc.HasValue);
                Assert.Equal(approving && mediaState == "Ready" ? 1 : 0, cache.Evictions);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FreePreview_RequiresPublishedCourseAndPreviewFlag(bool published, bool freePreview)
    {
        Guid episodeId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = CATEGORY.Create($"preview-{Guid.NewGuid():N}", "Preview", "Preview", null, null, 0);
            var owner = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Instructor", "Test", "Test");
            var course = COURSE.Create($"preview-{Guid.NewGuid():N}", "Preview test", owner.Id, category.Id,
                CourseLevel.Beginner, CourseLanguage.Thai, 100m);
            var section = course.AddSection("Lessons");
            var episode = course.AddEpisode(section.Id, "Preview lesson", null, freePreview);
            episodeId = episode.Id;
            if (published)
            {
                course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 12);
                course.SubmitForReview(new SystemClock());
                course.Publish(new SystemClock());
            }
            db.Categories().Add(category);
            db.InstructorProfiles().Add(owner);
            db.Courses().Add(course);
            await db.SaveChangesAsync();
        }
        using var verification = fixture.CreateScope();
        var reader = new CatalogPriceContract(verification.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.Equal(published && freePreview, await reader.IsEpisodeFreePreviewAsync(episodeId, default));
    }

    /// <summary>Approval syncs the published course to the search index; a recording index lets the test see whether it did.</summary>
    private static CourseSearchIndexer SearchIndexer(AppDbContext db, RecordingCourseSearchIndex index) =>
        new(db, index, Options.Create(new MeilisearchOptions()), NullLogger<CourseSearchIndexer>.Instance);

    private sealed class RecordingCache : IOutputCacheStore
    {
        public int Evictions { get; private set; }
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) { Evictions++; return ValueTask.CompletedTask; }
        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);
        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
