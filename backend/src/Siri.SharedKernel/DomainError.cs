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

    public static DomainError NotFound(string message) => new("not_found", message);

    public static DomainError Validation(string message) => new("validation", message);

    public static DomainError Forbidden(string message) => new("forbidden", message);

    public static DomainError Conflict(string message) => new("conflict", message);
}
