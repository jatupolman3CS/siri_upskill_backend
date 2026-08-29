using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;
using Siri.Persistence;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class GetEpisodeAttachmentsTests
{
    private static COURSE_EPISODE CreateEpisode(bool isFreePreview)
    {
        var course = COURSE.Create(
            "test-slug",
            "Test COURSE",
            Guid.NewGuid(),
            Guid.NewGuid(),
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            990m);

        var section = course.AddSection("Section 1");
        return section.AddEpisode("Episode 1", "Desc", isFreePreview);
    }

    private sealed class FakeEpisodeAccessReader(bool allowAccess) : IEpisodeAccessReader
    {
        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult(allowAccess);
        }
    }

    [Fact]
    public void EpisodeAttachmentResponse_DoesNotExposeStorageKey()
    {
        var properties = typeof(EpisodeAttachmentResponse).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Equals("StorageKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EpisodeAttachmentDownloadResponse_DoesNotExposeStorageKey()
    {
        var properties = typeof(EpisodeAttachmentDownloadResponse).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Equals("StorageKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EpisodeAccessHelper_AdminUser_ReturnsTrue()
    {
        var dummyDb = new AppDbContext(new DbContextOptions<AppDbContext>());
        var paidEpisode = CreateEpisode(isFreePreview: false);
        var reader = new FakeEpisodeAccessReader(allowAccess: false);

        var result = await EpisodeAccessHelper.CanUserAccessEpisodeAsync(
            dummyDb, reader, paidEpisode, userId: Guid.NewGuid(), isAdmin: true, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task EpisodeAccessHelper_FreePreviewEpisode_ReturnsTrueEvenIfAnonymous()
    {
        var dummyDb = new AppDbContext(new DbContextOptions<AppDbContext>());
        var previewEpisode = CreateEpisode(isFreePreview: true);
        var reader = new FakeEpisodeAccessReader(allowAccess: false);

        var result = await EpisodeAccessHelper.CanUserAccessEpisodeAsync(
            dummyDb, reader, previewEpisode, userId: null, isAdmin: false, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task EpisodeAccessHelper_AnonymousUserOnPaidEpisode_ReturnsFalse()
    {
        var dummyDb = new AppDbContext(new DbContextOptions<AppDbContext>());
        var paidEpisode = CreateEpisode(isFreePreview: false);
        var reader = new FakeEpisodeAccessReader(allowAccess: true);

        var result = await EpisodeAccessHelper.CanUserAccessEpisodeAsync(
            dummyDb, reader, paidEpisode, userId: null, isAdmin: false, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task EpisodeAccessHelper_EmptyUserIdOnPaidEpisode_ReturnsFalse()
    {
        var dummyDb = new AppDbContext(new DbContextOptions<AppDbContext>());
        var paidEpisode = CreateEpisode(isFreePreview: false);
        var reader = new FakeEpisodeAccessReader(allowAccess: true);

        var result = await EpisodeAccessHelper.CanUserAccessEpisodeAsync(
            dummyDb, reader, paidEpisode, userId: Guid.Empty, isAdmin: false, CancellationToken.None);

        Assert.False(result);
    }
}
