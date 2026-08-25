using Siri.Modules.Community.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

/// <summary>Persistence abstraction for <see cref="REPORT"/>, implemented by
/// <see cref="Infrastructure.ReportRepository"/> — same EF-agnostic-on-the-Application-side reasoning as
/// <see cref="IDiscussionRepository"/>'s own doc comment.</summary>
public interface IReportRepository
{
    Task<REPORT?> GetByIdAsync(Guid reportId, CancellationToken cancellationToken);

    /// <summary>The admin moderation queue — only <see cref="Domain.ReportStatus.Pending"/> reports,
    /// oldest first (first reported, first reviewed).</summary>
    Task<PagedResult<REPORT>> ListPendingAsync(int page, int pageSize, CancellationToken cancellationToken);

    void Add(REPORT report);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
