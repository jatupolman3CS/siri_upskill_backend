using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Features.GetContactMessages;

public sealed record ContactMessageListItemResponse(
    Guid Id,
    string Name,
    string Email,
    string Subject,
    string Message,
    ContactMessageStatus Status,
    DateTime? ResolvedAtUtc,
    Guid? ResolvedBy,
    string? AdminNotes,
    DateTime CreatedAtUtc);
