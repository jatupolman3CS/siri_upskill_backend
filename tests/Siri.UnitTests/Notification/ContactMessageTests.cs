using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.SubmitContactMessage;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Notification;

public sealed class ContactMessageTests
{
    [Fact]
    public void ContactMessage_Creation_InitializesPropertiesCorrectly()
    {
        // Arrange & Act
        var id = UuidV7.NewId();
        var message = new CONTACT_MESSAGE(
            id,
            "  John Doe  ",
            "  USER@Example.com ",
            "  Need Help  ",
            "  Detailed question text...  ");

        // Assert
        Assert.Equal(id, message.Id);
        Assert.Equal("John Doe", message.Name);
        Assert.Equal("user@example.com", message.Email);
        Assert.Equal("Need Help", message.Subject);
        Assert.Equal("Detailed question text...", message.Message);
        Assert.Equal(ContactMessageStatus.Pending, message.Status);
        Assert.Null(message.ResolvedAtUtc);
        Assert.Null(message.ResolvedBy);
        Assert.False(message.IsDeleted);
    }

    [Fact]
    public void ContactMessage_MarkResolved_UpdatesStatusAndAuditing()
    {
        // Arrange
        var message = new CONTACT_MESSAGE(
            UuidV7.NewId(),
            "John",
            "john@example.com",
            "Support",
            "Issue details");
        var adminId = Guid.NewGuid();

        // Act
        message.MarkResolved(adminId, "Customer contacted via phone");

        // Assert
        Assert.Equal(ContactMessageStatus.Resolved, message.Status);
        Assert.Equal(adminId, message.ResolvedBy);
        Assert.NotNull(message.ResolvedAtUtc);
        Assert.Equal("Customer contacted via phone", message.AdminNotes);
        Assert.Equal(adminId, message.UpdatedBy);
        Assert.NotNull(message.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("", "test@example.com", "Subject", "Message", false)]
    [InlineData("Name", "", "Subject", "Message", false)]
    [InlineData("Name", "not-an-email", "Subject", "Message", false)]
    [InlineData("Name", "test@example.com", "", "Message", false)]
    [InlineData("Name", "test@example.com", "Subject", "", false)]
    [InlineData("Valid Name", "valid@example.com", "Valid Subject", "Valid message body.", true)]
    public void SubmitContactMessageValidator_ValidatesRulesCorrectly(
        string name,
        string email,
        string subject,
        string message,
        bool expectedValid)
    {
        // Arrange
        var validator = new SubmitContactMessageValidator();
        var command = new SubmitContactMessageCommand(name, email, subject, message, null);

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }
}
