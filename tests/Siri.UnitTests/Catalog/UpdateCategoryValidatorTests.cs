using Siri.Modules.Catalog.Features.CreateCategory;
using Siri.Modules.Catalog.Features.UpdateCategory;

namespace Siri.UnitTests.Catalog;

public class UpdateCategoryValidatorTests
{
    private readonly UpdateCategoryValidator _validator = new();

    private static UpdateCategoryCommand ValidCommand(string slug = "web-development") =>
        new(slug, "พัฒนาเว็บ", "Web Development", "icon-web", null, true);

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Web-Development")]
    [InlineData("web development")]
    public void Validate_MalformedSlug_Fails(string slug)
    {
        var result = _validator.Validate(ValidCommand(slug));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCategoryCommand.Slug));
    }

    [Fact]
    public void Validate_SlugLongerThanMaxLength_Fails()
    {
        var slug = new string('a', CreateCategoryValidator.MaxSlugLength + 1);

        var result = _validator.Validate(ValidCommand(slug));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCategoryCommand.Slug));
    }

    [Theory]
    [InlineData("", "Web Development")]
    [InlineData("พัฒนาเว็บ", "")]
    public void Validate_MissingName_Fails(string nameTh, string nameEn)
    {
        var command = new UpdateCategoryCommand("web-development", nameTh, nameEn, null, null, true);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_IconKeyLongerThanMaxLength_Fails()
    {
        var command = new UpdateCategoryCommand(
            "web-development", "พัฒนาเว็บ", "Web Development", new string('a', CreateCategoryValidator.MaxIconKeyLength + 1), null, true);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCategoryCommand.IconKey));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_EitherIsActiveValue_Succeeds(bool isActive)
    {
        var command = new UpdateCategoryCommand("web-development", "พัฒนาเว็บ", "Web Development", null, null, isActive);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
