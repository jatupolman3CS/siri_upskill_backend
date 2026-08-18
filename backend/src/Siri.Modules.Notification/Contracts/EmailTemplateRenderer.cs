using Siri.Modules.Notification.Infrastructure.Templates;

namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// Contracts-surface wrapper around <see cref="EmailLayout"/> so another module can render its own
/// email content inside the one shared HTML shell (brand header/footer) instead of hand-rolling a
/// second layout, without reaching into this module's <c>Infrastructure/Templates</c> directly —
/// forbidden cross-module by the module-boundary architecture test (same reasoning as
/// <see cref="IEmailOutbox"/>'s own doc comment).
/// </summary>
public static class EmailTemplateRenderer
{
    /// <summary>See <see cref="EmailLayout.Render"/> — <paramref name="contentHtml"/> is inserted
    /// as-is, so callers must ensure it is already safe HTML (untrusted text encoded first).</summary>
    public static string RenderLayout(string title, string contentHtml) => EmailLayout.Render(title, contentHtml);
}
