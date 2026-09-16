using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.CreateLiveSession;

public sealed record LiveSessionResponse(
    Guid Id,
    Guid CourseId,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int SortOrder,
    CourseLiveSessionStatus Status,
    string? CancelReason,
    Guid? RecordingEpisodeId);
