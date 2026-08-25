using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.UpdateCourse;

namespace Siri.UnitTests.Catalog;

public class UpdateCourseValidatorTests
{
    private readonly UpdateCourseValidator _validator = new();

    private static UpdateCourseCommand ValidCommand() =>
        new("Web Development", "Learn to build websites", "Full description", Guid.NewGuid(),
            CourseLevel.Beginner, CourseLanguage.Thai, null, 990m, 1490m, 365, "SEO title", "SEO description");

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllNullableFieldsNull_Succeeds()
    {
        var command = ValidCommand() with
        {
            Subtitle = null,
            Description = null,
            ThumbnailUrl = null,
            ComparePrice = null,
            AccessDurationDays = null,
            SeoTitle = null,
            SeoDescription = null,
        };

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var command = ValidCommand() with { Title = "" };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.Title));
    }

    [Fact]
    public void Validate_DescriptionLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { Description = new string('a', UpdateCourseValidator.MaxDescriptionLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.Description));
    }

    [Fact]
    public void Validate_NegativePrice_Fails()
    {
        var command = ValidCommand() with { Price = -1m };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.Price));
    }

    [Fact]
    public void Validate_NegativeComparePrice_Fails()
    {
        var command = ValidCommand() with { ComparePrice = -1m };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.ComparePrice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveAccessDurationDays_Fails(int accessDurationDays)
    {
        var command = ValidCommand() with { AccessDurationDays = accessDurationDays };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.AccessDurationDays));
    }

    [Fact]
    public void Validate_SeoTitleLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { SeoTitle = new string('a', UpdateCourseValidator.MaxSeoTitleLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCourseCommand.SeoTitle));
    }
}
