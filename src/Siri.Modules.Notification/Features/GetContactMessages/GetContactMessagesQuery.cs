using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Features.GetContactMessages;

public sealed record GetContactMessagesQuery(
    int Page = 1,
    int PageSize = 20,
    ContactMessageStatus? Status = null);
