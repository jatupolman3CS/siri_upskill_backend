using Siri.Modules.Identity.Features.Login;

namespace Siri.UnitTests.Identity;

public class LoginValidatorTests
{
    private readonly LoginValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Succeeds()
    {
        var command = new LoginCommand("student@example.com", "whatever-they-typed", "device-abc", "Chrome on Windows");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MissingDeviceFields_StillSucceeds()
    {
        // DeviceId/DeviceName are optional — a caller that doesn't send them must not be rejected.
        var command = new LoginCommand("student@example.com", "whatever-they-typed", null, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var command = new LoginCommand(email, "whatever-they-typed", null, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_Fails()
    {
        var command = new LoginCommand("student@example.com", "", null, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public void Validate_ShortPassword_StillSucceeds()
    {
        // Deliberately NOT enforcing Register's MinPasswordLength on login — an existing account's
        // password predates any policy change and must still be able to log in; the hash comparison
        // itself is what actually decides right/wrong, not this validator (see class doc comment).
        var command = new LoginCommand("student@example.com", "short", null, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PasswordLongerThanMaxLength_Fails()
    {
        var password = new string('a', RegisterValidatorMaxPasswordLengthPlusOne());
        var command = new LoginCommand("student@example.com", password, null, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public void Validate_DeviceIdLongerThan200Chars_Fails()
    {
        var command = new LoginCommand("student@example.com", "whatever-they-typed", new string('d', 201), null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.DeviceId));
    }

    [Fact]
    public void Validate_DeviceNameLongerThan200Chars_Fails()
    {
        var command = new LoginCommand("student@example.com", "whatever-they-typed", null, new string('n', 201));

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.DeviceName));
    }

    private static int RegisterValidatorMaxPasswordLengthPlusOne() =>
        Siri.Modules.Identity.Features.Register.RegisterValidator.MaxPasswordLength + 1;
}
