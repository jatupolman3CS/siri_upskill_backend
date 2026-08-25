using Siri.Modules.Catalog.Features.ApplyAsInstructor;

namespace Siri.UnitTests.Catalog;

public class ApplyAsInstructorValidatorTests
{
    private readonly ApplyAsInstructorValidator _validator = new();

    private static ApplyAsInstructorCommand ValidCommand() =>
        new("Somchai Dev", "Senior Full-Stack Developer", "สอนพัฒนาเว็บมา 10 ปี");

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullHeadline_Succeeds()
    {
        var command = new ApplyAsInstructorCommand("Somchai Dev", null, "สอนพัฒนาเว็บมา 10 ปี");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyDisplayName_Fails()
    {
        var command = ValidCommand() with { DisplayName = "" };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyAsInstructorCommand.DisplayName));
    }

    [Fact]
    public void Validate_DisplayNameLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { DisplayName = new string('a', ApplyAsInstructorValidator.MaxDisplayNameLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyAsInstructorCommand.DisplayName));
    }

    [Fact]
    public void Validate_HeadlineLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { Headline = new string('a', ApplyAsInstructorValidator.MaxHeadlineLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyAsInstructorCommand.Headline));
    }

    [Fact]
    public void Validate_EmptyBio_Fails()
    {
        var command = ValidCommand() with { Bio = "" };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyAsInstructorCommand.Bio));
    }

    [Fact]
    public void Validate_BioLongerThanMaxLength_Fails()
    {
        var command = ValidCommand() with { Bio = new string('ก', ApplyAsInstructorValidator.MaxBioLength + 1) };

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyAsInstructorCommand.Bio));
    }
}
