using Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

namespace Siri.UnitTests.Catalog;

public class SetCourseEnrollmentPolicyCommandValidatorTests
{
    private readonly SetCourseEnrollmentPolicyCommandValidator _validator = new();

    [Fact]
    public void Validate_NullDeadlineAndNullMaxSeats_Succeeds()
    {
        var result = _validator.Validate(new SetCourseEnrollmentPolicyCommand(null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ValidDeadlineAndPositiveMaxSeats_Succeeds()
    {
        var command = new SetCourseEnrollmentPolicyCommand(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), 50);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ZeroOrNegativeMaxSeats_Fails(int maxSeats)
    {
        var command = new SetCourseEnrollmentPolicyCommand(null, maxSeats);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetCourseEnrollmentPolicyCommand.MaxSeats));
    }
}
