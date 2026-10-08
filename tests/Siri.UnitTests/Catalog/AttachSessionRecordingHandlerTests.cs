using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AttachSessionRecording;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// P11-06 (docs/contracts/P11-06-live-recording-catchup.md section 3.1/5): every branch of the handler's decision logic
/// (<see cref="AttachSessionRecordingHandler.ApplyAsync"/>) against an in-memory aggregate graph and a fake <see cref="IMediaAssetContract"/> — no EF at all.
/// Loading the graph, the instructor-profile lookup, the single <c>SaveChangesAsync</c> and the output-cache eviction are the I/O half of the handler and are
/// covered by the integration tests (<c>LiveRecordingCatchUpIntegrationTests</c>).
/// </summary>
public sealed class AttachSessionRecordingHandlerTests
{
    /// <summary>"Now" for the handler. Sessions are created against an earlier clock (the domain refuses to schedule into the past) and then "time passes".</summary>
    private static readonly DateTime Now = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);

    private static readonly FakeClock SetupClock = new(Now.AddDays(-3));

    private readonly FakeClock _clock = new(Now);
    private readonly FakeMediaAssets _media = new();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _ownerProfileId = Guid.NewGuid();

    // ---- Arrange helpers --------------------------------------------------------------------------

    private sealed class FakeMediaAssets : IMediaAssetContract
    {
        private readonly Dictionary<Guid, MediaAssetSummary> _assets = [];

        public int Calls { get; private set; }

        public MediaAssetSummary Add(Guid uploadedByUserId, string status = "Ready", int? durationSeconds = 600)
        {
            var asset = new MediaAssetSummary(Guid.NewGuid(), uploadedByUserId, status, durationSeconds);
            _assets[asset.Id] = asset;
            return asset;
        }

        public Task<MediaAssetSummary?> GetAssetSummaryAsync(Guid mediaAssetId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_assets.GetValueOrDefault(mediaAssetId));
        }
    }

    private COURSE NewLiveCourse()
    {
        var course = COURSE.Create(
            $"live-{Guid.NewGuid():N}", "คอร์สสด", _ownerProfileId, Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 1200m);
        course.SetDeliveryFormat(DeliveryFormat.Live);
        return course;
    }

    private static COURSE_LIVE_SESSION AddSession(COURSE course, DateTime startsAtUtc, string title = "คาบที่ 1 พื้นฐาน") =>
        course.AddLiveSession(title, null, startsAtUtc, startsAtUtc.AddHours(2), SetupClock);

    /// <summary>A live course with one session that started (and ended) yesterday relative to <see cref="Now"/>.</summary>
    private (COURSE Course, COURSE_LIVE_SESSION Session) NewCourseWithFinishedSession(string title = "คาบที่ 1 พื้นฐาน")
    {
        var course = NewLiveCourse();
        return (course, AddSession(course, Now.AddDays(-1), title));
    }

    private Task<Result<AttachSessionRecordingHandler.AttachOutcome>> ApplyAsync(
        COURSE course, Guid sessionId, AttachSessionRecordingCommand command, Guid? userId = null, Guid? profileId = null, bool anonymousProfile = false) =>
        AttachSessionRecordingHandler.ApplyAsync(
            course,
            userId ?? _ownerUserId,
            anonymousProfile ? null : profileId ?? _ownerProfileId,
            sessionId,
            command,
            _media,
            _clock,
            CancellationToken.None);

    private static AttachSessionRecordingCommand AssetCommand(Guid assetId, Guid? sectionId = null, string? title = null) =>
        new(assetId, null, sectionId, title);

    private static AttachSessionRecordingCommand EpisodeCommand(Guid episodeId) => new(null, episodeId, null, null);

    private static IReadOnlyList<COURSE_EPISODE> Episodes(COURSE course) => course.Sections.SelectMany(s => s.Episodes).ToList();

    private static void AssertNothingChanged(COURSE course, COURSE_LIVE_SESSION session)
    {
        Assert.Empty(course.Sections);
        Assert.Equal(0, course.EpisodeCount);
        Assert.Null(session.RecordingEpisodeId);
    }

    private static void AssertFailure(Result<AttachSessionRecordingHandler.AttachOutcome> result, string code, string? reason = null)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(reason, result.Error.Reason);
    }

    // ---- Course / ownership / state guards (steps 1-5) ----------------------------------------------------

    [Fact]
    public async Task Apply_CallerOwnsAnotherInstructorProfile_Forbidden_AndNothingChanges()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id), profileId: Guid.NewGuid());

        AssertFailure(result, "forbidden");
        AssertNothingChanged(course, session);
        Assert.Equal(0, _media.Calls); // ownership is decided before any other module is asked anything
    }

    [Fact]
    public async Task Apply_CallerWithoutAnInstructorProfile_Forbidden()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id), anonymousProfile: true);

        AssertFailure(result, "forbidden");
        AssertNothingChanged(course, session);
    }

    [Fact]
    public async Task Apply_ArchivedCourse_Conflict_AndNothingChanges()
    {
        var (course, session) = NewCourseWithFinishedSession();
        course.Archive();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        AssertFailure(result, "conflict");
        Assert.Contains("เก็บถาวร", result.Error.Message);
        AssertNothingChanged(course, session);
    }

    [Fact]
    public async Task Apply_SessionNotOnThisCourse_NotFound()
    {
        var (course, _) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, Guid.NewGuid(), AssetCommand(asset.Id));

        AssertFailure(result, "not_found");
        Assert.Equal(0, _media.Calls);
    }

    [Fact]
    public async Task Apply_SessionThatBelongsToAnotherCourse_NotFound_ThePathIdsAreNeverTrusted()
    {
        var (course, _) = NewCourseWithFinishedSession();
        var (_, foreignSession) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, foreignSession.Id, AssetCommand(asset.Id));

        AssertFailure(result, "not_found");
        Assert.Null(foreignSession.RecordingEpisodeId);
    }

    [Fact]
    public async Task Apply_SessionNotStartedYet_Conflict_WithTheStableReason_AndNoMediaLookup()
    {
        var course = NewLiveCourse();
        var session = AddSession(course, Now.AddHours(3));
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.SessionNotStarted);
        Assert.Equal("live.session_not_started", result.Error.Reason);
        AssertNothingChanged(course, session);
        Assert.Equal(0, _media.Calls);
    }

    [Fact]
    public async Task Apply_SessionStartingExactlyNow_IsAllowed()
    {
        var course = NewLiveCourse();
        var session = AddSession(course, Now);
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Apply_CancelledSession_CanStillReceiveARecording()
    {
        var (course, session) = NewCourseWithFinishedSession();
        // Cancelled while it was still in the future; "time passed" since (the domain only cancels a session that has not ended).
        course.CancelLiveSession(session.Id, "ผู้สอนไม่สะดวก", SetupClock);
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CourseLiveSessionStatus.Cancelled, session.Status);
        Assert.Equal(result.Value.Response.RecordingEpisodeId, session.RecordingEpisodeId);
    }

    [Fact]
    public async Task Apply_NeitherOrBothTargets_ValidationError_DefenceInDepthBehindTheValidator()
    {
        var (course, session) = NewCourseWithFinishedSession();

        AssertFailure(await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(null, null, null, null)), "validation");
        AssertFailure(await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(Guid.NewGuid(), Guid.NewGuid(), null, null)), "validation");
        AssertNothingChanged(course, session);
    }

    // ---- Mode A: a media asset becomes the recording lesson (step 6) ---------------------------------------

    [Fact]
    public async Task ModeA_UnknownAsset_NotFound()
    {
        var (course, session) = NewCourseWithFinishedSession();

        var result = await ApplyAsync(course, session.Id, AssetCommand(Guid.NewGuid()));

        AssertFailure(result, "not_found");
        AssertNothingChanged(course, session);
    }

    [Fact]
    public async Task ModeA_AssetUploadedBySomeoneElse_Forbidden_AndNothingChanges()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var foreignAsset = _media.Add(uploadedByUserId: Guid.NewGuid());

        var result = await ApplyAsync(course, session.Id, AssetCommand(foreignAsset.Id));

        AssertFailure(result, "forbidden");
        AssertNothingChanged(course, session);
    }

    [Theory]
    [InlineData("Uploading", 600)]
    [InlineData("Processing", 600)]
    [InlineData("Failed", 600)]
    [InlineData("ready", 600)] // the contract compares the status text exactly; the media module emits "Ready"
    [InlineData("Ready", null)]
    [InlineData("Ready", 0)]
    public async Task ModeA_AssetNotReadyOrWithoutDuration_Conflict_WithTheStableReason(string status, int? durationSeconds)
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId, status, durationSeconds);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.AssetNotReady);
        Assert.Equal("live.recording_asset_not_ready", result.Error.Reason);
        AssertNothingChanged(course, session);
    }

    [Fact]
    public async Task ModeA_FirstAttach_CreatesTheRecordingsSection_AndALesson_WithTheDefaultTitle()
    {
        var (course, session) = NewCourseWithFinishedSession("Kickoff");
        var asset = _media.Add(_ownerUserId, durationSeconds: 3_725);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        var outcome = result.Value;
        var section = Assert.Single(course.Sections);
        Assert.Equal("บันทึกการสอนสด", section.Title);
        Assert.Equal(LiveRecordingDefaults.SectionTitle, section.Title);
        var episode = Assert.Single(section.Episodes);
        Assert.Equal("บันทึก: Kickoff", episode.Title);
        Assert.Equal(asset.Id, episode.MediaAssetId);
        Assert.Equal(3_725, episode.DurationSeconds);
        Assert.Equal(CourseEpisodeStatus.Ready, episode.Status);
        Assert.False(episode.IsFreePreview);
        Assert.Null(episode.Description);

        // The session now points at the lesson; the course counters were recalculated by the aggregate.
        Assert.Equal(episode.Id, session.RecordingEpisodeId);
        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(3_725, course.TotalDurationSeconds);

        // The response mirrors the lesson; the handler is told which rows are new (so it can insert them).
        Assert.Equal(new LiveSessionRecordingResponse(session.Id, episode.Id, section.Id, "บันทึก: Kickoff", 3_725, asset.Id, ReplacedExisting: false), outcome.Response);
        Assert.True(outcome.Changed);
        Assert.Same(section, outcome.NewSection);
        Assert.Same(episode, outcome.NewEpisode);
    }

    [Fact]
    public async Task ModeA_SecondSession_ReusesTheRecordingsSection_NotASecondOne()
    {
        var (course, first) = NewCourseWithFinishedSession("คาบแรก");
        var second = AddSession(course, Now.AddHours(-6), "คาบสอง");
        var assetOne = _media.Add(_ownerUserId, durationSeconds: 100);
        var assetTwo = _media.Add(_ownerUserId, durationSeconds: 200);

        var one = await ApplyAsync(course, first.Id, AssetCommand(assetOne.Id));
        var two = await ApplyAsync(course, second.Id, AssetCommand(assetTwo.Id));

        Assert.True(one.IsSuccess);
        Assert.True(two.IsSuccess);
        var section = Assert.Single(course.Sections);
        Assert.Equal(2, section.Episodes.Count);
        Assert.Null(two.Value.NewSection); // not created again
        Assert.Equal(section.Id, two.Value.Response.SectionId);
        Assert.Equal([0, 1], section.Episodes.Select(e => e.SortOrder).OrderBy(x => x).ToArray());
        Assert.Equal(300, course.TotalDurationSeconds);
    }

    [Fact]
    public async Task ModeA_RecordingsSectionRenamedByTheInstructor_IsNotFound_ANewDefaultSectionIsCreated()
    {
        // The section title is content the instructor owns; once renamed it is just another section and the default one is created afresh.
        var (course, first) = NewCourseWithFinishedSession("คาบแรก");
        var second = AddSession(course, Now.AddHours(-6), "คาบสอง");
        var assetOne = _media.Add(_ownerUserId);
        var assetTwo = _media.Add(_ownerUserId);
        await ApplyAsync(course, first.Id, AssetCommand(assetOne.Id));
        course.Sections.Single().Rename("วิดีโอย้อนหลัง");

        var result = await ApplyAsync(course, second.Id, AssetCommand(assetTwo.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, course.Sections.Count);
        Assert.NotNull(result.Value.NewSection);
    }

    [Fact]
    public async Task ModeA_PicksTheFirstRecordingsSection_WhenTheCourseHasTwoWithThatTitle()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var firstSection = course.AddSection(LiveRecordingDefaults.SectionTitle);
        course.AddSection(LiveRecordingDefaults.SectionTitle);
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(firstSection.Id, result.Value.Response.SectionId);
        Assert.Equal(2, course.Sections.Count);
    }

    [Fact]
    public async Task ModeA_ExplicitSection_PutsTheLessonThere_AndCreatesNoSection()
    {
        var (course, session) = NewCourseWithFinishedSession();
        course.AddSection("บทที่ 1");
        var target = course.AddSection("บทที่ 2");
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, sectionId: target.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(target.Id, result.Value.Response.SectionId);
        Assert.Null(result.Value.NewSection);
        Assert.Single(target.Episodes);
        Assert.Equal(2, course.Sections.Count);
    }

    [Fact]
    public async Task ModeA_ExplicitSectionNotOnThisCourse_NotFound_AndNothingChanges()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, sectionId: Guid.NewGuid()));

        AssertFailure(result, "not_found");
        AssertNothingChanged(course, session);
    }

    [Fact]
    public async Task ModeA_CustomTitle_IsTrimmed()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, title: "  บันทึก: Kickoff  "));

        Assert.Equal("บันทึก: Kickoff", result.Value.Response.EpisodeTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ModeA_BlankTitle_FallsBackToTheDefault(string? title)
    {
        var (course, session) = NewCourseWithFinishedSession("Kickoff");
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, title: title));

        Assert.Equal("บันทึก: Kickoff", result.Value.Response.EpisodeTitle);
    }

    [Fact]
    public async Task ModeA_VeryLongSessionTitle_DefaultLessonTitleIsCutToTheColumnLength()
    {
        var (course, session) = NewCourseWithFinishedSession(new string('ก', 200));
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        var title = result.Value.Response.EpisodeTitle;
        Assert.Equal(200, title.Length);
        Assert.StartsWith("บันทึก: ", title);
    }

    [Fact]
    public async Task ModeA_LongTitleEndingInASurrogatePair_IsNeverCutInTheMiddleOfIt()
    {
        // "บันทึก: " is 8 chars; 191 BMP chars + one astral char (2 UTF-16 units) puts the high surrogate exactly at index 199.
        var sessionTitle = new string('ก', 191) + "\U0001F600" + "tail";
        var (course, session) = NewCourseWithFinishedSession(sessionTitle);
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        var title = result.Value.Response.EpisodeTitle;
        Assert.True(title.Length <= 200);
        Assert.False(char.IsHighSurrogate(title[^1]), "the title must not end with a lone high surrogate");
    }

    [Fact]
    public async Task ModeA_AssetAlreadyUsedByAnotherLessonOfTheCourse_Conflict_WithTheStableReason()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);
        var section = course.AddSection("บทที่ 1");
        var regular = course.AddEpisode(section.Id, "บทเรียนปกติ", null, false);
        course.AttachEpisodeMedia(regular.Id, asset.Id, 600);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.AssetInUse);
        Assert.Equal("live.recording_asset_in_use", result.Error.Reason);
        Assert.Single(Episodes(course));
        Assert.Null(session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeA_AssetUsedByTheRecordingLessonOfAnotherSession_Conflict()
    {
        var (course, first) = NewCourseWithFinishedSession("คาบแรก");
        var second = AddSession(course, Now.AddHours(-6), "คาบสอง");
        var asset = _media.Add(_ownerUserId);
        Assert.True((await ApplyAsync(course, first.Id, AssetCommand(asset.Id))).IsSuccess);

        var result = await ApplyAsync(course, second.Id, AssetCommand(asset.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.AssetInUse);
        Assert.Null(second.RecordingEpisodeId);
        Assert.Single(Episodes(course));
    }

    [Fact]
    public async Task ModeA_AssetUsedByAnotherCourseOfTheSameInstructor_IsAllowed_TheCheckIsPerCourse()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var (otherCourse, _) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId);
        var otherSection = otherCourse.AddSection("บทที่ 1");
        var otherEpisode = otherCourse.AddEpisode(otherSection.Id, "บทเรียน", null, false);
        otherCourse.AttachEpisodeMedia(otherEpisode.Id, asset.Id, 600);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ModeA_AttachToAPublishedCourse_Succeeds_AndLeavesItPublished()
    {
        var (course, session) = NewCourseWithFinishedSession();
        course.Publish(SetupClock); // a Live course with a future (at setup time) session is publishable
        var asset = _media.Add(_ownerUserId, durationSeconds: 1_800);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CourseStatus.Published, course.Status);
        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(1_800, course.TotalDurationSeconds);
    }

    [Fact]
    public async Task ModeA_AttachToAnInReviewOrDraftCourse_Succeeds()
    {
        var (course, session) = NewCourseWithFinishedSession();
        Assert.Equal(CourseStatus.Draft, course.Status);

        var result = await ApplyAsync(course, session.Id, AssetCommand(_media.Add(_ownerUserId).Id));

        Assert.True(result.IsSuccess);
    }

    // ---- Mode A: replace in place / idempotent repeat ------------------------------------------------------------------

    [Fact]
    public async Task ModeA_AttachAnotherAssetToASessionThatAlreadyHasARecording_ReplacesTheMediaInPlace()
    {
        var (course, session) = NewCourseWithFinishedSession("Kickoff");
        var assetA = _media.Add(_ownerUserId, durationSeconds: 600);
        var assetB = _media.Add(_ownerUserId, durationSeconds: 900);
        var first = await ApplyAsync(course, session.Id, AssetCommand(assetA.Id));
        var episodeId = first.Value.Response.RecordingEpisodeId;

        var result = await ApplyAsync(course, session.Id, AssetCommand(assetB.Id));

        Assert.True(result.IsSuccess);
        var outcome = result.Value;
        Assert.True(outcome.Response.ReplacedExisting);
        Assert.True(outcome.Changed);
        Assert.Null(outcome.NewEpisode); // same lesson, same position, same learners' progress
        Assert.Null(outcome.NewSection);
        Assert.Equal(episodeId, outcome.Response.RecordingEpisodeId);
        var episode = Assert.Single(Episodes(course));
        Assert.Equal(episodeId, episode.Id);
        Assert.Equal(assetB.Id, episode.MediaAssetId);
        Assert.Equal(900, episode.DurationSeconds);
        Assert.Equal(900, outcome.Response.DurationSeconds);
        Assert.Equal(assetB.Id, outcome.Response.MediaAssetId);
        Assert.Equal("บันทึก: Kickoff", episode.Title); // no title supplied: untouched
        Assert.Equal(900, course.TotalDurationSeconds);
        Assert.Equal(episodeId, session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeA_ReplaceWithATitle_RenamesTheLesson_AndKeepsItsDescription()
    {
        var (course, session) = NewCourseWithFinishedSession("Kickoff");
        var first = await ApplyAsync(course, session.Id, AssetCommand(_media.Add(_ownerUserId).Id));
        var episode = Assert.Single(Episodes(course));
        episode.UpdateDetails(episode.Title, "คำอธิบายที่ผู้สอนเขียนเอง");

        var result = await ApplyAsync(course, session.Id, AssetCommand(_media.Add(_ownerUserId).Id, title: "บันทึกฉบับแก้ไข"));

        Assert.True(result.IsSuccess);
        Assert.Equal("บันทึกฉบับแก้ไข", episode.Title);
        Assert.Equal("คำอธิบายที่ผู้สอนเขียนเอง", episode.Description);
        Assert.Equal(first.Value.Response.RecordingEpisodeId, result.Value.Response.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeA_SameAssetAgain_IsIdempotent_NothingChanges_NotReplaced()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var asset = _media.Add(_ownerUserId, durationSeconds: 777);
        var first = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, title: "ชื่อเดิม"));

        var again = await ApplyAsync(course, session.Id, AssetCommand(asset.Id, title: "ชื่อที่ถูกเมิน"));

        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Response.ReplacedExisting);
        Assert.False(again.Value.Changed);
        Assert.Null(again.Value.NewEpisode);
        Assert.Null(again.Value.NewSection);
        Assert.Equal(first.Value.Response, again.Value.Response);
        var episode = Assert.Single(Episodes(course));
        Assert.Equal("ชื่อเดิม", episode.Title); // "ไม่แก้อะไร": the title of a repeat is ignored too
        Assert.Equal(777, course.TotalDurationSeconds);
    }

    [Fact]
    public async Task ModeA_ReplaceWithAnAssetUsedByAnotherLesson_Conflict_TheOldMediaStays()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var assetA = _media.Add(_ownerUserId);
        var assetB = _media.Add(_ownerUserId);
        await ApplyAsync(course, session.Id, AssetCommand(assetA.Id));
        var section = course.AddSection("บทที่ 1");
        var regular = course.AddEpisode(section.Id, "บทเรียนปกติ", null, false);
        course.AttachEpisodeMedia(regular.Id, assetB.Id, 600);

        var result = await ApplyAsync(course, session.Id, AssetCommand(assetB.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.AssetInUse);
        var recording = course.Sections.SelectMany(s => s.Episodes).Single(e => e.Id == session.RecordingEpisodeId);
        Assert.Equal(assetA.Id, recording.MediaAssetId);
    }

    [Fact]
    public async Task ModeA_RecordingLessonNoLongerOnTheCourse_AttachesAFreshLesson_AndRepointsTheSession()
    {
        // The session still points at a lesson that was removed from the (draft) course: it counts as "no recording lesson", not as an error.
        var (course, session) = NewCourseWithFinishedSession("Kickoff");
        course.AttachSessionRecording(session.Id, course.AddEpisode(course.AddSection("เดิม").Id, "บทเรียนเดิม", null, false).Id);
        var strayEpisodeId = session.RecordingEpisodeId!.Value;
        course.RemoveEpisode(strayEpisodeId);
        var asset = _media.Add(_ownerUserId);

        var result = await ApplyAsync(course, session.Id, AssetCommand(asset.Id));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Response.ReplacedExisting);
        Assert.NotEqual(strayEpisodeId, session.RecordingEpisodeId);
        Assert.Equal(result.Value.Response.RecordingEpisodeId, session.RecordingEpisodeId);
        Assert.NotNull(result.Value.NewEpisode);
    }

    // ---- Mode B: link an existing lesson (step 7) --------------------------------------------------------------------------

    private static COURSE_EPISODE AddLessonWithMedia(COURSE course, string title = "บทเรียนที่มีวิดีโอ", int duration = 600)
    {
        var section = course.Sections.FirstOrDefault() ?? course.AddSection("บทที่ 1");
        var episode = course.AddEpisode(section.Id, title, null, false);
        course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), duration);
        return episode;
    }

    [Fact]
    public async Task ModeB_LessonNotOnThisCourse_NotFound_AndTheMediaContractIsNeverAsked()
    {
        var (course, session) = NewCourseWithFinishedSession();

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(Guid.NewGuid()));

        AssertFailure(result, "not_found");
        Assert.Null(session.RecordingEpisodeId);
        Assert.Equal(0, _media.Calls);
    }

    [Fact]
    public async Task ModeB_LessonOfAnotherCourse_NotFound()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var (otherCourse, _) = NewCourseWithFinishedSession();
        var foreign = AddLessonWithMedia(otherCourse);

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(foreign.Id));

        AssertFailure(result, "not_found");
        Assert.Null(session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_LessonWithoutMedia_Conflict_WithTheStableReason()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var bare = course.AddEpisode(course.AddSection("บทที่ 1").Id, "ยังไม่มีวิดีโอ", null, false);

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(bare.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.EpisodeHasNoMedia);
        Assert.Equal("live.recording_episode_has_no_media", result.Error.Reason);
        Assert.Null(session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_LessonAlreadyTheRecordingOfAnotherSession_Conflict_WithTheStableReason()
    {
        var (course, first) = NewCourseWithFinishedSession("คาบแรก");
        var second = AddSession(course, Now.AddHours(-6), "คาบสอง");
        var lesson = AddLessonWithMedia(course);
        Assert.True((await ApplyAsync(course, first.Id, EpisodeCommand(lesson.Id))).IsSuccess);

        var result = await ApplyAsync(course, second.Id, EpisodeCommand(lesson.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.EpisodeInUse);
        Assert.Equal("live.recording_episode_in_use", result.Error.Reason);
        Assert.Null(second.RecordingEpisodeId);
        Assert.Equal(lesson.Id, first.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_LinksTheLesson_AndReportsItsOwnTitleDurationAndMedia()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var lesson = AddLessonWithMedia(course, "สรุปบทเรียน", duration: 1_234);

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(lesson.Id));

        Assert.True(result.IsSuccess);
        var outcome = result.Value;
        Assert.True(outcome.Changed);
        Assert.False(outcome.Response.ReplacedExisting);
        Assert.Null(outcome.NewEpisode); // the lesson already existed
        Assert.Null(outcome.NewSection);
        Assert.Equal(new LiveSessionRecordingResponse(session.Id, lesson.Id, lesson.SectionId, "สรุปบทเรียน", 1_234, lesson.MediaAssetId!.Value, false), outcome.Response);
        Assert.Equal(lesson.Id, session.RecordingEpisodeId);
        Assert.Equal(1, course.EpisodeCount); // linking creates nothing
    }

    [Fact]
    public async Task ModeB_LinkingADifferentLessonOverAnExistingRecording_IsReportedAsReplaced()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var first = AddLessonWithMedia(course, "ฉบับแรก");
        var second = AddLessonWithMedia(course, "ฉบับแก้");
        await ApplyAsync(course, session.Id, EpisodeCommand(first.Id));

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(second.Id));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Response.ReplacedExisting);
        Assert.Equal(second.Id, session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_LinkingTheSameLessonAgain_IsIdempotent()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var lesson = AddLessonWithMedia(course);
        await ApplyAsync(course, session.Id, EpisodeCommand(lesson.Id));

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(lesson.Id));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
        Assert.False(result.Value.Response.ReplacedExisting);
        Assert.Equal(lesson.Id, session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_TitleAndSectionInTheCommand_AreIgnored_TheLessonKeepsItsOwn()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var lesson = AddLessonWithMedia(course, "ชื่อเดิมของบทเรียน");

        var result = await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(null, lesson.Id, Guid.NewGuid(), "ชื่ออื่น"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ชื่อเดิมของบทเรียน", lesson.Title);
        Assert.Equal("ชื่อเดิมของบทเรียน", result.Value.Response.EpisodeTitle);
    }

    [Fact]
    public async Task ModeB_NotStartedSession_Conflict_BeforeAnyLessonIsLookedAt()
    {
        var course = NewLiveCourse();
        var session = AddSession(course, Now.AddHours(1));
        var lesson = AddLessonWithMedia(course);

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(lesson.Id));

        AssertFailure(result, "conflict", LiveRecordingReasons.SessionNotStarted);
        Assert.Null(session.RecordingEpisodeId);
    }

    [Fact]
    public async Task ModeB_NonOwner_Forbidden()
    {
        var (course, session) = NewCourseWithFinishedSession();
        var lesson = AddLessonWithMedia(course);

        var result = await ApplyAsync(course, session.Id, EpisodeCommand(lesson.Id), profileId: Guid.NewGuid());

        AssertFailure(result, "forbidden");
        Assert.Null(session.RecordingEpisodeId);
    }
}

