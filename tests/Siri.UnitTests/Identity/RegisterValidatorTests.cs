using Siri.Modules.Identity.Features.Register;

namespace Siri.UnitTests.Identity;

public class RegisterValidatorTests
{
    private readonly RegisterValidator _validator = new();

    [Fact]
    public void Validate_StrongPassword_Succeeds()
    {
        var command = new RegisterCommand("student@example.com", "Correct-Horse-Battery-Staple-9", "Student One");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("short1")] // below MinPasswordLength (10)
    [InlineData("abc123456")] // 9 chars, still below the floor
    public void Validate_PasswordShorterThanMinLength_Fails(string password)
    {
        var command = new RegisterCommand("student@example.com", password, "Student One");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterCommand.Password));
    }

    [Fact]
    public void Validate_PasswordAtExactlyMinLength_Succeeds()
    {
        var password = new string('a', RegisterValidator.MinPasswordLength);
        var command = new RegisterCommand("student@example.com", password, "Student One");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("password123")]
    [InlineData("PASSWORD123")] // denylist check is case-insensitive
    [InlineData("1234567890")]
    [InlineData("administrator")]
    public void Validate_DenylistedPassword_FailsEvenThoughLongEnough(string password)
    {
        Assert.True(password.Length >= RegisterValidator.MinPasswordLength); // sanity: this is testing the denylist, not the length rule
        var command = new RegisterCommand("student@example.com", password, "Student One");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterCommand.Password));
    }

    [Fact]
    public void Validate_PasswordLongerThanMaxLength_Fails()
    {
        var password = new string('a', RegisterValidator.MaxPasswordLength + 1);
        var command = new RegisterCommand("student@example.com", password, "Student One");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var command = new RegisterCommand(email, "Correct-Horse-Battery-Staple-9", "Student One");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")] // below MinDisplayNameLength (2)
    public void Validate_DisplayNameTooShort_Fails(string displayName)
    {
        var command = new RegisterCommand("student@example.com", "Correct-Horse-Battery-Staple-9", displayName);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterCommand.DisplayName));
    }

    [Fact]
    public void Validate_DisplayNameLongerThanMaxLength_Fails()
    {
        var displayName = new string('ก', RegisterValidator.MaxDisplayNameLength + 1);
        var command = new RegisterCommand("student@example.com", "Correct-Horse-Battery-Staple-9", displayName);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterCommand.DisplayName));
    }
}
