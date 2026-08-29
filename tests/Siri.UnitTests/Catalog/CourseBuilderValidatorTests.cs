using FluentValidation.TestHelper;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AutosaveCourse;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Features.ReorderCourseEpisodes;
using Siri.Modules.Catalog.Features.ReorderCourseSections;
using Siri.Modules.Catalog.Features.UpdateCourseEpisode;
using Siri.Modules.Catalog.Features.UpdateCourseSection;

namespace Siri.UnitTests.Catalog;

public class CourseBuilderValidatorTests
{
    private readonly CreateCourseSectionValidator _createSectionValidator = new();
    private readonly UpdateCourseSectionValidator _updateSectionValidator = new();
    private readonly ReorderCourseSectionsValidator _reorderSectionsValidator = new();
    private readonly CreateCourseEpisodeValidator _createEpisodeValidator = new();
    private readonly UpdateCourseEpisodeValidator _updateEpisodeValidator = new();
    private readonly ReorderCourseEpisodesValidator _reorderEpisodesValidator = new();
    private readonly AutosaveCourseValidator _autosaveValidator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCourseSectionValidator_EmptyTitle_HasValidationError(string title)
    {
        var result = _createSectionValidator.TestValidate(new CreateCourseSectionCommand(title));
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateCourseSectionValidator_ValidTitle_PassesValidation()
    {
        var result = _createSectionValidator.TestValidate(new CreateCourseSectionCommand("Section 1"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateCourseSectionValidator_EmptyTitle_HasValidationError(string title)
    {
        var result = _updateSectionValidator.TestValidate(new UpdateCourseSectionCommand(title));
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void ReorderCourseSectionsValidator_DuplicateSectionIds_HasValidationError()
    {
        var dupId = Guid.NewGuid();
        var command = new ReorderCourseSectionsCommand([
            new ReorderCourseSectionItem(dupId, 0),
            new ReorderCourseSectionItem(dupId, 1),
        ]);

        var result = _reorderSectionsValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void ReorderCourseSectionsValidator_ValidItems_PassesValidation()
    {
        var command = new ReorderCourseSectionsCommand([
            new ReorderCourseSectionItem(Guid.NewGuid(), 0),
            new ReorderCourseSectionItem(Guid.NewGuid(), 1),
        ]);

        var result = _reorderSectionsValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCourseEpisodeValidator_EmptyTitle_HasValidationError(string title)
    {
        var result = _createEpisodeValidator.TestValidate(new CreateCourseEpisodeCommand(title, null, false));
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateCourseEpisodeValidator_TooLongDescription_HasValidationError()
    {
        var result = _createEpisodeValidator.TestValidate(new CreateCourseEpisodeCommand("Ep 1", new string('a', 2001), false));
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void ReorderCourseEpisodesValidator_DuplicateEpisodeIds_HasValidationError()
    {
        var dupId = Guid.NewGuid();
        var command = new ReorderCourseEpisodesCommand([
            new ReorderCourseEpisodeItem(dupId, 0),
            new ReorderCourseEpisodeItem(dupId, 1),
        ]);

        var result = _reorderEpisodesValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void AutosaveCourseValidator_MissingRowVersion_HasValidationError()
    {
        var command = new AutosaveCourseCommand(
            "COURSE Title", null, null, Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai,
            null, 990m, null, null, null, null, null, null, null, []);

        var result = _autosaveValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.RowVersion);
    }

    [Fact]
    public void AutosaveCourseValidator_ExceedingMaxSections_HasValidationError()
    {
        var sections = Enumerable.Range(0, 101)
            .Select(i => new AutosaveSectionItem(Guid.NewGuid(), $"Section {i}", i, []))
            .ToList();

        var command = new AutosaveCourseCommand(
            "COURSE Title", null, null, Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai,
            null, 990m, null, null, null, null, null, null, sections, [0x01]);

        var result = _autosaveValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sections);
    }

    [Fact]
    public void AutosaveCourseValidator_ExceedingMaxEpisodes_HasValidationError()
    {
        var episodes = Enumerable.Range(0, 201)
            .Select(i => new AutosaveEpisodeItem(Guid.NewGuid(), $"Episode {i}", null, i, false))
            .ToList();

        var sections = new List<AutosaveSectionItem>
        {
            new(Guid.NewGuid(), "Section 1", 0, episodes),
        };

        var command = new AutosaveCourseCommand(
            "COURSE Title", null, null, Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai,
            null, 990m, null, null, null, null, null, null, sections, [0x01]);

        var result = _autosaveValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Sections[0].Episodes");
    }

    [Fact]
    public void AutosaveCourseValidator_ValidCommand_PassesValidation()
    {
        var command = new AutosaveCourseCommand(
            "COURSE Title",
            "Subtitle",
            "Description",
            Guid.NewGuid(),
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            "https://example.com/thumb.jpg",
            990m,
            1990m,
            365,
            "SEO Title",
            "SEO Description",
            ["Outcome 1"],
            ["Requirement 1"],
            [
                new AutosaveSectionItem(
                    Guid.NewGuid(),
                    "Section 1",
                    0,
                    [
                        new AutosaveEpisodeItem(Guid.NewGuid(), "Episode 1", "Desc", 0, true),
                    ]),
            ],
            [0x00, 0x01, 0x02, 0x03]);

        var result = _autosaveValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
