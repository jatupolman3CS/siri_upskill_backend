using Siri.Modules.Identity.Features.ConfirmEmail;

namespace Siri.UnitTests.Identity;

public class ConfirmEmailValidatorTests
{
    private readonly ConfirmEmailValidator _validator = new();

    [Fact]
    public void Validate_NonEmptyToken_Succeeds()
    {
        var result = _validator.Validate(new ConfirmEmailCommand("a1b2c3d4e5f6"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyToken_Fails()
    {
        var result = _validator.Validate(new ConfirmEmailCommand(""));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ExcessivelyLongToken_Fails()
    {
        var token = new string('a', 513);

        var result = _validator.Validate(new ConfirmEmailCommand(token));

        Assert.False(result.IsValid);
    }
}
