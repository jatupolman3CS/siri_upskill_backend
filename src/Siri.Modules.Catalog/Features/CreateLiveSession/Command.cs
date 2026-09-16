namespace Siri.Modules.Catalog.Features.CreateLiveSession;

public sealed record CreateLiveSessionCommand(
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);
