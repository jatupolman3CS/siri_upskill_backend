using System.Text.RegularExpressions;

namespace Siri.Modules.Catalog.Application;

/// <summary>
/// The full-text search engine behind the public course search (Meilisearch — see
/// <c>Infrastructure.Search.MeilisearchCourseSearchIndex</c>). The index is a <em>derived copy</em> of Published
/// courses: PostgreSQL stays the source of truth for visibility, price, rating and every filter, and the engine only
/// answers "which courses match this text, best first" (course text and the instructor's name).
/// <para>
/// Error contract: <see cref="SearchCourseIdsAsync"/> never throws for engine trouble — it returns <c>null</c> so the
/// caller falls back to the database. Every write/maintenance method throws <see cref="CourseSearchIndexException"/>
/// instead, so a background job fails visibly rather than pretending the index is current.
/// </para>
/// </summary>
public interface ICourseSearchIndex
{
    /// <summary>False when Meilisearch is not configured (or switched off): every other member is then a harmless no-op.</summary>
    bool IsEnabled { get; }

    /// <summary>Course ids matching <paramref name="text"/>, most relevant first. <c>null</c> = the engine could not answer
    /// (disabled, unreachable, timed out, circuit open) and the caller must use its fallback; an empty list = it answered "no match".</summary>
    Task<IReadOnlyList<Guid>?> SearchCourseIdsAsync(string text, CancellationToken cancellationToken);

    /// <summary>Creates the index if missing and applies the search settings (searchable/filterable attributes, pagination ceiling). Idempotent.</summary>
    Task EnsureIndexAsync(CancellationToken cancellationToken);

    /// <summary>Adds or replaces documents by id. With <paramref name="waitForCompletion"/> it blocks until the engine finished (or failed) the task.</summary>
    Task UpsertCoursesAsync(IReadOnlyCollection<CourseSearchDocument> documents, bool waitForCompletion, CancellationToken cancellationToken);

    Task DeleteCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken);

    /// <summary>Every course id currently in the index (used to find documents that must be removed).</summary>
    Task<IReadOnlyCollection<Guid>> ListIndexedCourseIdsAsync(CancellationToken cancellationToken);

    /// <summary>Number of course documents in the index, or <c>null</c> when the index does not exist yet (or Meilisearch is off).
    /// Throws <see cref="CourseSearchIndexException"/> when the engine cannot be reached.</summary>
    Task<long?> CountCourseDocumentsAsync(CancellationToken cancellationToken);
}

/// <summary>Any failure talking to the search engine (network, timeout, rejected request, failed task). The message never contains the API key.</summary>
public sealed class CourseSearchIndexException : Exception
{
    public CourseSearchIndexException(string message)
        : base(message)
    {
    }

    public CourseSearchIndexException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One course as stored in the search index. JSON property names are the camelCase of these members (the index's attribute names).
/// <see cref="Id"/> is the primary key (<c>course_{guid}</c>); <see cref="Type"/> is the discriminator for the shared documents index.
/// </summary>
public sealed partial record CourseSearchDocument(
    string Id,
    string Type,
    Guid CourseId,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    Guid InstructorId,
    string InstructorName,
    string? InstructorHeadline,
    Guid CategoryId,
    string? CategoryNameTh,
    string? CategoryNameEn)
{
    public const string CourseType = "course";

    /// <summary>Longest description text sent to the engine — the head of a description carries what a visitor searches for, and an
    /// unbounded one only inflates the index.</summary>
    public const int MaxDescriptionLength = 4_000;

    /// <summary>Meilisearch document ids allow letters, digits, '-' and '_' only — a hyphenated GUID fits, a bare GUID would have no type prefix.</summary>
    public static string IdFor(Guid courseId) => $"{CourseType}_{courseId:D}";

    public static CourseSearchDocument ForCourse(
        Guid courseId,
        string slug,
        string title,
        string? subtitle,
        string? description,
        Guid instructorId,
        string instructorName,
        string? instructorHeadline,
        Guid categoryId,
        string? categoryNameTh,
        string? categoryNameEn) =>
        new(
            IdFor(courseId),
            CourseType,
            courseId,
            slug,
            title,
            subtitle,
            ToSearchText(description),
            instructorId,
            instructorName,
            instructorHeadline,
            categoryId,
            categoryNameTh,
            categoryNameEn);

    /// <summary>Tags removed, whitespace collapsed, capped at <see cref="MaxDescriptionLength"/>; <c>null</c> when nothing searchable is left.</summary>
    public static string? ToSearchText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withoutTags = HtmlTag().Replace(html, " ");
        var collapsed = Whitespace().Replace(withoutTags, " ").Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length <= MaxDescriptionLength ? collapsed : collapsed[..MaxDescriptionLength];
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
