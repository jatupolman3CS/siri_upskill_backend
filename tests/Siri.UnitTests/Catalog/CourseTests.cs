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
        course.SubmitForReview();
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

        course.SubmitForReview();

        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Fact]
    public void SubmitForReview_NoEpisodeWithMedia_ThrowsInvalidOperationExceptionAndLeavesStatusUnchanged()
    {
        var course = CreateDraftCourse();
        course.AddSection("Section 1"); // no episodes at all

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview());
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public void SubmitForReview_FromInReview_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview();

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview());
    }

    [Fact]
    public void SubmitForReview_FromPublished_ThrowsInvalidOperationException()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => course.SubmitForReview());
    }

    [Fact]
    public void SubmitForReview_FromRejected_ClearsRejectionReasonAndTransitionsToInReview()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview();
        course.Reject("ต้องแก้คำอธิบาย");

        course.SubmitForReview();

        Assert.Equal(CourseStatus.InReview, course.Status);
        Assert.Null(course.RejectionReason);
    }

    [Fact]
    public void Reject_FromInReview_SetsRejectedStatusAndReason()
    {
        var course = CreateDraftCourse();
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview();

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
        course.SubmitForReview();

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
}
