using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>
/// Stable <see cref="DomainError"/> codes returned by the Google integration (P11-03 contract section 5).
/// <para>
/// <see cref="DomainError.Message"/> is deliberately generic (operation + HTTP status + a sanitised Google
/// reason token) and <see cref="DomainError.Reason"/> carries Google's own error/reason token when one was
/// provided (<c>invalid_grant</c>, <c>invalid_client</c>, <c>rateLimitExceeded</c>, <c>insufficientPermissions</c>, ...).
/// Neither ever contains a token, authorization code, e-mail address or meeting URL.
/// </para>
/// </summary>
public static class GoogleErrors
{
    /// <summary>Token endpoint <c>invalid_grant</c>/<c>invalid_client</c>/<c>unauthorized_client</c>, or Calendar/userinfo 401, or a 403 that is a scope/permission problem. The refresh token (or access token) can no longer be used.</summary>
    public const string UnauthorizedCode = "google.unauthorized";

    /// <summary>Meet REST / Drive 403 that is a missing scope or no access to the resource (P11-13). Distinct from
    /// <see cref="UnauthorizedCode"/> (the token itself is dead): the token works, but it was not granted the recording scopes, or the
    /// account has no access to that conference/file. Reconnect with the recording consent (or upload by hand) is the remedy.</summary>
    public const string ForbiddenCode = "google.forbidden";

    /// <summary>Calendar 404/410 (event gone), or a Meet/Drive resource that does not exist (any more).</summary>
    public const string NotFoundCode = "google.not_found";

    /// <summary>429, or 403 with a rate/quota reason. Transient — retry later.</summary>
    public const string RateLimitedCode = "google.rate_limited";

    /// <summary>5xx, timeout, network failure or an unreadable 2xx body. Transient — retry later.</summary>
    public const string TransientCode = "google.transient";

    /// <summary>400 and any other non-retryable 4xx (malformed data, unknown 403 reason, redirect_uri_mismatch, ...).</summary>
    public const string BadRequestCode = "google.bad_request";

    /// <summary>
    /// <c>Integrations:Google:ClientId</c> is empty so the connect feature is switched off. Ends with
    /// <see cref="DomainErrorHttpResults.DotNotConfiguredCodeSuffix"/>, so the shared mapper answers 503 if it ever reaches the API
    /// unmapped (the Live service translates it to <c>Unavailable</c> + <c>live.google_not_configured</c> itself).
    /// </summary>
    public const string NotConfiguredCode = "google.not_configured";

    public static DomainError Unauthorized(string message, string? googleReason = null) =>
        WithReason(new DomainError(UnauthorizedCode, message), googleReason);

    public static DomainError Forbidden(string message, string? googleReason = null) =>
        WithReason(new DomainError(ForbiddenCode, message), googleReason);

    public static DomainError NotFound(string message, string? googleReason = null) =>
        WithReason(new DomainError(NotFoundCode, message), googleReason);

    public static DomainError RateLimited(string message, string? googleReason = null) =>
        WithReason(new DomainError(RateLimitedCode, message), googleReason);

    public static DomainError Transient(string message, string? googleReason = null) =>
        WithReason(new DomainError(TransientCode, message), googleReason);

    public static DomainError BadRequest(string message, string? googleReason = null) =>
        WithReason(new DomainError(BadRequestCode, message), googleReason);

    public static DomainError NotConfigured() =>
        new(NotConfiguredCode, "Google integration is not configured.");

    private static DomainError WithReason(DomainError error, string? googleReason) =>
        string.IsNullOrEmpty(googleReason) ? error : error.WithReason(googleReason);

    /// <summary>
    /// Keeps only characters safe to log / store in an error column (<c>[A-Za-z0-9_.-]</c>) and caps the length,
    /// so an attacker-influenced or oversized value from a remote error body can never reach logs or a DB column.
    /// </summary>
    internal static string? Sanitize(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var span = token.AsSpan(0, Math.Min(token.Length, 60));
        var buffer = new char[span.Length];
        var length = 0;
        foreach (var c in span)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')
            {
                buffer[length++] = c;
            }
        }

        return length == 0 ? null : new string(buffer, 0, length);
    }
}
