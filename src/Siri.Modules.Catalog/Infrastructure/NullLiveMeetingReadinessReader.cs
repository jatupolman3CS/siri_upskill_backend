using Siri.Modules.Catalog.Contracts;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Default <see cref="ILiveMeetingReadinessReader"/> for hosts that do not load <c>Siri.Modules.Live</c> —
/// reports nothing as missing, i.e. the publish gate is a no-op. Production always loads Live, whose own
/// implementation is registered over this one (<c>TryAddScoped</c> here, so registration order does not matter).
/// </summary>
public sealed class NullLiveMeetingReadinessReader : ILiveMeetingReadinessReader
{
    public Task<IReadOnlyCollection<Guid>> GetSessionsWithoutUsableMeetingAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>([]);
}
