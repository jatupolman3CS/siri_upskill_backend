using Siri.Modules.Catalog.Features.CreateCategory;

namespace Siri.UnitTests.Catalog;

public class CreateCategoryValidatorTests
{
    private readonly CreateCategoryValidator _validator = new();

    private static CreateCategoryCommand ValidCommand(string slug = "web-development") =>
        new(slug, "พัฒนาเว็บ", "Web Development", "icon-web", null);

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("web-development")]
    [InlineData("web")]
    [InlineData("web-development-101")]
    [InlineData("a")]
    public void Validate_WellFormedSlug_Succeeds(string slug)
    {
        var result = _validator.Validate(ValidCommand(slug));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Web-Development")] // uppercase not allowed
    [InlineData("web development")] // spaces not allowed
    [InlineData("-web-development")] // leading hyphen
    [InlineData("web-development-")] // trailing hyphen
    [InlineData("web--development")] // doubled hyphen
    [InlineData("web_development")] // underscore not allowed
    [InlineData("เว็บ")] // Thai characters not allowed — no auto Thai→Latin generation for categories
    public void Validate_MalformedSlug_Fails(string slug)
    {
        var result = _validator.Validate(ValidCommand(slug));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryCommand.Slug));
    }

    [Fact]
    public void Validate_SlugLongerThanMaxLength_Fails()
    {
        var slug = new string('a', CreateCategoryValidator.MaxSlugLength + 1); // all lowercase letters — pattern-valid, just too long
        var result = _validator.Validate(ValidCommand(slug));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryCommand.Slug));
    }

    [Theory]
    [InlineData("", "Web Development")]
    [InlineData("พัฒนาเว็บ", "")]
    public void Validate_MissingName_Fails(string nameTh, string nameEn)
    {
        var command = new CreateCategoryCommand("web-development", nameTh, nameEn, null, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NameLongerThanMaxLength_Fails()
    {
        var command = new CreateCategoryCommand(
            "web-development", new string('ก', CreateCategoryValidator.MaxNameLength + 1), "Web Development", null, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryCommand.NameTh));
    }

    [Fact]
    public void Validate_IconKeyLongerThanMaxLength_Fails()
    {
        var command = new CreateCategoryCommand(
            "web-development", "พัฒนาเว็บ", "Web Development", new string('a', CreateCategoryValidator.MaxIconKeyLength + 1), null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCategoryCommand.IconKey));
    }

    [Fact]
    public void Validate_NullIconKey_Succeeds()
    {
        var command = new CreateCategoryCommand("web-development", "พัฒนาเว็บ", "Web Development", null, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
