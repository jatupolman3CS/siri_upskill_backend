namespace Siri.SharedKernel;

/// <summary>
/// Represents an expected, well-known failure (validation, not found, forbidden, business rule
/// violation, ...). Carried inside a <see cref="Result"/>/<see cref="Result{T}"/> instead of being
/// thrown, per the "no exceptions as control flow" rule in .claude/rules/backend.md.
/// </summary>
/// <param name="Code">Stable machine-readable code, e.g. "course.not_found".</param>
/// <param name="Message">Human-readable message. UI-facing text still belongs in i18n files;
/// this is for logs/ProblemDetails/dev diagnostics.</param>
public sealed record DomainError(string Code, string Message)
{
    public static readonly DomainError None = new(string.Empty, string.Empty);

    /// <summary>
    /// Optional stable sub-code a client can branch on without parsing <see cref="Message"/>
    /// (e.g. <c>live.window_not_open</c>). Emitted as the <c>reason</c> member of the ProblemDetails
    /// body by <see cref="DomainErrorHttpResults.ToProblemHttpResult"/>. <c>null</c> for every error
    /// that does not need one, which leaves the response byte-for-byte as before.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Optional structured data that accompanies <see cref="Reason"/> (e.g. <c>opensAtUtc</c>,
    /// <c>sessionIds</c>). Keys are emitted into the ProblemDetails body exactly as supplied (callers
    /// pass camelCase). Must never carry secrets — it is returned to the client verbatim.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }

    public static DomainError NotFound(string message) => new("not_found", message);

    public static DomainError Validation(string message) => new("validation", message);

    public static DomainError Forbidden(string message) => new("forbidden", message);

    public static DomainError Conflict(string message) => new("conflict", message);

    /// <summary>
    /// A dependency this feature needs is temporarily or structurally unavailable on the server side
    /// (e.g. the OAuth state store is down, or a provider is not configured). Answered as HTTP 503.
    /// </summary>
    public static DomainError Unavailable(string message) => new("unavailable", message);

    /// <summary>
    /// Returns a copy of this error carrying a stable <paramref name="reason"/> sub-code and optional
    /// <paramref name="extensions"/>. <see cref="Code"/> (and therefore the HTTP status) is unchanged.
    /// </summary>
    public DomainError WithReason(string reason, IReadOnlyDictionary<string, object?>? extensions = null) =>
        this with { Reason = reason, Extensions = extensions };
}