/// <summary>
/// The change-tracking half of the handler, checked without a database: a context over a never-opened connection tracks the aggregate exactly as
/// <c>HandleAsync</c> query would have left it (<c>Attach</c> = "just loaded, nothing changed"), then the handler decision and insert-marking code run
/// against it. This is where the classic pre-generated-key trap lives — new children of a tracked aggregate with a client-assigned id are discovered as
/// existing rows (UPDATE of a row that does not exist) unless they are explicitly marked as inserts.
/// </summary>
public sealed class AttachSessionRecordingChangeTrackingTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock SetupClock = new(Now.AddDays(-3));

    private readonly FakeClock _clock = new(Now);
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _ownerProfileId = Guid.NewGuid();
    private readonly Dictionary<Guid, MediaAssetSummary> _assets = [];

    private sealed class FakeAssets(Dictionary<Guid, MediaAssetSummary> assets) : IMediaAssetContract
    {
        public Task<MediaAssetSummary?> GetAssetSummaryAsync(Guid mediaAssetId, CancellationToken cancellationToken) =>
            Task.FromResult(assets.GetValueOrDefault(mediaAssetId));
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=unit-test;Username=none;Password=none")
            .Options);

    private (COURSE Course, COURSE_LIVE_SESSION Session) NewCourse()
    {
        var course = COURSE.Create($"live-{Guid.NewGuid():N}", "คอร์สสด", _ownerProfileId, Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 1200m);
        course.SetDeliveryFormat(DeliveryFormat.Live);
        var start = Now.AddDays(-1);
        return (course, course.AddLiveSession("คาบที่ 1", null, start, start.AddHours(2), SetupClock));
    }

    private Guid ReadyAsset(int duration = 600)
    {
        var asset = new MediaAssetSummary(Guid.NewGuid(), _ownerUserId, "Ready", duration);
        _assets[asset.Id] = asset;
        return asset.Id;
    }

    private Task<Result<AttachSessionRecordingHandler.AttachOutcome>> ApplyAsync(COURSE course, Guid sessionId, AttachSessionRecordingCommand command) =>
        AttachSessionRecordingHandler.ApplyAsync(
            course, _ownerUserId, _ownerProfileId, sessionId, command, new FakeAssets(_assets), _clock, CancellationToken.None);

    private async Task<AttachSessionRecordingHandler.AttachOutcome> ApplyAndMarkAsync(AppDbContext context, COURSE course, Guid sessionId, AttachSessionRecordingCommand command)
    {
        var result = await ApplyAsync(course, sessionId, command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        AttachSessionRecordingHandler.MarkNewRowsAsInserts(context, result.Value);
        context.ChangeTracker.DetectChanges();
        return result.Value;
    }

    [Fact]
    public async Task FirstAttach_NewSectionAndLessonAreInserts_TheCourseAndTheSessionAreUpdates()
    {
        using var context = CreateContext();
        var (course, session) = NewCourse();
        context.Attach(course); // as loaded: Unchanged

        var outcome = await ApplyAndMarkAsync(context, course, session.Id, new AttachSessionRecordingCommand(ReadyAsset(), null, null, null));

        Assert.NotNull(outcome.NewSection);
        Assert.NotNull(outcome.NewEpisode);
        Assert.Equal(EntityState.Added, context.Entry(outcome.NewSection).State);
        Assert.Equal(EntityState.Added, context.Entry(outcome.NewEpisode).State);
        Assert.Equal(EntityState.Modified, context.Entry(course).State); // EpisodeCount / TotalDurationSeconds
        Assert.Equal(EntityState.Modified, context.Entry(session).State); // RecordingEpisodeId
        Assert.True(context.Entry(session).Property(s => s.RecordingEpisodeId).IsModified);
        Assert.Equal(2, context.ChangeTracker.Entries().Count(e => e.State == EntityState.Added)); // nothing else is inserted
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Deleted);
    }

    [Fact]
    public async Task WithoutTheExplicitMarking_NewChildrenOfATrackedAggregateWouldBeTreatedAsExistingRows()
    {
        // The premise of MarkNewRowsAsInserts, pinned: this is the failure the marking prevents (an UPDATE of rows that were never inserted).
        using var context = CreateContext();
        var (course, session) = NewCourse();
        context.Attach(course);

        var result = await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(ReadyAsset(), null, null, null));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(EntityState.Added, context.Entry(result.Value.NewSection!).State);
        Assert.NotEqual(EntityState.Added, context.Entry(result.Value.NewEpisode!).State);
    }

    [Fact]
    public async Task SecondAttach_ReusingTheRecordingsSection_InsertsOnlyTheLesson()
    {
        using var context = CreateContext();
        var (course, firstSession) = NewCourse();
        var start = Now.AddHours(-6);
        var secondSession = course.AddLiveSession("คาบที่ 2", null, start, start.AddHours(2), SetupClock);
        // The first class was attached in an earlier request: its rows exist, the context starts from that loaded state.
        Assert.True((await ApplyAsync(course, firstSession.Id, new AttachSessionRecordingCommand(ReadyAsset(), null, null, null))).IsSuccess);
        context.Attach(course);

        var outcome = await ApplyAndMarkAsync(context, course, secondSession.Id, new AttachSessionRecordingCommand(ReadyAsset(), null, null, null));

        Assert.Null(outcome.NewSection);
        Assert.NotNull(outcome.NewEpisode);
        Assert.Equal(EntityState.Added, context.Entry(outcome.NewEpisode).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(course.Sections.Single()).State);
        Assert.Equal(1, context.ChangeTracker.Entries().Count(e => e.State == EntityState.Added));
    }

    [Fact]
    public async Task ReplaceInPlace_InsertsNothing_ThePlayingLessonIsUpdated()
    {
        using var context = CreateContext();
        var (course, session) = NewCourse();
        Assert.True((await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(ReadyAsset(600), null, null, null))).IsSuccess);
        context.Attach(course);
        var episode = course.Sections.Single().Episodes.Single();

        var outcome = await ApplyAndMarkAsync(context, course, session.Id, new AttachSessionRecordingCommand(ReadyAsset(900), null, null, "ฉบับแก้"));

        Assert.True(outcome.Response.ReplacedExisting);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        Assert.Equal(EntityState.Modified, context.Entry(episode).State);
        Assert.True(context.Entry(episode).Property(e => e.MediaAssetId).IsModified);
        Assert.True(context.Entry(episode).Property(e => e.Title).IsModified);
        Assert.Equal(EntityState.Modified, context.Entry(course).State); // TotalDurationSeconds
        Assert.Equal(EntityState.Unchanged, context.Entry(session).State); // the pointer is already right
    }

    [Fact]
    public async Task IdempotentRepeat_ChangesNothingAtAll()
    {
        using var context = CreateContext();
        var (course, session) = NewCourse();
        var assetId = ReadyAsset();
        Assert.True((await ApplyAsync(course, session.Id, new AttachSessionRecordingCommand(assetId, null, null, null))).IsSuccess);
        context.Attach(course);

        var outcome = await ApplyAndMarkAsync(context, course, session.Id, new AttachSessionRecordingCommand(assetId, null, null, null));

        Assert.False(outcome.Changed);
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ModeB_LinkingAnExistingLesson_OnlyTheSessionIsUpdated()
    {
        using var context = CreateContext();
        var (course, session) = NewCourse();
        var lesson = course.AddEpisode(course.AddSection("บทที่ 1").Id, "บทเรียน", null, false);
        course.AttachEpisodeMedia(lesson.Id, Guid.NewGuid(), 300);
        context.Attach(course);

        var outcome = await ApplyAndMarkAsync(context, course, session.Id, new AttachSessionRecordingCommand(null, lesson.Id, null, null));

        Assert.True(outcome.Changed);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        Assert.Equal(EntityState.Modified, context.Entry(session).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(course).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(lesson).State);
    }
}

