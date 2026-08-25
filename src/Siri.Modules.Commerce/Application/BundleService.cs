using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// No <c>IBundleService</c> interface — same reasoning as <c>OrderService</c>'s own doc comment.
/// <see cref="BUNDLE"/> is a public catalog-style resource (see its own doc comment) — <see cref="GetByIdAsync"/>/
/// <see cref="ListAsync"/> back public, unauthenticated storefront browsing, while <see cref="CreateAsync"/>
/// is admin-only (see <c>CommerceModule.MapCommerceEndpoints</c>'s split routing for this aggregate).
/// <para>
/// Primary constructor is safe here (contrast <c>RefundService</c>'s traditional constructor): the sole
/// dependency, <see cref="IBundleRepository"/>, IS read below (by <see cref="GetByIdAsync"/>).
/// </para>
/// </summary>
public sealed class BundleService(IBundleRepository bundleRepository)
{
    public async Task<Result<BundleResponse>> CreateAsync(CreateBundleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Slug))
        {
            return Result.Failure<BundleResponse>(DomainError.Validation("Slug is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Result.Failure<BundleResponse>(DomainError.Validation("Title is required."));
        }

        if (command.Price < 0)
        {
            return Result.Failure<BundleResponse>(DomainError.Validation("Price cannot be negative."));
        }

        if (command.CourseIds is null || command.CourseIds.Count == 0)
        {
            return Result.Failure<BundleResponse>(DomainError.Validation("Bundle must contain at least one course."));
        }

        var bundle = BUNDLE.Create(command.Slug, command.Title, command.Description, command.Price);

        foreach (var courseId in command.CourseIds.Distinct())
        {
            bundle.AddItem(courseId);
        }

        await bundleRepository.AddAsync(bundle, cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(bundle));
    }

    /// <summary>Public, no ownership to check (contrast <c>OrderService.GetByIdAsync</c>) — a plain
    /// fetch-by-id-or-404, same "public catalog-style resource" shape Catalog's own course-detail read
    /// uses.</summary>
    public async Task<Result<BundleResponse>> GetByIdAsync(Guid bundleId, CancellationToken cancellationToken)
    {
        var bundle = await bundleRepository.GetByIdAsync(bundleId, cancellationToken).ConfigureAwait(false);
        if (bundle is null) return Result.Failure<BundleResponse>(DomainError.NotFound("ไม่พบชุดคอร์สที่ระบุ"));
        return ToResponse(bundle);
    }

    /// <summary>Public storefront list — database.md: "รายการที่โตได้ต้อง paginate เสมอ".</summary>
    public async Task<PagedResult<BundleResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var totalCount = await bundleRepository.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await bundleRepository.ListAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        var mapped = items.Select(ToResponse).ToList();
        return PagedResult<BundleResponse>.Create(mapped, totalCount, effectivePage, effectivePageSize);
    }

    private static BundleResponse ToResponse(BUNDLE bundle) =>
        new(
            bundle.BUNDLE_ID, bundle.SLUG, bundle.TITLE, bundle.DESCRIPTION, bundle.PRICE, bundle.IS_ACTIVE,
            bundle.STARTS_AT_UTC, bundle.ENDS_AT_UTC,
            bundle.BUNDLE_ITEMS.Select(i => new BundleItemResponse(i.COURSE_ID)).ToList());
}

public sealed record BundleResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    decimal Price,
    bool IsActive,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    IReadOnlyList<BundleItemResponse> Items);

public sealed record BundleItemResponse(Guid CourseId);

public sealed record CreateBundleCommand(string Slug, string Title, string? Description, decimal Price, IReadOnlyList<Guid> CourseIds);
