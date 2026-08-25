using Microsoft.EntityFrameworkCore;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Infrastructure;

public sealed class ReportRepository(AppDbContext context) : IReportRepository
{
    public Task<REPORT?> GetByIdAsync(Guid reportId, CancellationToken cancellationToken) =>
        context.Reports().FirstOrDefaultAsync(r => r.REPORT_ID == reportId, cancellationToken);

    public async Task<PagedResult<REPORT>> ListPendingAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Reports()
            .Where(r => r.STATUS == ReportStatus.Pending);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<REPORT>.Create(items, totalCount, page, pageSize);
    }

    public void Add(REPORT report) => context.Reports().Add(report);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
