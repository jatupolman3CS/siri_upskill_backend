using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>Placeholder success value so non-generic operations can share the generic send pipeline.</summary>
internal readonly record struct Unit;

/// <summary>What Google's error body told us — only ever sanitised tokens, never Google's free-text message.</summary>
/// <param name="Reason">Google's machine token: <c>error</c> (token endpoint, e.g. <c>invalid_grant</c>) or
/// <c>error.errors[0].reason</c> (Calendar, e.g. <c>rateLimitExceeded</c>).</param>
internal readonly record struct GoogleErrorInfo(string? Reason);

/// <summary>
/// The single send pipeline + error mapping shared by <see cref="GoogleOAuthService"/> and
/// <see cref="GoogleCalendarProvider"/> (P11-03 contract section 5 error table).
/// <para>
/// Rules enforced here: every transport failure becomes a typed result (never an exception, except caller
/// cancellation); nothing about the request body, headers, tokens, codes, e-mails or response bodies is logged — only the
/// operation name, HTTP status and Google's sanitised reason token; there is no in-process retry (the Live sync job owns
/// backoff, and retrying a non-idempotent POST here could duplicate an event).
/// </para>
/// </summary>
internal static class GoogleHttp
{
    public static async Task<Result<T>> SendAsync<T>(
        IHttpClientFactory httpClientFactory,
        string clientName,
        ILogger logger,
        string operation,
        Func<HttpRequestMessage> buildRequest,
        Func<HttpResponseMessage, CancellationToken, Task<Result<T>>> handleResponse,
        CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(clientName);
            using var request = buildRequest();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            return await handleResponse(response, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Framework-generated message (DNS/TLS/connection); it never contains request bodies or headers.
            logger.LogWarning("Google {Operation} request failed: {ExceptionType}: {Message}", operation, ex.GetType().Name, ex.Message);
            return Result.Failure<T>(GoogleErrors.Transient($"Google {operation} request failed (network)."));
        }
        catch (IOException ex)
        {
            logger.LogWarning("Google {Operation} request failed while reading the response: {ExceptionType}", operation, ex.GetType().Name);
            return Result.Failure<T>(GoogleErrors.Transient($"Google {operation} request failed (network)."));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Google {Operation} request timed out.", operation);
            return Result.Failure<T>(GoogleErrors.Transient($"Google {operation} request timed out."));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // JsonException: malformed body. NotSupportedException: ReadFromJsonAsync on a non-JSON content type (e.g. an HTML error page behind a proxy).
            logger.LogWarning("Google {Operation} returned an unreadable body.", operation);
            return Result.Failure<T>(GoogleErrors.Transient($"Google {operation} returned an unexpected response."));
        }
    }

    public static async Task<Result> SendAsync(
        IHttpClientFactory httpClientFactory,
        string clientName,
        ILogger logger,
        string operation,
        Func<HttpRequestMessage> buildRequest,
        Func<HttpResponseMessage, CancellationToken, Task<Result>> handleResponse,
        CancellationToken ct)
    {
        var result = await SendAsync<Unit>(
            httpClientFactory,
            clientName,
            logger,
            operation,
            buildRequest,
            async (response, token) =>
            {
                var inner = await handleResponse(response, token).ConfigureAwait(false);
                return inner.IsSuccess ? Result.Success(default(Unit)) : Result.Failure<Unit>(inner.Error);
            },
            ct).ConfigureAwait(false);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>Reads the sanitised reason token out of a Google error body. Tolerates every shape Google uses
    /// (<c>{"error":"invalid_grant",...}</c> and <c>{"error":{"errors":[{"reason":"..."}],"status":"..."}}</c>) and an empty/non-JSON body.</summary>
    public static async Task<GoogleErrorInfo> ReadErrorInfoAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }

        var error = (root as JsonObject)?["error"];
        if (error is JsonValue)
        {
            return new GoogleErrorInfo(GoogleErrors.Sanitize(GetString(error)));
        }

