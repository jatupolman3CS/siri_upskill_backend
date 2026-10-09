using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Infrastructure.Delivery;

namespace Siri.UnitTests.Notification;

public class NotificationMessagesTests
{
    [Fact]
    public void EmailDeliveryMessage_ToJsonThenParse_RoundTrips()
    {
        var message = new EmailDeliveryMessage(Guid.NewGuid(), "identity-password-reset", 2, new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));

        var parsed = EmailDeliveryMessage.Parse(message.ToJson());

        Assert.Equal(message, parsed);
    }

    [Fact]
    public void EmailDeliveryMessage_ToJson_CarriesNoBodySubjectOrAddress()
    {
        var json = new EmailDeliveryMessage(Guid.NewGuid(), "identity-password-reset", 1, DateTime.UtcNow).ToJson();

        // The claim-check payload names the row and nothing else: nothing here may hold a reset link or a personal address.
        Assert.DoesNotContain("body", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"messageId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"messageId\":\"nope\"}")]
    public void EmailDeliveryMessage_Parse_UnusableValue_IsPoison(string value)
    {
        Assert.Throws<PoisonMessageException>(() => EmailDeliveryMessage.Parse(value));
    }

    [Fact]
    public void EmailDeliveryMessage_KeyFor_IsTheMessageIdSoEveryPublishOfOneMessageSharesAPartition()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString("N"), EmailDeliveryMessage.KeyFor(id));
        Assert.Equal(EmailDeliveryMessage.KeyFor(id), EmailDeliveryMessage.KeyFor(id));
    }

    [Fact]
    public void EmailDeliveryMessage_KeyFor_DifferentMessagesGetDifferentKeys()
    {
        Assert.NotEqual(EmailDeliveryMessage.KeyFor(Guid.NewGuid()), EmailDeliveryMessage.KeyFor(Guid.NewGuid()));
    }

    [Fact]
    public void EmailDeliveryMessage_KeyFor_HasNothingToDoWithAnyone_sAddress()
    {
        // There is no overload taking an address: the key is built from the message id alone, so there is nothing about the recipient
        // (not even a guessable hash of it) for a reader of the topic to confirm.
        var overloads = typeof(EmailDeliveryMessage).GetMethods().Where(m => m.Name == nameof(EmailDeliveryMessage.KeyFor)).ToList();

        Assert.Single(overloads);
        Assert.Equal(typeof(Guid), Assert.Single(overloads[0].GetParameters()).ParameterType);
    }

    [Fact]
    public void InAppNotificationEvent_ToJsonThenParse_RoundTrips()
    {
        var notificationEvent = new InAppNotificationEvent(Guid.NewGuid(), Guid.NewGuid(), "live.reminder", new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(notificationEvent, InAppNotificationEvent.Parse(notificationEvent.ToJson()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("{\"notificationId\":\"00000000-0000-0000-0000-000000000000\",\"userId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void InAppNotificationEvent_Parse_UnusableValue_IsPoison(string value)
    {
        Assert.Throws<PoisonMessageException>(() => InAppNotificationEvent.Parse(value));
    }

    [Fact]
    public void InAppNotificationEvent_Parse_MissingUserId_IsPoison()
    {
        var json = $"{{\"notificationId\":\"{Guid.NewGuid()}\",\"type\":\"x\"}}";

        Assert.Throws<PoisonMessageException>(() => InAppNotificationEvent.Parse(json));
    }

    [Fact]
    public void InAppNotificationEvent_KeyFor_IsTheUserIdWithoutDashes()
    {
        var userId = Guid.NewGuid();

        Assert.Equal(userId.ToString("N"), InAppNotificationEvent.KeyFor(userId));
    }
}
