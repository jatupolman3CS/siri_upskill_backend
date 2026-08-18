using Siri.Modules.Identity.Features.ForgotPassword;

namespace Siri.UnitTests.Identity;

public class ForgotPasswordValidatorTests
{
    private readonly ForgotPasswordValidator _validator = new();

    [Fact]
    public void Validate_ValidEmail_Succeeds()
    {
        var command = new ForgotPasswordCommand("student@example.com");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var command = new ForgotPasswordCommand(email);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ForgotPasswordCommand.Email));
    }

    [Fact]
    public void Validate_EmailLongerThanMaxLength_Fails()
    {
        var email = new string('a', 250) + "@example.com"; // > 256 chars total
        var command = new ForgotPasswordCommand(email);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }
}
