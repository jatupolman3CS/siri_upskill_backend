using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateCourse;

namespace Siri.UnitTests.Catalog;

public class CreateCourseValidatorTests
{
    private readonly CreateCourseValidator _validator = new();

    private static CreateCourseCommand ValidCommand() =>
        new("Web Development", Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var command = ValidCommand() with { Title = "" };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.Title));
    }

    [Fact]
    public void Validate_TitleLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { Title = new string('a', CreateCourseValidator.MaxTitleLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.Title));
    }

    [Fact]
    public void Validate_EmptyCategoryId_Fails()
    {
        var command = ValidCommand() with { CategoryId = Guid.Empty };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.CategoryId));
    }

    [Fact]
    public void Validate_NegativePrice_Fails()
    {
        var command = ValidCommand() with { Price = -1m };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.Price));
    }

    [Fact]
    public void Validate_ZeroPrice_Succeeds()
    {
        var command = ValidCommand() with { Price = 0m };

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UndefinedLevelEnumValue_Fails()
    {
        var command = ValidCommand() with { Level = (CourseLevel)999 };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.Level));
    }

    [Fact]
    public void Validate_UndefinedLanguageEnumValue_Fails()
    {
        var command = ValidCommand() with { Language = (CourseLanguage)999 };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourseCommand.Language));
    }
}
