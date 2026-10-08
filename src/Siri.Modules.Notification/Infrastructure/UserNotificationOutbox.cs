using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary><see cref="IUserNotificationOutbox"/> implementation — stages a <see cref="USER_NOTIFICATION"/> on the
/// scoped <see cref="AppDbContext"/> without saving, exactly like <see cref="EmailOutbox"/> does for email
/// (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md §3.2). Scoped for the same reason.</summary>
internal sealed class UserNotificationOutbox(AppDbContext dbContext, IClock clock) : IUserNotificationOutbox
{
    /// <summary>Contract §3.2 — tighter than the column's varchar(300) on purpose.</summary>
    internal const int TitleMaxLength = 200;

    internal const int BodyMaxLength = 2000;

    /// <summary>Matches <c>UserNotificationConfiguration</c>'s <c>HasMaxLength(64)</c>.</summary>
    internal const int TypeMaxLength = 64;

    /// <summary>Matches <c>UserNotificationConfiguration</c>'s <c>HasMaxLength(1000)</c>.</summary>
    internal const int LinkUrlMaxLength = 1000;

    public void Stage(Guid userId, string type, string title, string body, string? linkUrl)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A notification needs a recipient user id.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        if (type.Length > TypeMaxLength)
        {
            throw new ArgumentException($"Notification type must be at most {TypeMaxLength} characters.", nameof(type));
        }

        var notification = USER_NOTIFICATION.Create(
            userId,
            type,
            CutToFit(title, TitleMaxLength),
            CutToFit(body, BodyMaxLength),
            NormalizeLink(linkUrl),
            clock);

        dbContext.UserNotifications().Add(notification);
    }

    /// <summary>Cuts to at most <paramref name="maxLength"/> UTF-16 code units without ever leaving half of a
    /// surrogate pair at the end (an emoji in a course name must not turn into an invalid string).</summary>
    internal static string CutToFit(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var end = maxLength;
        if (char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return value[..end];
    }

    /// <summary>Accepts only an app-relative path (one leading <c>/</c>, not <c>//</c> or <c>/\</c>, which
    /// browsers read as a protocol-relative/other-host URL) or an absolute http(s) URL, with no control
    /// characters. Returns <c>null</c> for a null/blank link; throws for anything unsafe or over-long.</summary>
    internal static string? NormalizeLink(string? linkUrl)
    {
        if (string.IsNullOrWhiteSpace(linkUrl))
        {
            return null;
        }

        var link = linkUrl.Trim();

        if (link.Length > LinkUrlMaxLength)
        {
            throw new ArgumentException($"Notification link must be at most {LinkUrlMaxLength} characters.", nameof(linkUrl));
        }

        if (link.Any(char.IsControl))
        {
            throw new ArgumentException("Notification link must not contain control characters.", nameof(linkUrl));
        }

        var isRelativePath = link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) && !link.StartsWith("/\\", StringComparison.Ordinal);
        var isAbsoluteWebUrl = Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        if (!isRelativePath && !isAbsoluteWebUrl)
        {
            throw new ArgumentException("Notification link must be an app-relative path or an absolute http(s) URL.", nameof(linkUrl));
        }

        return link;
    }
}
