namespace Siri.Modules.Cms.Application;

/// <summary>
/// Wire-shape projection of <see cref="Domain.REDIRECT"/>. Ordinary PascalCase properties — DTOs are
/// exempt from docs/DECISIONS.md D-17's UPPERCASE rule, same as <see cref="BannerResponse"/>.
/// </summary>
public sealed record RedirectResponse(Guid Id, string FromPath, string ToPath, int StatusCode);
