using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public sealed class RedirectService(IRedirectRepository repository)
{
    public async Task<Result<RedirectResponse>> CreateAsync(CreateRedirectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var exists = await repository.FromPathExistsAsync(command.FromPath, null, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return Result.Failure<RedirectResponse>(DomainError.Conflict("มีกฎการเปลี่ยนเส้นทางสำหรับ FromPath นี้อยู่แล้ว"));
        }

        var redirect = REDIRECT.Create(command.FromPath, command.ToPath, command.StatusCode);

        repository.Add(redirect);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(redirect));
    }

    public async Task<Result<RedirectResponse>> GetByFromPathAsync(string fromPath, CancellationToken cancellationToken)
    {
        var redirect = await repository.GetByFromPathAsync(fromPath, cancellationToken).ConfigureAwait(false);
        if (redirect is null)
        {
            return Result.Failure<RedirectResponse>(DomainError.NotFound("ไม่พบกฎการเปลี่ยนเส้นทาง"));
        }

        return Result.Success(ToResponse(redirect));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var redirect = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (redirect is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบกฎการเปลี่ยนเส้นทาง"));
        }

        repository.Remove(redirect);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<PagedResult<RedirectResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.GetPagedAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return PagedResult<RedirectResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize);
    }

    private static RedirectResponse ToResponse(REDIRECT r) =>
        new(r.REDIRECT_ID, r.FROM_PATH, r.TO_PATH, r.STATUS_CODE);
}
