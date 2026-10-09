using Siri.Modules.Notification.Infrastructure.Delivery;

namespace Siri.UnitTests.Notification;

public class NotificationTopicsTests
{
    [Fact]
    public void For_DerivesEveryNameFromThePrefix()
    {
        var topics = NotificationTopics.For("siriupskill-dev");

        Assert.Equal("siriupskill-dev.notification.email.v1", topics.Email);
        Assert.Equal("siriupskill-dev.notification.email.dlq.v1", topics.EmailDeadLetter);
        Assert.Equal("siriupskill-dev.notification.inapp.v1", topics.InApp);
        Assert.Equal("siriupskill-dev.notification.inapp.dlq.v1", topics.InAppDeadLetter);
        Assert.Equal("siriupskill-dev.notification.email", topics.EmailGroup);
        Assert.Equal("siriupskill-dev.notification.inapp", topics.InAppGroup);
    }

    [Fact]
    public void For_EveryTopicAndGroupNameIsDistinct()
    {
        var topics = NotificationTopics.For("p");

        var all = new[] { topics.Email, topics.EmailDeadLetter, topics.InApp, topics.InAppDeadLetter, topics.EmailGroup, topics.InAppGroup };

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void For_TwoEnvironmentsOnOneCluster_NeverShareATopicOrAGroup()
    {
        var dev = NotificationTopics.For("siriupskill-dev");
        var prod = NotificationTopics.For("siriupskill");

        Assert.Empty(Names(dev).Intersect(Names(prod)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void For_BlankPrefix_Throws(string prefix)
    {
        Assert.Throws<ArgumentException>(() => NotificationTopics.For(prefix));
    }

    private static string[] Names(NotificationTopics t) =>
        [t.Email, t.EmailDeadLetter, t.InApp, t.InAppDeadLetter, t.EmailGroup, t.InAppGroup];
}
