using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Wire-shape projection of <see cref="Domain.POST"/>. Ordinary PascalCase properties — DTOs are exempt
/// from docs/DECISIONS.md D-17's UPPERCASE rule, same as <see cref="BannerResponse"/>. Includes
/// <see cref="ContentHtml"/> as-is — see <see cref="Domain.POST.CONTENT_HTML"/>'s own doc comment: this is
/// NOT yet guaranteed sanitized, so a future public render path must not trust it blindly until the
/// sanitizer (a later task) exists — see <see cref="PostService"/>'s own doc comment.
/// </summary>
public sealed record PostResponse(
    Guid Id,
    string Slug,
    string Title,
    string Excerpt,
    string ContentHtml,
    string? CoverImageUrl,
    Guid AuthorUserId,
    PostStatus Status,
    DateTime? PublishedAtUtc,
    string? SeoTitle,
    string? SeoDescription);
