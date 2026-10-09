using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;

namespace Siri.UnitTests.Payout;

/// <summary>
/// In-memory <see cref="IInstructorPayoutAccountRepository"/> shared by the payout-account service and controller tests. <see cref="Add"/> stages straight into
/// <see cref="Accounts"/>; the save counters and the one-shot failure hook let a test observe "was anything written" and simulate a concurrent writer winning the
/// unique-instructor race (the real repository throws <c>DbUpdateException</c> there).
/// </summary>
internal sealed class FakeInstructorPayoutAccountRepository : IInstructorPayoutAccountRepository
{
    public readonly Dictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT> Accounts = [];

    /// <summary>How many times <see cref="SaveChangesAsync"/> was called (also counts a call that threw).</summary>
    public int SaveCalls { get; private set; }

    /// <summary>How many staged-but-unsaved accounts were thrown away through <see cref="Discard"/>.</summary>
    public int DiscardCalls { get; private set; }

    /// <summary>When set, the next <see cref="SaveChangesAsync"/> first runs <see cref="BeforeFailingSave"/> (the "other writer" committing) and then throws this.</summary>
    public Exception? FailNextSaveWith { get; set; }

    public Action? BeforeFailingSave { get; set; }

    public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.TryGetValue(id, out var acc) ? acc : null);

    public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.Values.FirstOrDefault(a => a.INSTRUCTOR_ID == instructorId));

    public Task<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>> GetVerifiedAccountsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
    {
        var idSet = instructorIds.ToHashSet();
        var dict = Accounts.Values
            .Where(a => idSet.Contains(a.INSTRUCTOR_ID) && a.VERIFIED_AT_UTC != null)
            .ToDictionary(a => a.INSTRUCTOR_ID);
        return Task.FromResult<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>>(dict);
    }

    public IQueryable<INSTRUCTOR_PAYOUT_ACCOUNT> Query() => Accounts.Values.AsQueryable();

    public void Add(INSTRUCTOR_PAYOUT_ACCOUNT account) => Accounts[account.INSTRUCTOR_PAYOUT_ACCOUNT_ID] = account;

    public void Discard(INSTRUCTOR_PAYOUT_ACCOUNT account)
    {
        DiscardCalls++;
        Accounts.Remove(account.INSTRUCTOR_PAYOUT_ACCOUNT_ID);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        if (FailNextSaveWith is { } failure)
        {
            FailNextSaveWith = null;
            BeforeFailingSave?.Invoke();
            throw failure;
        }

        return Task.CompletedTask;
    }
}
