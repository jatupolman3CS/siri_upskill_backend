using Siri.Modules.Catalog.Contracts;

namespace Siri.UnitTests.Payout;

/// <summary>
/// Stand-in for <see cref="IInstructorProfileReader"/>: maps an authenticated USER id to the instructor PROFILE id the money tables are keyed by. A user that was
/// never mapped has no profile (like a learner), exactly as the real reader answers.
/// </summary>
internal sealed class FakeInstructorProfileReader : IInstructorProfileReader
{
    private readonly Dictionary<Guid, Guid> _profileByUser = [];

    public int Calls { get; private set; }

    /// <summary>Gives <paramref name="userId"/> an instructor profile with id <paramref name="profileId"/> and returns the profile id.</summary>
    public Guid Map(Guid userId, Guid profileId)
    {
        _profileByUser[userId] = profileId;
        return profileId;
    }

    public Task<Guid?> GetProfileIdByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<Guid?>(_profileByUser.TryGetValue(userId, out var profileId) ? profileId : null);
    }
}
