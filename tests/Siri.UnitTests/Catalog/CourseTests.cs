using Siri.Modules.Catalog.Domain;

namespace Siri.UnitTests.Catalog;

public class CourseTests
{
    private static COURSE CreateDraftCourse() =>
        COURSE.Create("web-development", "Web Development", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);

    [Fact]
    public void Create_ValidInput_ReturnsDraftCourseWithGivenFields()
    {
        var instructorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var course = COURSE.Create("web-development", "Web Development", instructorId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m);

        Assert.NotEqual(Guid.Empty, course.Id);
        Assert.Equal("web-development", course.Slug);
        Assert.Equal("Web Development", course.Title);
        Assert.Equal(instructorId, course.InstructorId);
        Assert.Equal(categoryId, course.CategoryId);
        Assert.Equal(CourseLevel.Beginner, course.Level);
        Assert.Equal(CourseLanguage.Thai, course.Language);
        Assert.Equal(990m, course.Price);
        Assert.Equal("THB", course.Currency);
        Assert.Equal(CourseStatus.Draft, course.Status);
        Assert.Null(course.PublishedAtUtc);
        Assert.Empty(course.Sections);
        Assert.Empty(course.Outcomes);
        Assert.Empty(course.Requirements);
    }

    [Theory]
    [InlineData("", "Web Development")]
    [InlineData("web-development", "")]
    public void Create_MissingRequiredField_ThrowsArgumentException(string slug, string title)
    {
        Assert.Throws<ArgumentException>(() =>
            COURSE.Create(slug, title, Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m));
    }