        if (error is JsonObject errorObject)
        {
            string? reason = null;
            if (errorObject["errors"] is JsonArray { Count: > 0 } errors && errors[0] is JsonObject first)
            {
                reason = GetString(first["reason"]);
            }

            // Newer API surfaces report the token in error.status (e.g. PERMISSION_DENIED) or error.details[].reason.
            reason ??= GetString(errorObject["status"]);
            return new GoogleErrorInfo(GoogleErrors.Sanitize(reason));
        }

        return default;
    }

    /// <summary>Maps a failed Calendar/userinfo response to a typed error per the contract table.</summary>
    public static async Task<DomainError> MapCalendarErrorAsync(
        HttpResponseMessage response, ILogger logger, string operation, CancellationToken ct)
    {
        var info = await ReadErrorInfoAsync(response, ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        logger.LogWarning("Google {Operation} failed: HTTP {StatusCode} reason {Reason}", operation, status, info.Reason ?? "-");

        var reason = info.Reason;
        var suffix = reason is null ? string.Empty : $" ({reason})";
        var message = $"Google {operation} failed with HTTP {status}{suffix}.";

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return GoogleErrors.Unauthorized(message, reason);

            case HttpStatusCode.Forbidden:
                if (reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded")
                {
                    return GoogleErrors.RateLimited(message, reason);
                }

                // Scope/permission problem: the instructor must reconnect (or grant the calendar scope).
                if (reason is "insufficientPermissions" or "forbidden" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT" or "insufficientScope")
                {
                    return GoogleErrors.Unauthorized(message, reason);
                }

                // Any other 403 (notACalendarUser, accessNotConfigured, forbiddenForNonOrganizer, ...) cannot be fixed by retrying
                // or by reconnecting: surface it as a non-retryable bad request so it ends in "Failed" and manual fallback.
                return GoogleErrors.BadRequest(message, reason);

            case HttpStatusCode.NotFound:
            case HttpStatusCode.Gone:
                return GoogleErrors.NotFound(message, reason);

            case HttpStatusCode.TooManyRequests:
                return GoogleErrors.RateLimited(message, reason);

            case HttpStatusCode.RequestTimeout:
                return GoogleErrors.Transient(message, reason);

            default:
                return status >= 500
                    ? GoogleErrors.Transient(message, reason)
                    : GoogleErrors.BadRequest(message, reason);
        }
    }

    /// <summary>Maps a failed token-endpoint response (<c>{"error":"invalid_grant"}</c> style) to a typed error.</summary>
    public static async Task<DomainError> MapTokenErrorAsync(
        HttpResponseMessage response, ILogger logger, string operation, CancellationToken ct)
    {
        var info = await ReadErrorInfoAsync(response, ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        logger.LogWarning("Google {Operation} failed: HTTP {StatusCode} reason {Reason}", operation, status, info.Reason ?? "-");

        var reason = info.Reason;
        var suffix = reason is null ? string.Empty : $" ({reason})";
        var message = $"Google {operation} failed with HTTP {status}{suffix}.";

        if (reason is "invalid_grant" or "invalid_client" or "unauthorized_client")
        {
            return GoogleErrors.Unauthorized(message, reason);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => GoogleErrors.Unauthorized(message, reason),
            HttpStatusCode.TooManyRequests => GoogleErrors.RateLimited(message, reason),
            HttpStatusCode.RequestTimeout => GoogleErrors.Transient(message, reason),
            _ when status >= 500 => GoogleErrors.Transient(message, reason),
            _ => GoogleErrors.BadRequest(message, reason),
        };
    }

    /// <summary>False when the token is empty or contains whitespace/control characters. Such a value is never put in a header
    /// (it would be malformed, or a header-injection attempt); the caller answers <c>google.unauthorized</c> without calling Google.</summary>
    public static bool IsUsableToken(string? accessToken) =>
        !string.IsNullOrEmpty(accessToken) && !accessToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    /// <summary>Adds <c>Authorization: Bearer {token}</c>. Call only after <see cref="IsUsableToken"/>.</summary>
    public static void SetBearer(HttpRequestMessage request, string accessToken) =>
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

    public static Result<T> InvalidToken<T>(string operation) =>
        Result.Failure<T>(GoogleErrors.Unauthorized($"Google {operation} was not attempted: the access token is missing or malformed."));

    public static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
