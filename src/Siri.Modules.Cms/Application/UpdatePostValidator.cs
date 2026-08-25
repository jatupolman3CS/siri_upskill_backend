using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only checks — no DB access. See <see cref="CreateBannerValidator"/>'s own doc comment
/// for why validators are real code in this scaffold pass. Same "do not sanitize HTML here" scope
/// exclusion as <see cref="CreatePostValidator"/>'s own doc comment.</summary>
public sealed class UpdatePostValidator : AbstractValidator<UpdatePostCommand>
{
    public UpdatePostValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(CreatePostValidator.MaxTitleLength);
        RuleFor(c => c.Excerpt).NotEmpty().MaximumLength(CreatePostValidator.MaxExcerptLength);
        RuleFor(c => c.ContentHtml).NotEmpty();
        RuleFor(c => c.CoverImageUrl).MaximumLength(CreatePostValidator.MaxCoverImageUrlLength);
        RuleFor(c => c.SeoTitle).MaximumLength(CreatePostValidator.MaxSeoTitleLength);
        RuleFor(c => c.SeoDescription).MaximumLength(CreatePostValidator.MaxSeoDescriptionLength);
    }
}
