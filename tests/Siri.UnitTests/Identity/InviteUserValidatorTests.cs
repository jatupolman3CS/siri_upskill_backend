using FluentValidation.TestHelper;
using Siri.Modules.Identity.Features.Admin.InviteUser;
using Xunit;

namespace Siri.UnitTests.Identity;

public sealed class InviteUserValidatorTests
{
    private readonly InviteUserValidator _validator = new();

    [Theory]
    [InlineData("valid@example.com", "John Doe", "Learner")]
    [InlineData("instructor@test.com", "Ajarn Test", "Instructor")]
    [InlineData("admin@siri.com", "Admin USER", "Admin")]
    [InlineData("super@siri.com", "Super Admin", "SuperAdmin")]
    public void Validate_ValidCommand_Passes(string email, string displayName, string role)
    {
        var command = new InviteUserCommand(email, displayName, role);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-email")]
    [InlineData("@nodomain.com")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var command = new InviteUserCommand(email, "Test Name", "Learner");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_EmptyDisplayName_Fails(string displayName)
    {
        var command = new InviteUserCommand("valid@example.com", displayName, "Learner");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Theory]
    [InlineData("Hacker")]
    [InlineData("Root")]
    [InlineData("")]
    public void Validate_InvalidRole_Fails(string role)
    {
        var command = new InviteUserCommand("valid@example.com", "Test Name", role);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ROLE);
    }
}
