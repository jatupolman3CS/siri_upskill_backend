using FluentValidation;

namespace Siri.Modules.Learning.Application;

/// <summary>Request payload for PUT /api/learning/enrollments/{enrollmentId}/episode-progress/{episodeId}
/// — a heartbeat-style upsert (see <c>EpisodeProgressService</c>'s own doc comment). Records/DTOs in this
/// module are NOT uppercased — see <see cref="IEnrollmentRepository"/>'s own doc comment for the
/// naming-exception reasoning.</summary>
public sealed record UpsertEpisodeProgressCommand(int LastPositionSeconds, int WatchedSeconds, bool IsCompleted);

public sealed record EpisodeProgressResponse(
    Guid Id,
    Guid EnrollmentId,
    Guid EpisodeId,
    int LastPositionSeconds,
    int WatchedSeconds,
    bool IsCompleted,
    DateTime? CompletedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Format-only checks — no DB access (see <c>CreateEnrollmentValidator</c>'s own doc comment for
/// this module's format-vs-business-rule split).</summary>
public sealed class UpsertEpisodeProgressValidator : AbstractValidator<UpsertEpisodeProgressCommand>
{
    public UpsertEpisodeProgressValidator()
    {
        RuleFor(c => c.LastPositionSeconds).GreaterThanOrEqualTo(0);
        RuleFor(c => c.WatchedSeconds).GreaterThanOrEqualTo(0);
    }
}
