using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Features.AttachSessionRecording;
using Siri.Modules.Catalog.Infrastructure.Contracts;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// P11-13: the parts of <see cref="LiveRecordingAttacher"/> that need no database. Its reads and the "same rules as the manual attach" behaviour run against
/// real PostgreSQL in <c>LiveRecordingAttacherIntegrationTests</c>; the rules themselves are covered branch by branch in <c>AttachSessionRecordingHandlerTests</c>.
/// </summary>
public sealed class LiveRecordingAttacherTests
{
    // These calls are refused before the database or the handler is touched, so neither is needed (a null would throw if that ever changed).
    private static LiveRecordingAttacher CreateWithoutDependencies() => new(null!, null!);

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task AttachAsync_AnEmptyId_IsAValidationFailureWithoutTouchingAnything(bool emptyInstructor, bool emptyCourse, bool emptySession, bool emptyAsset)
    {
        var attacher = CreateWithoutDependencies();

        var result = await attacher.AttachAsync(
            emptyInstructor ? Guid.Empty : Guid.NewGuid(),
            emptyCourse ? Guid.Empty : Guid.NewGuid(),
            emptySession ? Guid.Empty : Guid.NewGuid(),
            emptyAsset ? Guid.Empty : Guid.NewGuid(),
            null,
            CancellationToken.None);

        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task ListEndedAsync_NonPositiveLimitOrEmptyRange_IsEmptyWithoutTouchingTheDatabase()
    {
        var attacher = CreateWithoutDependencies();
        var now = DateTime.UtcNow;

        Assert.Empty(await attacher.ListEndedAsync(now.AddHours(-1), now, 0, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedAsync(now.AddHours(-1), now, -5, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedAsync(now, now.AddHours(-1), 10, CancellationToken.None));
        Assert.Empty(await attacher.ListEndedAsync(now, now, 10, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_EmptyId_IsNullWithoutTouchingTheDatabase()
    {
        Assert.Null(await CreateWithoutDependencies().GetAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public void PublishedReasons_AreExactlyTheOnesTheManualAttachEndpointAnswers()
    {
        // One source of truth: the constants the endpoint uses are the ones other modules branch on.
        Assert.Equal(LiveRecordingAttachReasons.SessionNotStarted, LiveRecordingReasons.SessionNotStarted);
        Assert.Equal(LiveRecordingAttachReasons.AssetNotReady, LiveRecordingReasons.AssetNotReady);
        Assert.Equal(LiveRecordingAttachReasons.AssetInUse, LiveRecordingReasons.AssetInUse);
        Assert.Equal(LiveRecordingAttachReasons.EpisodeHasNoMedia, LiveRecordingReasons.EpisodeHasNoMedia);
        Assert.Equal(LiveRecordingAttachReasons.EpisodeInUse, LiveRecordingReasons.EpisodeInUse);

        Assert.Equal("live.recording_asset_in_use", LiveRecordingAttachReasons.AssetInUse);
        Assert.Equal("live.recording_asset_not_ready", LiveRecordingAttachReasons.AssetNotReady);
        Assert.Equal("live.session_not_started", LiveRecordingAttachReasons.SessionNotStarted);
    }
}
