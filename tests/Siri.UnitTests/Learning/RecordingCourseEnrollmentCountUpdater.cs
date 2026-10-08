using Siri.Modules.Catalog.Contracts;

namespace Siri.UnitTests.Learning;

/// <summary>A fake <see cref="ICourseEnrollmentCountUpdater"/> that records what Learning reported (course id + delta), in call order.</summary>
internal sealed class RecordingCourseEnrollmentCountUpdater : ICourseEnrollmentCountUpdater
{
    public List<(Guid CourseId, int Delta)> Adjustments { get; } = [];

    public Task AdjustAsync(Guid courseId, int delta, CancellationToken cancellationToken)
    {
        Adjustments.Add((courseId, delta));
        return Task.CompletedTask;
    }

    public Task<int> RecountAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> RecountDriftedAsync(int pageSize, CancellationToken cancellationToken) => Task.FromResult(0);
}
