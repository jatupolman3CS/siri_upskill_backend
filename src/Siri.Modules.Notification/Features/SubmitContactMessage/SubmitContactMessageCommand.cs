namespace Siri.Modules.Notification.Features.SubmitContactMessage;

public sealed record SubmitContactMessageCommand(
    string Name,
    string Email,
    string Subject,
    string Message,
    string? BotField);
