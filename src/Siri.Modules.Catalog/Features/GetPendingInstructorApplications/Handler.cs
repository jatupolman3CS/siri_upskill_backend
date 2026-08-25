using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetPendingInstructorApplications;

/// <summary>
/// Pending instructor applications for admin review, offset-paginated (database.md: "รายการที่โตได้ต้อง
/// paginate เสมอ" + "offset ได้สำหรับ admin table") — this codebase's first real use of
/// <see cref="PagedResult{T}"/>.
/// </summary>
public sealed class GetPendingInstructorApplicationsHandler(AppDbContext dbContext)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<InstructorApplicationSummary>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        // Clamped here, not the endpoint — an out-of-range page/pageSize is a business rule about what
        // this list means, not an HTTP-binding concern (backend.md: "Endpoint ต้องบางที่สุด").
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        var query = dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.Status == InstructorApplicationStatus.Pending)
            .OrderBy(p => p.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(p => new InstructorApplicationSummary(p.Id, p.UserId, p.DisplayName, p.Headline, p.Bio, p.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<InstructorApplicationSummary>.Create(items, totalCount, effectivePage, effectivePageSize);
    }
}