    [Fact]
    public void Create_NegativePrice_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            COURSE.Create("web-development", "Web Development", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, -1m));
    }

    [Fact]
    public void AddSection_MultipleSections_AutoAppendsSortOrder()
    {
        var course = CreateDraftCourse();

        var first = course.AddSection("Section 1");
        var second = course.AddSection("Section 2");

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.Equal(2, course.Sections.Count);
    }

    [Fact]
    public void AddEpisode_MultipleEpisodes_AutoAppendsSortOrderAndLinksToCourseAndSection()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");

        var first = section.AddEpisode("Episode 1", null, isFreePreview: false);
        var second = section.AddEpisode("Episode 2", null, isFreePreview: false);

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.Equal(2, section.Episodes.Count);
        Assert.Equal(course.Id, first.CourseId);
        Assert.Equal(section.Id, first.SectionId);
    }

    [Fact]
    public void ReorderSections_FullSet_ReassignsSortOrder()
    {
        var course = CreateDraftCourse();
        var first = course.AddSection("Section 1");
        var second = course.AddSection("Section 2");

        course.ReorderSections([second.Id, first.Id]);

        Assert.Equal(0, second.SortOrder);
        Assert.Equal(1, first.SortOrder);
    }

    [Fact]
    public void ReorderSections_PartialSet_ThrowsArgumentException()
    {
        var course = CreateDraftCourse();
        var first = course.AddSection("Section 1");
        course.AddSection("Section 2");

        Assert.Throws<ArgumentException>(() => course.ReorderSections([first.Id]));
    }

    [Fact]
    public void ReorderEpisodes_FullSet_ReassignsSortOrder()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var first = section.AddEpisode("Episode 1", null, isFreePreview: false);
        var second = section.AddEpisode("Episode 2", null, isFreePreview: false);

        section.ReorderEpisodes([second.Id, first.Id]);

        Assert.Equal(0, second.SortOrder);
        Assert.Equal(1, first.SortOrder);
    }

    [Fact]
    public void ReorderEpisodes_PartialSet_ThrowsArgumentException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var first = section.AddEpisode("Episode 1", null, isFreePreview: false);
        section.AddEpisode("Episode 2", null, isFreePreview: false);

        Assert.Throws<ArgumentException>(() => section.ReorderEpisodes([first.Id]));
    }

    [Fact]
    public void AddOutcome_MultipleOutcomes_AutoAppendsSortOrder()
    {
        var course = CreateDraftCourse();

        var first = course.AddOutcome("เข้าใจพื้นฐาน HTML/CSS");
        var second = course.AddOutcome("สร้างเว็บไซต์ได้เอง");

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
    }

    [Fact]
    public void AddRequirement_MultipleRequirements_AutoAppendsSortOrder()
    {
        var course = CreateDraftCourse();

        var first = course.AddRequirement("มีคอมพิวเตอร์");
        var second = course.AddRequirement("รู้พื้นฐานการใช้อินเทอร์เน็ต");

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
    }

    [Fact]
    public void AttachMedia_ValidInput_SetsMediaAssetIdDurationAndReadyStatus()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = section.AddEpisode("Episode 1", null, isFreePreview: false);
        var mediaAssetId = Guid.NewGuid();

        episode.AttachMedia(mediaAssetId, 600);

        Assert.Equal(mediaAssetId, episode.MediaAssetId);
        Assert.Equal(600, episode.DurationSeconds);
        Assert.Equal(CourseEpisodeStatus.Ready, episode.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AttachMedia_NonPositiveDuration_ThrowsArgumentOutOfRangeException(int durationSeconds)
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = section.AddEpisode("Episode 1", null, isFreePreview: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => episode.AttachMedia(Guid.NewGuid(), durationSeconds));
    }

    [Fact]
    public void RemoveMedia_AttachedEpisode_ClearsMediaAndRevertsToDraftStatus()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = section.AddEpisode("Episode 1", null, isFreePreview: false);
        episode.AttachMedia(Guid.NewGuid(), 600);

        episode.RemoveMedia();

        Assert.Null(episode.MediaAssetId);
        Assert.Null(episode.DurationSeconds);
        Assert.Equal(CourseEpisodeStatus.Draft, episode.Status);
    }

    [Fact]
    public void Publish_EpisodeWithMediaAttached_SucceedsAndSetsPublishedAtUtc()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = section.AddEpisode("Episode 1", null, isFreePreview: false);
        episode.AttachMedia(Guid.NewGuid(), 600);
        var clock = new FakeClock(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc));

        course.Publish(clock);

        Assert.Equal(CourseStatus.Published, course.Status);
        Assert.Equal(clock.UtcNow, course.PublishedAtUtc);
    }

    [Fact]
    public void Publish_NoSections_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();

        Assert.Throws<InvalidOperationException>(() => course.Publish(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void Publish_EpisodesButNoneHaveMedia_ThrowsInvalidOperationExceptionAndLeavesStatusUnchanged()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false); // no AttachMedia call

        Assert.Throws<InvalidOperationException>(() => course.Publish(new FakeClock(DateTime.UtcNow)));
        Assert.Equal(CourseStatus.Draft, course.Status); // rejected attempt must not mutate state
    }

    [Fact]
    public void Publish_AlreadyPublished_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.Publish(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void Publish_FromRejected_ClearsRejectionReason()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview(new FakeClock(DateTime.UtcNow));
        course.Reject("ต้องแก้คำอธิบาย");

        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Null(course.RejectionReason);
    }

    [Fact]
    public void SubmitForReview_DraftWithEpisodeThatHasMedia_TransitionsToInReview()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        course.SubmitForReview(new FakeClock(DateTime.UtcNow));

        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Fact]
    public void SubmitForReview_NoEpisodeWithMedia_ThrowsInvalidOperationExceptionAndLeavesStatusUnchanged()
    {
        var course = CreateDraftCourse();
        course.AddSection("Section 1"); // no episodes at all

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview(new FakeClock(DateTime.UtcNow)));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public void SubmitForReview_FromInReview_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void SubmitForReview_FromPublished_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void SubmitForReview_FromRejected_ClearsRejectionReasonAndTransitionsToInReview()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview(new FakeClock(DateTime.UtcNow));
        course.Reject("ต้องแก้คำอธิบาย");

        course.SubmitForReview(new FakeClock(DateTime.UtcNow));

        Assert.Equal(CourseStatus.InReview, course.Status);
        Assert.Null(course.RejectionReason);
    }

    [Fact]
    public void Reject_FromInReview_SetsRejectedStatusAndReason()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview(new FakeClock(DateTime.UtcNow));

        course.Reject("คำอธิบายไม่ครบถ้วน");

        Assert.Equal(CourseStatus.Rejected, course.Status);
        Assert.Equal("คำอธิบายไม่ครบถ้วน", course.RejectionReason);
    }

    [Fact]
    public void Reject_FromDraft_ThrowsInvalidOperationExceptionAndLeavesStatusUnchanged()
    {
        var course = CreateDraftCourse();

        Assert.Throws<InvalidOperationException>(() => course.Reject("เหตุผล"));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public void Reject_EmptyReason_ThrowsArgumentException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview(new FakeClock(DateTime.UtcNow));

        Assert.Throws<ArgumentException>(() => course.Reject(""));
    }

    [Fact]
    public void SetPricing_NegativePrice_ThrowsArgumentOutOfRangeException()
    {
        var course = CreateDraftCourse();

        Assert.Throws<ArgumentOutOfRangeException>(() => course.SetPricing(-1m, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetAccessDuration_ZeroOrNegative_ThrowsArgumentOutOfRangeException(int accessDurationDays)
    {
        var course = CreateDraftCourse();

        Assert.Throws<ArgumentOutOfRangeException>(() => course.SetAccessDuration(accessDurationDays));
    }

    [Fact]
    public void SetAccessDuration_Null_MeansLifetimeAccess()
    {
        var course = CreateDraftCourse();
        course.SetAccessDuration(30);

        course.SetAccessDuration(null);

        Assert.Null(course.AccessDurationDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetEnrollmentPolicy_ZeroOrNegativeMaxSeats_ThrowsArgumentOutOfRangeException(int maxSeats)
    {
        var course = CreateDraftCourse();

        Assert.Throws<ArgumentOutOfRangeException>(() => course.SetEnrollmentPolicy(null, maxSeats));
    }

    [Fact]
    public void SetEnrollmentPolicy_NonUtcDeadline_ThrowsArgumentException()
    {
        var course = CreateDraftCourse();
        var localDeadline = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => course.SetEnrollmentPolicy(localDeadline, null));
    }

    [Fact]
    public void SetEnrollmentPolicy_NullDeadlineAndNullMaxSeats_MeansNoRestriction()
    {
        var course = CreateDraftCourse();
        course.SetEnrollmentPolicy(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), 10);

        course.SetEnrollmentPolicy(null, null);

        Assert.Null(course.EnrollmentDeadlineUtc);
        Assert.Null(course.MaxSeats);
    }

    [Fact]
    public void SetEnrollmentPolicy_ValidUtcDeadlineAndPositiveMaxSeats_SetsBothFields()
    {
        var course = CreateDraftCourse();
        var deadline = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        course.SetEnrollmentPolicy(deadline, 50);

        Assert.Equal(deadline, course.EnrollmentDeadlineUtc);
        Assert.Equal(50, course.MaxSeats);
    }

    [Fact]
    public void SetEnrollmentPolicy_MaxSeatsBelowCurrentSeatsUsed_DoesNotThrow()
    {
        // SeatsUsed is only ever mutated by Commerce's raw ExecuteUpdateAsync (ICatalogPriceContract
        // .TryReserveSeatAsync), never through any COURSE method — reflection is the only way to get a
        // non-zero SeatsUsed onto an in-memory instance for this test, same technique already used
        // elsewhere in this codebase (e.g. OrderExpiryJobTests/PromoCodeTests set CreatedAtUtc this way).
        var course = CreateDraftCourse();
        typeof(COURSE).GetProperty(nameof(COURSE.SeatsUsed))!.SetValue(course, 10);

        var exception = Record.Exception(() => course.SetEnrollmentPolicy(null, 1));

        Assert.Null(exception);
        Assert.Equal(1, course.MaxSeats);
        Assert.Equal(10, course.SeatsUsed);
    }

    [Fact]
    public void RemoveSection_ValidDraftCourse_RemovesSectionAndReindexesRemaining()
    {
        var course = CreateDraftCourse();
        var sec1 = course.AddSection("Section 1");
        var sec2 = course.AddSection("Section 2");
        var sec3 = course.AddSection("Section 3");

        course.RemoveSection(sec2.Id);

        Assert.Equal(2, course.Sections.Count);
        Assert.DoesNotContain(course.Sections, s => s.Id == sec2.Id);
        Assert.Equal(0, course.Sections.ElementAt(0).SortOrder);
        Assert.Equal(sec1.Id, course.Sections.ElementAt(0).Id);
        Assert.Equal(1, course.Sections.ElementAt(1).SortOrder);
        Assert.Equal(sec3.Id, course.Sections.ElementAt(1).Id);
    }

    [Fact]
    public void RemoveSection_PublishedCourse_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var sec1 = course.AddSection("Section 1");
        sec1.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.RemoveSection(sec1.Id));
    }

    [Fact]
    public void RemoveSection_UnknownSectionId_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        course.AddSection("Section 1");

        Assert.Throws<InvalidOperationException>(() => course.RemoveSection(Guid.NewGuid()));
    }

    [Fact]
    public void RemoveEpisode_ValidDraftCourse_RemovesEpisodeAndReindexesRemaining()
    {
        var course = CreateDraftCourse();
        var sec = course.AddSection("Section 1");
        var ep1 = sec.AddEpisode("Ep 1", null, isFreePreview: false);
        var ep2 = sec.AddEpisode("Ep 2", null, isFreePreview: false);
        var ep3 = sec.AddEpisode("Ep 3", null, isFreePreview: false);

        course.RemoveEpisode(ep2.Id);

        Assert.Equal(2, sec.Episodes.Count);
        Assert.DoesNotContain(sec.Episodes, e => e.Id == ep2.Id);
        Assert.Equal(0, sec.Episodes.ElementAt(0).SortOrder);
        Assert.Equal(ep1.Id, sec.Episodes.ElementAt(0).Id);
        Assert.Equal(1, sec.Episodes.ElementAt(1).SortOrder);
        Assert.Equal(ep3.Id, sec.Episodes.ElementAt(1).Id);
    }

    [Fact]
    public void RemoveEpisode_PublishedCourse_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var sec1 = course.AddSection("Section 1");
        var ep1 = sec1.AddEpisode("Episode 1", null, isFreePreview: false);
        ep1.AttachMedia(Guid.NewGuid(), 600);
        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.RemoveEpisode(ep1.Id));
    }

    [Fact]
    public void SetOutcomes_ReplacesOutcomesWithSequentialSortOrder()
    {
        var course = CreateDraftCourse();
        course.AddOutcome("Old 1");
        course.AddOutcome("Old 2");

        course.SetOutcomes(["New 1", "New 2", "New 3"]);

        Assert.Equal(3, course.Outcomes.Count);
        Assert.Equal("New 1", course.Outcomes.ElementAt(0).Text);
        Assert.Equal(0, course.Outcomes.ElementAt(0).SortOrder);
        Assert.Equal("New 2", course.Outcomes.ElementAt(1).Text);
        Assert.Equal(1, course.Outcomes.ElementAt(1).SortOrder);
        Assert.Equal("New 3", course.Outcomes.ElementAt(2).Text);
        Assert.Equal(2, course.Outcomes.ElementAt(2).SortOrder);
    }

    [Fact]
    public void SetRequirements_ReplacesRequirementsWithSequentialSortOrder()
    {
        var course = CreateDraftCourse();
        course.AddRequirement("Old 1");

        course.SetRequirements(["Req 1", "Req 2"]);

        Assert.Equal(2, course.Requirements.Count);
        Assert.Equal("Req 1", course.Requirements.ElementAt(0).Text);
        Assert.Equal(0, course.Requirements.ElementAt(0).SortOrder);
        Assert.Equal("Req 2", course.Requirements.ElementAt(1).Text);
        Assert.Equal(1, course.Requirements.ElementAt(1).SortOrder);
    }

    // ---- EpisodeCount/TotalDurationSeconds self-maintenance -------------------------------------
    // Regression coverage for the bug where seeded/created courses always reported 0 episodes and 0
    // duration: COURSE.AddEpisode/AttachEpisodeMedia/RemoveEpisodeMedia/RemoveEpisode/RemoveSection/
    // AddSection must all keep these two denormalized columns correct via RecalculateEpisodeStats.

    [Fact]
    public void AddEpisode_ThroughCourse_IncreasesEpisodeCountButNotDurationBeforeMediaAttached()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");

        course.AddEpisode(section.Id, "Episode 1", null, isFreePreview: false);

        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(0, course.TotalDurationSeconds); // no media attached yet -> DurationSeconds is null
    }

    [Fact]
    public void AddEpisode_UnknownSectionId_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        course.AddSection("Section 1");

        Assert.Throws<InvalidOperationException>(() => course.AddEpisode(Guid.NewGuid(), "Episode 1", null, isFreePreview: false));
    }

    [Fact]
    public void AttachEpisodeMedia_ThroughCourse_IncreasesTotalDurationSeconds()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = course.AddEpisode(section.Id, "Episode 1", null, isFreePreview: false);

        course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 600);

        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(600, course.TotalDurationSeconds);
    }

    [Fact]
    public void AttachEpisodeMedia_MultipleEpisodesAcrossSections_SumsAllDurations()
    {
        var course = CreateDraftCourse();
        var section1 = course.AddSection("Section 1");
        var section2 = course.AddSection("Section 2");
        var ep1 = course.AddEpisode(section1.Id, "Episode 1", null, isFreePreview: true);
        var ep2 = course.AddEpisode(section1.Id, "Episode 2", null, isFreePreview: false);
        var ep3 = course.AddEpisode(section2.Id, "Episode 3", null, isFreePreview: false);

        course.AttachEpisodeMedia(ep1.Id, Guid.NewGuid(), 300);
        course.AttachEpisodeMedia(ep2.Id, Guid.NewGuid(), 450);
        course.AttachEpisodeMedia(ep3.Id, Guid.NewGuid(), 900);

        Assert.Equal(3, course.EpisodeCount);
        Assert.Equal(1650, course.TotalDurationSeconds);
    }

    [Fact]
    public void AttachEpisodeMedia_UnknownEpisodeId_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        course.AddSection("Section 1");

        Assert.Throws<InvalidOperationException>(() => course.AttachEpisodeMedia(Guid.NewGuid(), Guid.NewGuid(), 600));
    }

    [Fact]
    public void RemoveEpisodeMedia_ThroughCourse_DecreasesTotalDurationSecondsButKeepsEpisodeCount()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var episode = course.AddEpisode(section.Id, "Episode 1", null, isFreePreview: false);
        course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 600);

        course.RemoveEpisodeMedia(episode.Id);

        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(0, course.TotalDurationSeconds);
    }

    [Fact]
    public void RemoveEpisode_ThroughCourse_DecreasesEpisodeCountAndTotalDurationSeconds()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        var ep1 = course.AddEpisode(section.Id, "Episode 1", null, isFreePreview: true);
        course.AttachEpisodeMedia(ep1.Id, Guid.NewGuid(), 300);
        var ep2 = course.AddEpisode(section.Id, "Episode 2", null, isFreePreview: false);
        course.AttachEpisodeMedia(ep2.Id, Guid.NewGuid(), 700);

        course.RemoveEpisode(ep2.Id);

        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(300, course.TotalDurationSeconds);
    }

    [Fact]
    public void AddSection_WithMultipleEpisodesEach_EpisodeCountAndTotalDurationSecondsReflectWholeCourse()
    {
        var course = CreateDraftCourse();
        var section1 = course.AddSection("Section 1");
        var ep1 = course.AddEpisode(section1.Id, "Episode 1", null, isFreePreview: true);
        course.AttachEpisodeMedia(ep1.Id, Guid.NewGuid(), 120);
        var ep2 = course.AddEpisode(section1.Id, "Episode 2", null, isFreePreview: false);
        course.AttachEpisodeMedia(ep2.Id, Guid.NewGuid(), 180);

        var section2 = course.AddSection("Section 2");
        var ep3 = course.AddEpisode(section2.Id, "Episode 3", null, isFreePreview: false);
        course.AttachEpisodeMedia(ep3.Id, Guid.NewGuid(), 240);

        Assert.Equal(3, course.EpisodeCount);
        Assert.Equal(540, course.TotalDurationSeconds);
    }

    [Fact]
    public void RemoveSection_WithEpisodes_DecreasesEpisodeCountAndTotalDurationSecondsByWholeSection()
    {
        var course = CreateDraftCourse();
        var section1 = course.AddSection("Section 1");
        var ep1 = course.AddEpisode(section1.Id, "Episode 1", null, isFreePreview: true);
        course.AttachEpisodeMedia(ep1.Id, Guid.NewGuid(), 300);

        var section2 = course.AddSection("Section 2");
        var ep2 = course.AddEpisode(section2.Id, "Episode 2", null, isFreePreview: false);
        course.AttachEpisodeMedia(ep2.Id, Guid.NewGuid(), 400);
        var ep3 = course.AddEpisode(section2.Id, "Episode 3", null, isFreePreview: false);
        course.AttachEpisodeMedia(ep3.Id, Guid.NewGuid(), 500);

        course.RemoveSection(section2.Id);

        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(300, course.TotalDurationSeconds);
    }

    [Fact]
    public void AddSection_EmptyCourse_DoesNotChangeEpisodeCountOrTotalDurationSeconds()
    {
        var course = CreateDraftCourse();

        course.AddSection("Section 1");

        Assert.Equal(0, course.EpisodeCount);
        Assert.Equal(0, course.TotalDurationSeconds);
    }

    [Fact]
    public void SetTrailer_ValidMediaAssetId_SetsTrailerMediaAssetId()
    {
        var course = CreateDraftCourse();
        var trailerId = Guid.NewGuid();

        course.SetTrailer(trailerId);

        Assert.Equal(trailerId, course.TrailerMediaAssetId);
    }

    [Fact]
    public void SetTrailer_Null_ClearsTrailerMediaAssetId()
    {
        var course = CreateDraftCourse();
        course.SetTrailer(Guid.NewGuid());

        course.SetTrailer(null);

        Assert.Null(course.TrailerMediaAssetId);
    }

    // ---- Publish/SubmitForReview — Live/Hybrid invariant (task P11-01) --------------------------
    // docs/contracts/P11-01-catalog-live-sessions.md §2.4/§2.7: Live/Hybrid can satisfy the "has media
    // or a future scheduled session" gate either way — OnDemand's own tests above are unchanged
    // regression coverage (only the SubmitForReview(clock) signature changed, not the behavior).

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void Publish_LiveOrHybridWithFutureScheduledSessionOnly_Succeeds(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);

        course.Publish(clock);

        Assert.Equal(CourseStatus.Published, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void Publish_LiveOrHybridWithEpisodeMediaOnly_Succeeds(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        course.Publish(clock);

        Assert.Equal(CourseStatus.Published, course.Status);
    }

    [Fact]
    public void Publish_HybridWithBothFutureSessionAndEpisodeMedia_Succeeds()
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(DeliveryFormat.Hybrid);
        course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        course.Publish(clock);

        Assert.Equal(CourseStatus.Published, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void Publish_LiveOrHybridWithOnlyPastSessionAndNoEpisodeMedia_ThrowsInvalidOperationException(DeliveryFormat format)
    {
        // A session that is "in the past" relative to now can only be reached by adding it in the
        // future and then advancing the clock (AddLiveSession itself rejects a past startsAtUtc).
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var start = clock.UtcNow.AddHours(1);
        var end = start.AddHours(1);
        course.AddLiveSession("Kickoff", null, start, end, clock);
        clock.UtcNow = end.AddMinutes(1);

        Assert.Throws<InvalidOperationException>(() => course.Publish(clock));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void Publish_LiveOrHybridWithOnlyCancelledSessionAndNoEpisodeMedia_ThrowsInvalidOperationException(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var session = course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);

        Assert.Throws<InvalidOperationException>(() => course.Publish(clock));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SubmitForReview_LiveOrHybridWithFutureScheduledSessionOnly_Succeeds(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);

        course.SubmitForReview(clock);

        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SubmitForReview_LiveOrHybridWithEpisodeMediaOnly_Succeeds(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        course.SubmitForReview(clock);

        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Fact]
    public void SubmitForReview_HybridWithBothFutureSessionAndEpisodeMedia_Succeeds()
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(DeliveryFormat.Hybrid);
        course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        course.SubmitForReview(clock);

        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SubmitForReview_LiveOrHybridWithOnlyPastSessionAndNoEpisodeMedia_ThrowsInvalidOperationException(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var start = clock.UtcNow.AddHours(1);
        var end = start.AddHours(1);
        course.AddLiveSession("Kickoff", null, start, end, clock);
        clock.UtcNow = end.AddMinutes(1);

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview(clock));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Theory]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SubmitForReview_LiveOrHybridWithOnlyCancelledSessionAndNoEpisodeMedia_ThrowsInvalidOperationException(DeliveryFormat format)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);
        var session = course.AddLiveSession("Kickoff", null, clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), clock);
        course.CancelLiveSession(session.Id, null, clock);

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview(clock));
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public void Publish_OnDemandErrorMessage_MatchesPreP11_01TextExactly()
    {
        // Regression guard (contract §2.7): the OnDemand error message string must stay byte-identical
        // to what it was before this task, since it is part of the observable API/domain contract even
        // though no existing test asserted the literal text before now.
        var course = CreateDraftCourse();

        var exception = Assert.Throws<InvalidOperationException>(() => course.Publish(new FakeClock(DateTime.UtcNow)));

        Assert.Equal("Cannot publish a course with no episode that has media attached.", exception.Message);
    }
}
