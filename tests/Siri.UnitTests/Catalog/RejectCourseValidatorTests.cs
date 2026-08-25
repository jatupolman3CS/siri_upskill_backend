using Siri.Modules.Catalog.Features.RejectCourse;

namespace Siri.UnitTests.Catalog;

public class RejectCourseValidatorTests
{
    private readonly RejectCourseValidator _validator = new();

    [Fact]
    public void Validate_ValidReason_Succeeds()
    {
        var result = _validator.Validate(new RejectCourseCommand("คำอธิบายไม่ครบถ้วน"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        var result = _validator.Validate(new RejectCourseCommand(""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectCourseCommand.Reason));
    }

    [Fact]
    public void Validate_ReasonLongerThanMaxLength_Fails()
    {
        var command = new RejectCourseCommand(new string('a', RejectCourseValidator.MaxReasonLength + 1));

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectCourseCommand.Reason));
    }
}
