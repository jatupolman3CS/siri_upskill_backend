namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for PUT /api/cms/admin/posts/{id}. The target id comes from the route, not this record.
/// No <see cref="Domain.POST.SLUG"/>: immutable after creation in this scope, same precedent
/// <c>Siri.Modules.Catalog.Features.UpdateCourse.UpdateCourseCommand</c>'s own doc comment sets for
/// <c>Course.Slug</c> — doubly justified here because this exact module already owns the dedicated
/// mechanism (<see cref="Domain.REDIRECT"/>) for the "old URL still needs to work" problem a silent slug
/// change would create; a later task that wants to support renaming should pair it with a
/// <see cref="Domain.REDIRECT"/> row, not a bare field update. No <see cref="Domain.POST.STATUS"/>: owned
/// exclusively by <see cref="ChangePostStatusCommand"/>, same "one mutation method owns this field"
/// convention <see cref="UpdateBannerCommand"/>'s own doc comment describes for <c>BANNER.SortOrder</c>.
/// </summary>
public sealed record UpdatePostCommand(
    string Title,
    string Excerpt,
    string ContentHtml,
    string? CoverImageUrl,
    string? SeoTitle,
    string? SeoDescription);
