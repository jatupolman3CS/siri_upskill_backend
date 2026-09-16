using FluentValidation.TestHelper;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CancelLiveSession;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;
using Siri.Modules.Catalog.Features.UpdateLiveSession;

namespace Siri.UnitTests.Catalog;

public class LiveSessionValidatorTests
{
    private readonly CreateLiveSessionCommandValidator _createValidator = new();
    private readonly UpdateLiveSessionCommandValidator _updateValidator = new();
    private readonly CancelLiveSessionCommandValidator _cancelValidator = new();
    private readonly SetCourseDeliveryFormatCommandValidator _deliveryFormatValidator = new();

    private static readonly DateTime BaseStart = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BaseEnd = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);

    // ---- CreateLiveSessionValidator -----------------------------------------------------------

    [Fact]
    public void CreateLiveSessionValidator_ValidCommand_PassesValidation()
    {
        var command = new CreateLiveSessionCommand("Live Session 1", "Session description", BaseStart, BaseEnd);
        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateLiveSessionValidator_EmptyTitle_HasValidationError(string title)
    {
        var command = new CreateLiveSessionCommand(title, null, BaseStart, BaseEnd);
        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateLiveSessionValidator_TitleTooLong_HasValidationError()
    {
        var command = new CreateLiveSessionCommand(new string('a', 201), null, BaseStart, BaseEnd);
        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateLiveSessionValidator_DescriptionTooLong_HasValidationError()
    {
        var command = new CreateLiveSessionCommand("Title", new string('d', 2001), BaseStart, BaseEnd);
        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void CreateLiveSessionValidator_EndsAtBeforeStartsAt_HasValidationError()
    {
        var command = new CreateLiveSessionCommand("Title", null, BaseEnd, BaseStart);
        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.EndsAtUtc);
    }

    [Fact]
    public void CreateLiveSessionValidator_EndsAtEqualToStartsAt_HasValidationError()
    {
        var command = new CreateLiveSessionCommand("Title", null, BaseStart, BaseStart);
        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.EndsAtUtc);
    }

    // ---- UpdateLiveSessionValidator -----------------------------------------------------------

    [Fact]
    public void UpdateLiveSessionValidator_ValidCommand_PassesValidation()
    {
        var command = new UpdateLiveSessionCommand("Updated Session", "Updated description", BaseStart, BaseEnd);
        var result = _updateValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateLiveSessionValidator_EmptyTitle_HasValidationError(string title)
    {
        var command = new UpdateLiveSessionCommand(title, null, BaseStart, BaseEnd);
        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void UpdateLiveSessionValidator_TitleTooLong_HasValidationError()
    {
        var command = new UpdateLiveSessionCommand(new string('a', 201), null, BaseStart, BaseEnd);
        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void UpdateLiveSessionValidator_DescriptionTooLong_HasValidationError()
    {
        var command = new UpdateLiveSessionCommand("Title", new string('d', 2001), BaseStart, BaseEnd);
        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void UpdateLiveSessionValidator_EndsAtBeforeStartsAt_HasValidationError()
    {
        var command = new UpdateLiveSessionCommand("Title", null, BaseEnd, BaseStart);
        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.EndsAtUtc);
    }

    // ---- CancelLiveSessionValidator -----------------------------------------------------------

    [Fact]
    public void CancelLiveSessionValidator_ValidReason_PassesValidation()
    {
        var command = new CancelLiveSessionCommand("ผู้สอนติดภารกิจเร่งด่วน");
        var result = _cancelValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CancelLiveSessionValidator_EmptyReason_HasValidationError(string reason)
    {
        var command = new CancelLiveSessionCommand(reason);
        var result = _cancelValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void CancelLiveSessionValidator_ReasonTooLong_HasValidationError()
    {
        var command = new CancelLiveSessionCommand(new string('r', 501));
        var result = _cancelValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    // ---- SetCourseDeliveryFormatValidator -----------------------------------------------------

    [Theory]
    [InlineData(DeliveryFormat.OnDemand)]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SetCourseDeliveryFormatValidator_ValidFormat_PassesValidation(DeliveryFormat format)
    {
        var command = new SetCourseDeliveryFormatCommand(format);
        var result = _deliveryFormatValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SetCourseDeliveryFormatValidator_InvalidFormat_HasValidationError()
    {
        var command = new SetCourseDeliveryFormatCommand((DeliveryFormat)99);
        var result = _deliveryFormatValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DeliveryFormat);
    }
}
