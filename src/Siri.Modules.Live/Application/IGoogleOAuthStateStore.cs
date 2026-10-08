namespace Siri.Modules.Live.Application;

/// <summary>What the connect step remembers about an OAuth round trip.</summary>
/// <param name="UserId">The instructor who started the connection (never taken from the callback request).</param>
/// <param name="CodeVerifier">PKCE verifier — sent only to Google's token endpoint. <b>Secret.</b></param>
/// <param name="ReturnPath">Validated in-SPA path to land on afterwards.</param>
public sealed record GoogleOAuthState(Guid UserId, string CodeVerifier, string ReturnPath);

/// <summary>
/// Short-lived server-side store for OAuth <c>state</c> values (P11-03 contract section 6.1). A state is single-use: the party
/// that deletes it wins, so a replayed callback finds nothing. The raw <c>state</c> is never persisted (only its hash is a key).
/// </summary>
public interface IGoogleOAuthStateStore
{
    /// <summary>Stores the payload under <paramref name="state"/>. <c>false</c> = the store is unavailable — callers fail
    /// <em>closed</em> (an OAuth flow must never proceed with an unrecorded state).</summary>
    Task<bool> TrySaveAsync(string state, GoogleOAuthState payload, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>Atomically-enough "get and delete": returns the payload only to the caller whose delete succeeded.
    /// <c>null</c> = unknown, expired, already used, or the store is unavailable.</summary>
    Task<GoogleOAuthState?> TryConsumeAsync(string state, CancellationToken cancellationToken);
}
