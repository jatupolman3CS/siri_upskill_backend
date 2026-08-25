using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only checks — no DB access (mirrors <see cref="CreateBannerValidator"/>'s own doc
/// comment: format here, uniqueness — <c>IRedirectRepository.FromPathExistsAsync</c> — in the
/// service).</summary>
public sealed class CreateRedirectValidator : AbstractValidator<CreateRedirectCommand>
{
    public const int MaxFromPathLength = 500; // matches REDIRECTS.FROM_PATH's column width (RedirectConfiguration)
    public const int MaxToPathLength = 1000; // matches REDIRECTS.TO_PATH's column width

    /// <summary>The redirect status codes that actually mean "redirect" over HTTP — 301/302 (classic
    /// permanent/temporary) and 307/308 (method-preserving variants). Rejects nonsense like 200 or 404
    /// being stored as a "redirect" status code.</summary>
    private static readonly int[] ValidStatusCodes = new[] { 301, 302, 307, 308 };

    public CreateRedirectValidator()
    {
        RuleFor(c => c.FromPath).NotEmpty().MaximumLength(MaxFromPathLength);
        RuleFor(c => c.ToPath).NotEmpty().MaximumLength(MaxToPathLength);
        RuleFor(c => c.StatusCode)
            .Must(code => ValidStatusCodes.Contains(code))
                .WithMessage("StatusCode ต้องเป็น 301, 302, 307 หรือ 308 เท่านั้น");
    }
}
