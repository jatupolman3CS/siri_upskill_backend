using Siri.Modules.Notification.Infrastructure.Templates;

namespace Siri.UnitTests.Notification;

public class GenericNotificationEmailTemplateTests
{
    [Fact]
    public void Render_ValidInput_ProducesHtmlContainingRecipientNameAndMessage()
    {
        var html = GenericNotificationEmailTemplate.Render("สมชาย ใจดี", "<p>คอร์สของคุณพร้อมแล้ว</p>");

        Assert.Contains("สมชาย ใจดี", html, StringComparison.Ordinal);
        Assert.Contains("<p>คอร์สของคุณพร้อมแล้ว</p>", html, StringComparison.Ordinal);
        Assert.Contains("SIRI UpSkill", html, StringComparison.Ordinal); // proves EmailLayout ran underneath
    }

    [Fact]
    public void Render_RecipientNameWithHtml_EncodesTheName()
    {
        var html = GenericNotificationEmailTemplate.Render("<script>alert(1)</script>", "<p>Body</p>");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MissingRecipientName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GenericNotificationEmailTemplate.Render("", "<p>Body</p>"));
    }
}
