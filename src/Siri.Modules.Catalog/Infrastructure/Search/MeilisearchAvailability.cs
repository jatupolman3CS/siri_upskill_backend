using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// Process-wide health memory for the Meilisearch client — a singleton because the client itself is transient (typed
/// <c>HttpClient</c>) and must not forget between requests. Two jobs:
/// <list type="bullet">
/// <item>a small circuit breaker: after <see cref="MeilisearchOptions.CircuitBreakerFailureThreshold"/> consecutive search failures,
/// searches skip the engine for <see cref="MeilisearchOptions.CircuitOpenSeconds"/>, so an outage costs one timeout per window instead
/// of one per visitor;</item>
/// <item>"index already ensured in this process", so a single-course sync does not re-send the settings on every approval.</item>
/// </list>
/// Reads time through <see cref="IClock"/> only (database.md).
/// </summary>
public sealed class MeilisearchAvailability(IClock clock, IOptions<MeilisearchOptions> options)
{
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTime _openUntilUtc = DateTime.MinValue;
    private volatile bool _indexEnsured;

    /// <summary>True while the breaker is open (searches must go straight to the fallback).</summary>
    public bool IsCircuitOpen
    {
        get
        {
            lock (_gate)
            {
                return clock.UtcNow < _openUntilUtc;
            }
        }
    }

    public bool IndexEnsured => _indexEnsured;

    public void MarkIndexEnsured() => _indexEnsured = true;

    public void MarkIndexUnknown() => _indexEnsured = false;

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntilUtc = DateTime.MinValue;
        }
    }

    /// <summary>Returns true when this failure tripped the breaker (so the caller can log the transition once).</summary>
    public bool RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < options.Value.CircuitBreakerFailureThreshold)
            {
                return false;
            }

            var wasOpen = clock.UtcNow < _openUntilUtc;
            _openUntilUtc = clock.UtcNow.AddSeconds(options.Value.CircuitOpenSeconds);

            // Half-open behaviour falls out for free: once the window passes, the next search is allowed through; one more failure
            // re-opens immediately (the counter is still at/above the threshold), one success resets it.
            return !wasOpen;
        }
    }
}
