namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/posts. Binds from the JSON request body.
/// <see cref="Domain.POST.AUTHOR_USER_ID"/> is deliberately not a field here — <see cref="PostService.CreateAsync"/>
/// takes the caller's own id from <c>IUserContext.UserId</c> instead, never the request body
/// (.claude/rules/backend.md: "อ่าน user ปัจจุบันจาก IUserContext เท่านั้น ห้ามรับ userId มาจาก request
/// body"), same shape <c>Siri.Modules.Payout.Application.InstructorPayoutAccountService.CreateForCurrentUserAsync</c>
/// already uses. <see cref="Domain.POST.STATUS"/> is likewise absent — every new post starts
/// <see cref="Domain.PostStatus.Draft"/> (see <see cref="Domain.POST.Create"/>'s own doc comment);
/// publishing is <see cref="ChangePostStatusCommand"/>'s job.
/// <para>
/// <see cref="Slug"/> is admin-supplied here, not auto-generated from <see cref="Title"/> the way
/// <c>Siri.Modules.Catalog.Features.CreateCourse.CreateCourseHandler</c> derives a course's slug via
/// <c>ThaiSlugGenerator</c> — building an equivalent transliteration utility for this module is out of this
/// scaffold's scope, and a CMS/blog editor letting the author set the SEO slug directly is a reasonable
/// content-management UX choice on its own terms, not just a shortcut.
/// </para>
/// </summary>
public sealed record CreatePostCommand(
    string Slug,
    string Title,
    string Excerpt,
    string ContentHtml,
    string? CoverImageUrl,
    string? SeoTitle,
    string? SeoDescription);
