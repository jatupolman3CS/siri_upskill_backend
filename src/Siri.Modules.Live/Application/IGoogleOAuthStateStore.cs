using System.Text.Json.Serialization;

namespace Siri.Modules.Live.Application;

/// <summary>Why the instructor is being sent to Google's consent screen (P11-13 contract section 4). Stored as a string in the state payload; a payload
/// written before the field existed has none and reads as <see cref="Calendar"/> (the first member, the type's default).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoogleOAuthPurpose>))]
public enum GoogleOAuthPurpose
{
    /// <summary>The ordinary connect: Calendar access so the platform can create Meet rooms.</summary>
    Calendar,

    /// <summary>The second, optional consent step: Meet/Drive read access so the platform can import recordings (Workspace accounts only).</summary>
    RecordingAccess,
}

/// <summary>What the connect step remembers about an OAuth round trip.</summary>
/// <param name="UserId">The instructor who started the connection (never taken from the callback request).</param>
/// <param name="CodeVerifier">PKCE verifier — sent only to Google's token endpoint. <b>Secret.</b></param>
/// <param name="ReturnPath">Validated in-SPA path to land on afterwards.</param>
/// <param name="Purpose">What the consent was for; decides which scopes the callback insists on.</param>
public sealed record GoogleOAuthState(Guid UserId, string CodeVerifier, string ReturnPath, GoogleOAuthPurpose Purpose = GoogleOAuthPurpose.Calendar);

/// <summary>
/// Short-lived server-side store for OAuth <c>state</c> values (P11-03 contract section 6.1). A state is single-use: the party
/// that deletes it wins, so a replayed callback finds nothing. The raw <c>state</c> is never persisted (only its hash is a key).
/// </summary>
public interface IGoogleOAuthStateStore
{
    /// <summary>Stores the payload under <paramref name="state"/>. <c>false</c> = it could not be recorded anywhere (the implementation
    /// already falls back to this process's memory when Redis is down, so this means that fallback is full too) — callers fail
    /// <em>closed</em> (an OAuth flow must never proceed with an unrecorded state).</summary>
    Task<bool> TrySaveAsync(string state, GoogleOAuthState payload, TimeSpan timeToLive, CancellationToken cancellationToken);

    /// <summary>Atomically-enough "get and delete": returns the payload only to the caller whose delete succeeded.
    /// <c>null</c> = unknown, expired, already used, or the store is unavailable.</summary>
    Task<GoogleOAuthState?> TryConsumeAsync(string state, CancellationToken cancellationToken);
}