/// <summary>
/// Structural guarantees of the new endpoint (contract section 3): the route, instructors only, the partitioned per-user <c>live-user</c> limiter overriding the
/// class-level app-wide "default" window, no user id taken from the request, and the documented status codes.
/// </summary>
public sealed class AttachSessionRecordingEndpointContractTests
{
    private static readonly System.Reflection.MethodInfo Action =
        typeof(Siri.Api.Controllers.Catalog.LiveSessionsController).GetMethod(nameof(Siri.Api.Controllers.Catalog.LiveSessionsController.AttachSessionRecording))!;

    [Fact]
    public void Route_IsPostCoursesCourseIdLiveSessionsSessionIdRecording()
    {
        var controllerRoute = typeof(Siri.Api.Controllers.Catalog.LiveSessionsController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), inherit: false).Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>().Single();
        var http = Assert.Single(Action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), inherit: false).Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());

        Assert.Equal("api/catalog/instructor/courses/{courseId:guid}/live-sessions", controllerRoute.Template);
        Assert.Equal("{sessionId:guid}/recording", http.Template);
    }

    [Fact]
    public void Endpoint_IsInstructorOnly_AndNeverAnonymous()
    {
        var controller = typeof(Siri.Api.Controllers.Catalog.LiveSessionsController);
        var authorize = controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();

        Assert.Equal(AuthorizationPolicyNames.InstructorOnly, authorize.Policy);
        Assert.Empty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: false));
        Assert.Empty(Action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: false));
    }

    [Fact]
    public void Action_UsesThePerUserLiveUserLimiter_NotTheAppWideDefaultOfTheClass()
    {
        var limiter = Assert.Single(Action.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>());

        Assert.Equal(Siri.Api.Configuration.RateLimiterConfiguration.LiveUserPolicyName, limiter.PolicyName);
        Assert.Equal("live-user", limiter.PolicyName);
    }

    [Fact]
    public void Action_TakesNoUserIdFromTheRequest_OnlyCourseSessionAndTheCommand()
    {
        var bound = Action.GetParameters()
            .Where(p => p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromServicesAttribute), inherit: false).Length == 0 && p.ParameterType != typeof(CancellationToken))
            .Select(p => p.Name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["command", "courseId", "sessionId"], bound);
    }

    [Fact]
    public void Action_DocumentsTheContractsStatusCodes()
    {
        var codes = Action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Select(a => a.StatusCode)
            .Order()
            .ToArray();

        Assert.Equal([200, 400, 403, 404, 409, 429], codes);
        Assert.Contains(
            Action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute), inherit: false).Cast<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>(),
            a => a.StatusCode == 200 && a.Type == typeof(LiveSessionRecordingResponse));
    }

    [Fact]
    public void Response_SerializesAsCamelCaseWithExactlyTheAppendixProperties()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new LiveSessionRecordingResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "บันทึก: Kickoff", 3_725, Guid.NewGuid(), ReplacedExisting: true),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal(
            ["durationSeconds", "episodeTitle", "mediaAssetId", "recordingEpisodeId", "replacedExisting", "sectionId", "sessionId"],
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.True(document.RootElement.GetProperty("replacedExisting").GetBoolean());
    }
}

