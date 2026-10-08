namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// The Notification module's public surface for creating an in-app notification (<c>NOTIFY.NOTIFICATIONS</c>)
/// for a user from another module — the in-app twin of <see cref="IEmailOutbox"/> (task P11-04,
/// docs/contracts/P11-04-live-invites-ics-reminders.md §3.2). Before this contract only this module's own
/// announcement job could create a notification.
/// </summary>
public interface IUserNotificationOutbox
{
    /// <summary>
    /// Stages one notification on the ambient, request-scoped <c>AppDbContext</c> — this method does
    /// <b>not</b> call <c>SaveChangesAsync</c> itself (same transaction-boundary rule as
    /// <see cref="IEmailOutbox.Enqueue(string, string, string, string?)"/>), so the notification commits or
    /// rolls back together with whatever else the caller is writing.
    /// <para>
    /// <paramref name="title"/> (at most 200 characters) and <paramref name="body"/> (at most 2000) are cut to
    /// fit rather than rejected — a long course name must not make a reminder fail. <paramref name="type"/> is
    /// a short server-chosen key (at most 64 characters) such as <c>live.invite</c>; an over-long value is a
    /// programmer error and throws, as does a blank title/body/type. <paramref name="linkUrl"/> (optional) must
    /// be either an application-relative path starting with a single <c>/</c> or an absolute
    /// <c>http(s)</c> URL (at most 1000 characters) — anything else throws, because the frontend renders it as a
    /// link and a <c>javascript:</c> URL there would be an XSS vector.
    /// </para>
    /// </summary>
    void Stage(Guid userId, string type, string title, string body, string? linkUrl);
}
