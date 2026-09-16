namespace Siri.Modules.Catalog.Features.UpdateLiveSession;

public sealed record UpdateLiveSessionCommand(
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);
