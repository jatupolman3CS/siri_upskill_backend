using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Format-only checks — no DB access (mirrors <see cref="CreateBannerValidator"/>'s own doc comment: format
/// here, uniqueness/existence/business rules — e.g. slug uniqueness via <c>IPostRepository.SlugExistsAsync</c> —
/// in the service). Deliberately does NOT attempt to sanitize or otherwise validate
/// <see cref="CreatePostCommand.ContentHtml"/>'s markup — that is the server-side allowlist sanitizer
/// called out on <see cref="Domain.POST.CONTENT_HTML"/>'s own doc comment, a later task's job, not a
/// format-only FluentValidation rule.
/// </summary>
public sealed class CreatePostValidator : AbstractValidator<CreatePostCommand>
{
    public const int MaxSlugLength = 200; // matches POSTS.SLUG's column width (PostConfiguration)
    public const int MaxTitleLength = 200; // matches POSTS.TITLE's column width
    public const int MaxExcerptLength = 500; // matches POSTS.EXCERPT's column width
    public const int MaxCoverImageUrlLength = 1000; // matches POSTS.COVER_IMAGE_URL's column width
    public const int MaxSeoTitleLength = 200; // matches POSTS.SEO_TITLE's column width
    public const int MaxSeoDescriptionLength = 500; // matches POSTS.SEO_DESCRIPTION's column width

    /// <summary>Lowercase letters/digits/hyphens only, no leading/trailing/doubled hyphen — a standard
    /// URL-slug shape. Deliberately simpler than any Thai-transliteration-aware check: there is no
    /// transliteration happening here to validate — see <see cref="CreatePostCommand"/>'s own doc comment
    /// for why the slug is admin-supplied, not derived from the title.</summary>
    private const string SlugPattern = @"^[a-z0-9]+(-[a-z0-9]+)*$";

    public CreatePostValidator()
    {
        RuleFor(c => c.Slug)
            .NotEmpty()
            .MaximumLength(MaxSlugLength)
            .Matches(SlugPattern)
                .WithMessage("Slug ต้องเป็นตัวพิมพ์เล็ก ตัวเลข และเครื่องหมายขีดกลางเท่านั้น");
        RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(c => c.Excerpt).NotEmpty().MaximumLength(MaxExcerptLength);
        RuleFor(c => c.ContentHtml).NotEmpty();
        RuleFor(c => c.CoverImageUrl).MaximumLength(MaxCoverImageUrlLength);
        RuleFor(c => c.SeoTitle).MaximumLength(MaxSeoTitleLength);
        RuleFor(c => c.SeoDescription).MaximumLength(MaxSeoDescriptionLength);
    }
}
