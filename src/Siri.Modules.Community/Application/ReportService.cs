using Siri.Modules.Community.Application.Response;
using Siri.Modules.Community.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

public sealed class ReportService(IReportRepository repository, IDiscussionRepository discussionRepository, IClock clock)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<Result<ReportResponse>> CreateAsync(Guid reportedByUserId, CreateReportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var discussion = await discussionRepository.GetByIdAsync(command.DiscussionId, cancellationToken).ConfigureAwait(false);
        if (discussion is null)
        {
            return Result.Failure<ReportResponse>(DomainError.NotFound("ไม่พบกระทู้ที่ต้องการรายงาน"));
        }

        var report = REPORT.Create(command.DiscussionId, reportedByUserId, command.Reason);

        repository.Add(report);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(report));
    }

    public async Task<Result<PagedResult<ReportResponse>>> ListPendingAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize <= 0 || pageSize > MaxPageSize ? DefaultPageSize : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.ListPendingAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return Result.Success(PagedResult<ReportResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize));
    }

    public async Task<Result<ReportResponse>> ResolveAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await repository.GetByIdAsync(reportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ReportResponse>(DomainError.NotFound("ไม่พบรายงาน"));
        }

        report.Resolve(clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(report));
    }

    public async Task<Result<ReportResponse>> DismissAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await repository.GetByIdAsync(reportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ReportResponse>(DomainError.NotFound("ไม่พบรายงาน"));
        }

        report.Dismiss(clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(report));
    }

    private static ReportResponse ToResponse(REPORT r) =>
        new(
            r.REPORT_ID,
            r.DISCUSSION_ID,
            r.REPORTED_BY_USER_ID,
            r.REASON,
            r.STATUS,
            r.RESOLVED_AT_UTC,
            r.CreatedAtUtc);
}
