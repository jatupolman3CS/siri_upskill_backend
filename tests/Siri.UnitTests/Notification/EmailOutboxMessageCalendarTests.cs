using Siri.Modules.Notification.Domain;

namespace Siri.UnitTests.Notification;

/// <summary>Unit tests for the iCalendar part of <see cref="EMAIL_OUTBOX_MESSAGE"/> (task P11-04,
/// docs/contracts/P11-04-live-invites-ics-reminders.md §2.1).</summary>
public class EmailOutboxMessageCalendarTests
{
    private const string ValidIcs = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";

    [Theory]
    [InlineData("REQUEST")]
    [InlineData("CANCEL")]
    [InlineData("PUBLISH")]
    public void Enqueue_WithValidCalendarPart_StoresMethodAndContent(string method)
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", "live-invite-batch", method, ValidIcs);

        Assert.Equal(method, message.CalendarMethod);
        Assert.Equal(ValidIcs, message.CalendarIcs);
        Assert.Equal(EmailOutboxStatus.Pending, message.Status);
    }

    [Fact]
    public void Enqueue_FourArgumentOverload_HasNoCalendarPart()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", "welcome");

        Assert.Null(message.CalendarMethod);
        Assert.Null(message.CalendarIcs);
    }

    [Fact]
    public void Enqueue_BothCalendarArgumentsNull_IsAnOrdinaryEmail()
    {
        var message = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, null, null);

        Assert.Null(message.CalendarMethod);
        Assert.Null(message.CalendarIcs);
    }

    [Fact]
    public void Enqueue_MethodWithoutContent_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, "REQUEST", null));
    }

    [Fact]
    public void Enqueue_ContentWithoutMethod_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, null, ValidIcs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("request")]
    [InlineData("REPLY")]
    [InlineData("REQUEST ")]
    public void Enqueue_UnknownMethod_Throws(string method)
    {
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, method, ValidIcs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VERSION:2.0\r\nBEGIN:VCALENDAR")]
    [InlineData(" BEGIN:VCALENDAR\r\nEND:VCALENDAR")]
    public void Enqueue_ContentThatIsEmptyOrDoesNotStartWithVCalendar_Throws(string ics)
    {
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, "REQUEST", ics));
    }

    [Fact]
    public void Enqueue_ContentAtTheLimit_IsAcceptedAndOneOverIsRejected()
    {
        var atLimit = "BEGIN:VCALENDAR" + new string('x', EMAIL_OUTBOX_MESSAGE.CalendarIcsMaxLength - "BEGIN:VCALENDAR".Length);
        var overLimit = atLimit + "x";

        var accepted = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, "PUBLISH", atLimit);

        Assert.Equal(EMAIL_OUTBOX_MESSAGE.CalendarIcsMaxLength, accepted.CalendarIcs!.Length);
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, "PUBLISH", overLimit));
    }

    [Fact]
    public void Enqueue_InvalidCalendarPart_StillValidatesTheOrdinaryFieldsFirst()
    {
        Assert.Throws<ArgumentException>(() =>
            EMAIL_OUTBOX_MESSAGE.Enqueue("", "Subject", "<p>Body</p>", null, "REQUEST", ValidIcs));
    }
}
