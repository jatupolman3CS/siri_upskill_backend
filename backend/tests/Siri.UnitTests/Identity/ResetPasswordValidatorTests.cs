using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Features.ResetPassword;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Mirrors <see cref="RegisterValidatorTests"/> case-for-case for <see cref="ResetPasswordCommand.NewPassword"/>
/// — <see cref="ResetPasswordValidator"/>'s own doc comment explains why it references
/// <see cref="RegisterValidator"/>'s constants/denylist directly rather than duplicating them; this
/// class is the "verify this holds, rather than assuming it" half of that decision (task instruction).
/// </summary>
public class ResetPasswordValidatorTests
{
    private readonly ResetPasswordValidator _validator = new();

    [Fact]
    public void Validate_StrongPasswordAndToken_Succeeds()
    {
        var command = new ResetPasswordCommand("a1b2c3d4e5f6", "Correct-Horse-Battery-Staple-9");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("short1")] // below MinPasswordLength (10)
    [InlineData("abc123456")] // 9 chars, still below the floor
    public void Validate_PasswordShorterThanMinLength_Fails(string password)
    {
        var command = new ResetPasswordCommand("a1b2c3d4e5f6", password);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordCommand.NewPassword));
    }

    [Fact]
    public void Validate_PasswordAtExactlyMinLength_Succeeds()
    {
        var password = new string('a', RegisterValidator.MinPasswordLength);
        var command = new ResetPasswordCommand("a1b2c3d4e5f6", password);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PasswordLongerThanMaxLength_Fails()
    {
        var password = new string('a', RegisterValidator.MaxPasswordLength + 1);
        var command = new ResetPasswordCommand("a1b2c3d4e5f6", password);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The core "verify, don't assume" check (task instruction): every literal password
    /// <see cref="RegisterValidatorTests"/>'s own denylist test asserts against must be rejected here
    /// too — same literal strings, not merely "the same field referenced twice", so this genuinely
    /// exercises <see cref="ResetPasswordValidator"/>'s independently-compiled rule, not just a shared
    /// reference re-read by the test itself. <see cref="RegisterValidator.DisallowedPasswords"/> is
    /// <c>internal</c> to <c>Siri.Modules.Identity</c> and this test project is a separate assembly
    /// with no <c>InternalsVisibleTo</c>, which is exactly why the literals are duplicated here rather
    /// than enumerated from that field directly.
    /// </summary>
    [Theory]
    [InlineData("password123")]
    [InlineData("PASSWORD123")] // denylist check is case-insensitive
    [InlineData("password1234")]
    [InlineData("1234567890")]
    [InlineData("0123456789")]
    [InlineData("qwertyuiop123")]
    [InlineData("letmein12345")]
    [InlineData("iloveyou1234")]
    [InlineData("welcome12345")]
    [InlineData("administrator")]
    [InlineData("changeme123")]
    [InlineData("trustno1234")]
    [InlineData("dragon123456")]
    [InlineData("monkey123456")]
    [InlineData("football1234")]
    [InlineData("baseball1234")]
    public void Validate_DenylistedPassword_FailsEvenThoughLongEnough(string password)
    {
        Assert.True(password.Length >= RegisterValidator.MinPasswordLength); // sanity: testing the denylist, not the length rule
        var command = new ResetPasswordCommand("a1b2c3d4e5f6", password);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordCommand.NewPassword));
    }

    [Fact]
    public void Validate_MissingToken_Fails()
    {
        var command = new ResetPasswordCommand("", "Correct-Horse-Battery-Staple-9");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordCommand.Token));
    }

    [Fact]
    public void Validate_TokenLongerThanMaxLength_Fails()
    {
        var token = new string('a', 513); // > the 512-char sanity cap
        var command = new ResetPasswordCommand(token, "Correct-Horse-Battery-Staple-9");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordCommand.Token));
    }
}
