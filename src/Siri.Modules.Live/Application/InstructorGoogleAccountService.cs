using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The instructor's connection to their own Google account (aggregate: <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/>) — status,
/// connect (OAuth authorization-code + PKCE, state kept server-side in Redis), disconnect, and handing out short-lived access
/// tokens to the sync job (P11-03 contract section 6.1). There is no platform-wide Google credential: every call acts with one
/// instructor's token.
/// <para>
/// <b>Secrets:</b> the refresh token is only ever stored through <see cref="ISensitiveDataProtector"/>; access tokens live in this
/// scoped instance's dictionary for one run/request and are never persisted or logged; nothing here logs a token, code, state,
/// verifier or e-mail address.
/// </para>
/// <para>
/// <b>Revocation policy:</b> an <c>invalid_grant</c> (the refresh token is dead) or an insufficient-scope answer revokes the account
/// — clears the stored token, alerts the instructor once, and sends their meetings to <c>NeedsReconnect</c>. An <c>invalid_client</c>
/// is the <em>platform's</em> configuration being wrong (client id/secret), not the instructor's fault: the token is kept, nobody is
/// told to reconnect, an operator-level error is logged and the call is treated as transient. The same goes for a stored refresh
/// token this host cannot <em>decrypt</em> (wrong/rotated key, corrupt column — <see cref="CredentialUnreadableReason"/>): that is a
/// platform problem, never evidence about Google's side, so it must not revoke anyone's connection or e-mail them.
/// </para>
/// </summary>
public sealed class InstructorGoogleAccountService(
    IInstructorGoogleAccountRepository accounts,
    ISessionMeetingRepository meetings,
    IGoogleOAuthService oauth,
    IGoogleOAuthStateStore stateStore,
    ISensitiveDataProtector protector,
    ILiveScheduleReader schedule,
    IInstructorAlertSender alerts,
    IClock clock,
    IOptions<LiveOptions> liveOptions,
    IOptions<GoogleOAuthOptions> googleOptions,
    ILogger<InstructorGoogleAccountService> logger)
{
    /// <summary>OAuth <c>state</c> lifetime.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Refresh a cached access token this long before Google says it expires.</summary>
    private static readonly TimeSpan TokenExpirySkew = TimeSpan.FromMinutes(1);

    private static readonly DomainError NotConnectedError = new(NotConnectedCode, "The instructor has not connected a usable Google account.");

    public const string NotConnectedCode = "live.google_not_connected";

    public const string NotConfiguredReason = "live.google_not_configured";

    /// <summary><see cref="DomainError.Reason"/> of the transient failure returned when a stored refresh token cannot be decrypted here
    /// (a platform configuration problem — see <see cref="RefreshAsync"/>); the sync job stores it as <c>google_client_misconfigured</c>.</summary>
    public const string CredentialUnreadableReason = "credential_unreadable";

    /// <summary>Statuses that mean "this class is blocked on the instructor": counted in the status page.</summary>
    private static readonly MeetingSyncStatus[] BlockedStatuses = [MeetingSyncStatus.NeedsReconnect, MeetingSyncStatus.AwaitingLink];

    private readonly Dictionary<Guid, CachedToken> _tokenCache = [];

    private sealed record CachedToken(Result<string> Result, DateTime? ExpiresAtUtc);

    /// <summary>The Google connect feature is usable: an OAuth client is configured and rooms are not manual-only.</summary>
    public bool IsFeatureAvailable => oauth.IsConfigured && liveOptions.Value.Provider != LiveProviderMode.ManualOnly;

    // ---- Status -----------------------------------------------------------------------------------

    public async Task<GoogleConnectionStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var configured = IsFeatureAvailable;
        var account = await accounts.GetByInstructorUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return new GoogleConnectionStatusResponse(configured, false, false, null, null, null, null, 0);
        }

        var deliberatelyDisconnected = account.REVOKED_REASON == GoogleAccountRevokedReason.UserDisconnected;
        var needsReconnect = !account.IsActive && !deliberatelyDisconnected;
        var affected = await CountFutureSessionsWithoutRoomAsync(userId, BlockedStatuses, cancellationToken).ConfigureAwait(false);

        // After a deliberate disconnect the account is forgotten: no e-mail/dates are shown.
        return new GoogleConnectionStatusResponse(
            configured,
            account.IsActive,
            needsReconnect,
            deliberatelyDisconnected ? null : account.GOOGLE_EMAIL,
            deliberatelyDisconnected ? null : account.CONNECTED_AT_UTC,
            deliberatelyDisconnected ? null : account.LAST_VALIDATED_AT_UTC,
            account.REVOKED_REASON,
            affected);
    }

    // ---- Connect ----------------------------------------------------------------------------------

    public async Task<Result<GoogleConnectResponse>> BeginConnectAsync(Guid userId, string? returnPath, CancellationToken cancellationToken)
    {
        if (!IsFeatureAvailable)
        {
            return Result.Failure<GoogleConnectResponse>(NotConfigured());
        }

        var safeReturnPath = GoogleOAuthOptions.IsSafeInstructorPath(returnPath)
            ? returnPath!
            : googleOptions.Value.PostConnectRedirectPath;

        var state = GoogleOAuthPkce.GenerateState();
        var codeVerifier = GoogleOAuthPkce.GenerateCodeVerifier();

        var stored = await stateStore
            .TrySaveAsync(state, new GoogleOAuthState(userId, codeVerifier, safeReturnPath), StateLifetime, cancellationToken)
            .ConfigureAwait(false);
        if (!stored)
        {
            return Result.Failure<GoogleConnectResponse>(
                DomainError.Unavailable("ไม่สามารถเริ่มการเชื่อมต่อ Google ได้ในขณะนี้ กรุณาลองใหม่อีกครั้ง"));
        }

        var url = oauth.BuildAuthorizationUrl(state, GoogleOAuthPkce.ComputeCodeChallenge(codeVerifier));
        return new GoogleConnectResponse(url);
    }

    /// <summary>
    /// Finishes the OAuth round trip (the anonymous callback). <b>Never throws and always yields a redirect</b> — it is a browser
    /// navigation, so every failure becomes <c>?google=error&amp;reason=...</c>. Trust comes entirely from the single-use
    /// <paramref name="state"/>; the user is the one who started the flow, never anything in the callback request.
    /// </summary>
    public async Task<GoogleConnectOutcome> CompleteConnectAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        // Until a state is consumed we do not know the instructor's return path, so errors land on the default page.
        var defaultPath = googleOptions.Value.PostConnectRedirectPath;

        if (string.IsNullOrEmpty(state) || state.Length > 256)
        {
            return Failure(defaultPath, GoogleConnectErrorReasons.StateInvalid);
        }

        var payload = await stateStore.TryConsumeAsync(state, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return Failure(defaultPath, GoogleConnectErrorReasons.StateInvalid);
        }

        var returnPath = GoogleOAuthOptions.IsSafeInstructorPath(payload.ReturnPath) ? payload.ReturnPath : defaultPath;

        if (!IsFeatureAvailable)
        {
            return Failure(returnPath, GoogleConnectErrorReasons.ExchangeFailed);
        }

        if (!string.IsNullOrEmpty(error))
        {
            return Failure(
                returnPath,
                string.Equals(error, "access_denied", StringComparison.Ordinal)
                    ? GoogleConnectErrorReasons.AccessDenied
                    : GoogleConnectErrorReasons.ExchangeFailed);
        }

        if (string.IsNullOrEmpty(code) || code.Length > 2048)
        {
            return Failure(returnPath, GoogleConnectErrorReasons.ExchangeFailed);
        }

        try
        {
            return await ConnectAsync(payload, returnPath, code, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The callback is a navigation: it must end in a redirect whatever happens. Only the exception type is logged.
            logger.LogError("Completing the Google connection failed unexpectedly: {ExceptionType}.", ex.GetType().Name);
            return Failure(returnPath, GoogleConnectErrorReasons.ExchangeFailed);
        }
    }

    private async Task<GoogleConnectOutcome> ConnectAsync(GoogleOAuthState payload, string returnPath, string code, CancellationToken cancellationToken)
    {
        var exchange = await oauth.ExchangeCodeAsync(code, payload.CodeVerifier, cancellationToken).ConfigureAwait(false);
        if (exchange.IsFailure)
        {
            LogExchangeFailure(exchange.Error);
            return Failure(returnPath, GoogleConnectErrorReasons.ExchangeFailed);
        }

        var tokens = exchange.Value;

        if (!GoogleScopes.HasCalendarScope(tokens.GrantedScopes))
        {
            // The instructor unticked the calendar permission: keep nothing and undo what Google issued.
            await RevokeQuietlyAsync(tokens.RefreshToken ?? tokens.AccessToken, cancellationToken).ConfigureAwait(false);
            return Failure(returnPath, GoogleConnectErrorReasons.ScopeMissing);
        }

        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            await RevokeQuietlyAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
            return Failure(returnPath, GoogleConnectErrorReasons.NoRefreshToken);
        }

        var userInfo = await oauth.GetUserInfoAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        if (userInfo.IsFailure)
        {
            LogExchangeFailure(userInfo.Error);
            await RevokeQuietlyAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            return Failure(returnPath, GoogleConnectErrorReasons.ExchangeFailed);
        }

        var encryptedRefreshToken = protector.Encrypt(tokens.RefreshToken);

        var existing = await accounts.GetByInstructorUserIdAsync(payload.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            accounts.Add(INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
                payload.UserId, userInfo.Value.Subject, userInfo.Value.Email, encryptedRefreshToken, tokens.GrantedScopes, clock));
        }
        else
        {
            // Replacing a still-working credential: the old token is retired at Google, best effort.
            if (existing.IsActive)
            {
                await RevokeQuietlyAsync(DecryptOrNull(existing.REFRESH_TOKEN_ENCRYPTED), cancellationToken).ConfigureAwait(false);
            }

            existing.Reconnect(userInfo.Value.Subject, userInfo.Value.Email, encryptedRefreshToken, tokens.GrantedScopes, clock);
        }

        // Rooms that were waiting on this instructor's Google account get another go.
        foreach (var meeting in await meetings.GetResettableByInstructorAsync(payload.UserId, cancellationToken).ConfigureAwait(false))
        {
            if (meeting.CanRequestResync)
            {
                meeting.RequestResync();
            }
        }

        await accounts.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _tokenCache.Remove(payload.UserId);

        return new GoogleConnectOutcome(BuildRedirectUrl(returnPath, "?google=connected"), ErrorReason: null);
    }

    // ---- Disconnect -------------------------------------------------------------------------------

    /// <summary>Idempotent. Revokes the token at Google (best effort, outcome ignored) and clears it. Rooms that already
    /// exist stay usable; they just can no longer be edited through the platform until the instructor reconnects.</summary>
    public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByInstructorUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.IsActive)
        {
            return;
        }

        await RevokeQuietlyAsync(DecryptOrNull(account.REFRESH_TOKEN_ENCRYPTED), cancellationToken).ConfigureAwait(false);

        account.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, clock);
        await accounts.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _tokenCache.Remove(userId);
    }

    // ---- Access tokens for the sync job ------------------------------------------------------------

    /// <summary>The instructor's active account row, or <c>null</c>.</summary>
    public async Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetActiveAccountAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        return account is { IsActive: true } ? account : null;
    }

    /// <summary>The instructor's account row whatever its state (active, revoked, deliberately disconnected), or <c>null</c> if they never connected.</summary>
    public Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetAccountAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        accounts.GetByInstructorUserIdAsync(instructorUserId, cancellationToken);

    /// <summary>
    /// A fresh access token for the instructor, via their stored refresh token. Outcomes are typed errors:
    /// <c>live.google_not_connected</c> (no usable account), <c>google.unauthorized</c> (the refresh token is dead — the account has
    /// just been revoked and the instructor alerted, once), or any other Google error (transient/rate-limited/bad request/
    /// misconfigured client) which the caller may retry. The result — including a failure — is cached on this scoped instance, so one
    /// job run asks Google at most once per instructor.
    /// </summary>
    public async Task<Result<string>> TryGetAccessTokenAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        if (_tokenCache.TryGetValue(instructorUserId, out var cached)
            && (cached.ExpiresAtUtc is null || cached.ExpiresAtUtc.Value - TokenExpirySkew > clock.UtcNow))
        {
            return cached.Result;
        }

        var result = await RefreshAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<Result<string>> RefreshAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var account = await GetActiveAccountAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Remember(instructorUserId, Result.Failure<string>(NotConnectedError), expiresAtUtc: null);
        }

        string refreshToken;
        try
        {
            refreshToken = protector.Decrypt(account.REFRESH_TOKEN_ENCRYPTED!);
        }
        catch (Exception ex) when (IsUnreadableCiphertext(ex))
        {
            // Our own failure to read the credential — the encryption key is wrong for this host (a Workers/API key mismatch, a rotated
            // key, a restored database) or the column is corrupted — says NOTHING about whether Google still honours the token. Revoking
            // here would, with one misconfigured host, disconnect every instructor and e-mail each of them to reconnect. So treat it like
            // an invalid_client: an operator-level error, the account untouched, nobody alerted, and the attempt recorded as a
            // transient/platform-configuration failure (the instructor can still disconnect/reconnect to replace a truly corrupt row).
            logger.LogError(
                "An instructor's stored Google refresh token could not be decrypted ({ExceptionType}). Check DataProtection:EncryptionKeyBase64 is the same key on Siri.Api and Siri.Workers. The account is NOT revoked and no one is alerted; the operation will be retried. Instructor {InstructorUserId}.",
                ex.GetType().Name,
                instructorUserId);
            return Remember(
                instructorUserId,
                Result.Failure<string>(GoogleErrors.Transient("The stored Google credential could not be read.", CredentialUnreadableReason)),
                expiresAtUtc: null);
        }

        var refreshed = await oauth.RefreshAccessTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        if (refreshed.IsSuccess)
        {
            account.MarkValidated(clock);
            return Remember(instructorUserId, Result.Success(refreshed.Value.AccessToken), refreshed.Value.ExpiresAtUtc);
        }

        var error = refreshed.Error;
        if (error.Code == GoogleErrors.UnauthorizedCode)
        {
            if (error.Reason is "invalid_client" or "unauthorized_client")
            {
                // Platform misconfiguration, not the instructor's problem: keep their token, do not tell them to reconnect.
                logger.LogError(
                    "Google rejected the OAuth client credentials ({Reason}). Check Integrations:Google:ClientId/ClientSecret. Instructor tokens are NOT revoked; the operation will be retried.",
                    error.Reason);
                return Remember(
                    instructorUserId,
                    Result.Failure<string>(GoogleErrors.Transient("Google rejected the platform's OAuth client credentials.", "invalid_client")),
                    expiresAtUtc: null);
            }

            await RevokeAndAlertAsync(account, GoogleAccountRevokedReason.InvalidGrant, cancellationToken).ConfigureAwait(false);
        }

        return Remember(instructorUserId, Result.Failure<string>(error), expiresAtUtc: null);
    }

    /// <summary>
    /// A Calendar call made with a freshly refreshed token answered <c>google.unauthorized</c>: the grant no longer covers what we
    /// need. Revokes the account (<c>insufficient_scope</c> when Google said the scope is too narrow, else <c>invalid_grant</c>),
    /// alerts the instructor once, saves, and returns the revocation reason.
    /// </summary>
    public async Task<string> HandleCalendarUnauthorizedAsync(Guid instructorUserId, DomainError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        var reason = error.Reason is "insufficientPermissions" or "forbidden" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT" or "insufficientScope" or "PERMISSION_DENIED"
            ? GoogleAccountRevokedReason.InsufficientScope
            : GoogleAccountRevokedReason.InvalidGrant;

        var account = await accounts.GetByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            await RevokeAndAlertAsync(account, reason, cancellationToken).ConfigureAwait(false);
        }

        _tokenCache.Remove(instructorUserId);
        return reason;
    }

    private async Task RevokeAndAlertAsync(INSTRUCTOR_GOOGLE_ACCOUNT account, string reason, CancellationToken cancellationToken)
    {
        // Alert only on the active -> revoked transition, so one dead credential produces one e-mail.
        var wasActive = account.IsActive;
        account.MarkRevoked(reason, clock);

        if (wasActive)
        {
            var affected = await CountFutureSessionsWithoutRoomAsync(
                account.INSTRUCTOR_USER_ID,
                [MeetingSyncStatus.Pending, MeetingSyncStatus.NeedsReconnect, MeetingSyncStatus.AwaitingLink, MeetingSyncStatus.Failed],
                cancellationToken).ConfigureAwait(false);

            await alerts.GoogleReconnectNeededAsync(account.INSTRUCTOR_USER_ID, affected, cancellationToken).ConfigureAwait(false);
        }

        // Committed immediately (together with the staged e-mail): a revocation must not be lost if the caller's own work fails later.
        await accounts.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>Upcoming scheduled classes of the instructor whose meeting has no room URL and sits in one of <paramref name="statuses"/>.</summary>
    private async Task<int> CountFutureSessionsWithoutRoomAsync(
        Guid instructorUserId, IReadOnlyCollection<MeetingSyncStatus> statuses, CancellationToken cancellationToken)
    {
        var page = await schedule
            .GetInstructorSessionContextsAsync(
                instructorUserId,
                fromUtc: clock.UtcNow,
                toUtc: null,
                includeCancelled: false,
                newestFirst: false,
                skip: 0,
                take: LiveScheduleLimits.MaxPageSize,
                cancellationToken)
            .ConfigureAwait(false);

        if (page.Items.Count == 0)
        {
            return 0;
        }

        var rows = await meetings
            .GetBySessionIdsAsync(page.Items.Select(i => i.SessionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        return rows.Count(m => m.MEET_URL_ENCRYPTED is null && statuses.Contains(m.SYNC_STATUS));
    }

    private Result<string> Remember(Guid instructorUserId, Result<string> result, DateTime? expiresAtUtc)
    {
        _tokenCache[instructorUserId] = new CachedToken(result, expiresAtUtc);
        return result;
    }

    private async Task RevokeQuietlyAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        // Best effort: the outcome never changes what we do next, and nothing about it is logged beyond the result being a failure.
        var revoked = await oauth.RevokeAsync(token, cancellationToken).ConfigureAwait(false);
        if (revoked.IsFailure)
        {
            logger.LogInformation("Revoking a Google token at Google did not succeed ({ErrorCode}); continuing.", revoked.Error.Code);
        }
    }

    private string? DecryptOrNull(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        try
        {
            return protector.Decrypt(cipherText);
        }
        catch (Exception ex) when (IsUnreadableCiphertext(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The ways <see cref="ISensitiveDataProtector.Decrypt"/> reports a value it cannot read: <see cref="CryptographicException"/> (wrong key /
    /// tampered), <see cref="FormatException"/> (not Base64) and <see cref="InvalidOperationException"/> (SensitiveDataProtector's "ciphertext too
    /// short to hold a nonce and tag" — a truncated column). All of them mean "unreadable here", never "Google rejected the token".
    /// </summary>
    private static bool IsUnreadableCiphertext(Exception ex) => ex is CryptographicException or FormatException or InvalidOperationException;

    private void LogExchangeFailure(DomainError error)
    {
        if (error.Reason is "invalid_client" or "unauthorized_client")
        {
            logger.LogError("Google rejected the OAuth client credentials ({Reason}). Check Integrations:Google:ClientId/ClientSecret.", error.Reason);
            return;
        }

        logger.LogWarning("Google connection could not be completed: {ErrorCode} ({Reason}).", error.Code, error.Reason ?? "-");
    }

    private GoogleConnectOutcome Failure(string path, string reason) =>
        new(BuildRedirectUrl(path, $"?google=error&reason={reason}"), reason);

    private string BuildRedirectUrl(string path, string query) =>
        $"{liveOptions.Value.GetNormalizedPublicBaseUrl()}{path}{query}";

    private static DomainError NotConfigured() =>
        DomainError.Unavailable("ระบบยังไม่เปิดให้เชื่อมต่อ Google Calendar").WithReason(NotConfiguredReason);
}
