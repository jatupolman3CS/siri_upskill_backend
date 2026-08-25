using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>Request payload for POST /api/media/assets. Deliberately carries only <see cref="Title"/> —
/// <c>Provider</c>/<c>ProviderAssetId</c> on <see cref="MEDIA_ASSET"/> come from calling
/// <c>Siri.Integrations.Video.IVideoProvider.CreateVideoAsync(title, ct)</c> (a later task's job, not this
/// scaffold's), never from the client directly — mirrors that interface method's own single
/// <c>title</c> parameter. Same "the client supplies only what it actually knows, the server resolves the
/// rest" split <c>Siri.Modules.Catalog.Features.CreateCourse.CreateCourseCommand</c>'s own doc comment
/// already establishes for instructor identity.</summary>
public sealed record CreateMediaAssetCommand(string Title);

/// <summary>
/// Provider/pipeline status-callback shape — not bound to any HTTP endpoint in this scaffold pass (see
/// <c>MediaAssetEndpoints</c>'s own doc comment for why: this is a system/provider-driven transition, not a
/// user action), but kept as one coherent parameter type so <see cref="MediaAssetService.UpdateStatusAsync"/>
/// doesn't need five loose parameters, ready for whichever later task wires this to an actual Bunny Stream
/// webhook or polling handler.
/// </summary>
public sealed record UpdateMediaAssetStatusCommand(
    MediaAssetStatus Status,
    string? PlaybackId,
    int? DurationSeconds,
    string? ThumbnailUrl,
    string? ErrorMessage);