public sealed class AttachSessionRecordingValidatorTests
{
    private readonly AttachSessionRecordingCommandValidator _validator = new();

    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void Validate_AssetOnly_IsValid() =>
        Assert.True(_validator.Validate(new AttachSessionRecordingCommand(Id, null, null, null)).IsValid);

    [Fact]
    public void Validate_AssetWithSectionAndTitle_IsValid() =>
        Assert.True(_validator.Validate(new AttachSessionRecordingCommand(Id, null, Guid.NewGuid(), "บันทึก: Kickoff")).IsValid);

    [Fact]
    public void Validate_EpisodeOnly_IsValid() =>
        Assert.True(_validator.Validate(new AttachSessionRecordingCommand(null, Id, null, null)).IsValid);

    [Fact]
    public void Validate_NeitherTarget_IsInvalid() =>
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(null, null, null, null)).IsValid);

    [Fact]
    public void Validate_BothTargets_IsInvalid() =>
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(Id, Guid.NewGuid(), null, null)).IsValid);

    [Fact]
    public void Validate_SectionWithAnEpisodeTarget_IsInvalid()
    {
        var result = _validator.Validate(new AttachSessionRecordingCommand(null, Id, Guid.NewGuid(), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AttachSessionRecordingCommand.SectionId));
    }

    [Fact]
    public void Validate_EmptyAssetId_IsInvalid() =>
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(Guid.Empty, null, null, null)).IsValid);

    [Fact]
    public void Validate_EmptyEpisodeId_IsInvalid() =>
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(null, Guid.Empty, null, null)).IsValid);

    [Fact]
    public void Validate_EmptySectionId_IsInvalid() =>
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(Id, null, Guid.Empty, null)).IsValid);

    [Fact]
    public void Validate_TitleOf200CharsAfterTrim_IsValid() =>
        Assert.True(_validator.Validate(new AttachSessionRecordingCommand(Id, null, null, "  " + new string('ก', 200) + "  ")).IsValid);

    [Fact]
    public void Validate_TitleOf201Chars_IsInvalid()
    {
        var result = _validator.Validate(new AttachSessionRecordingCommand(Id, null, null, new string('ก', 201)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AttachSessionRecordingCommand.EpisodeTitle));
    }

    [Fact]
    public void Validate_EpisodeTitleIgnoredInEpisodeMode_StillMustFitTheLimit()
    {
        // The title is not used in mode B but is still bounded input.
        Assert.False(_validator.Validate(new AttachSessionRecordingCommand(null, Id, null, new string('x', 201))).IsValid);
        Assert.True(_validator.Validate(new AttachSessionRecordingCommand(null, Id, null, "anything short")).IsValid);
    }
}
