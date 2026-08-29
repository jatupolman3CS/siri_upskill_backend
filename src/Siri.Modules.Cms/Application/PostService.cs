using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public sealed class PostService(IPostRepository repository, IClock clock)
{
    public async Task<Result<PostResponse>> CreateAsync(Guid authorUserId, CreatePostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slugExists = await repository.SlugExistsAsync(command.Slug, null, cancellationToken).ConfigureAwait(false);
        if (slugExists)
        {
            return Result.Failure<PostResponse>(DomainError.Conflict("Slug นี้ถูกใช้งานแล้ว"));
        }

        var sanitizedHtml = HtmlSanitizerHelper.Sanitize(command.ContentHtml);

        var post = POST.Create(
            command.Slug,
            command.Title,
            command.Excerpt,
            sanitizedHtml,
            authorUserId,
            command.CoverImageUrl,
            command.SeoTitle,
            command.SeoDescription);

        repository.Add(post);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(post));
    }

    public async Task<Result<PostResponse>> UpdateAsync(Guid id, UpdatePostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var post = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return Result.Failure<PostResponse>(DomainError.NotFound("ไม่พบบทความ"));
        }

        var sanitizedHtml = HtmlSanitizerHelper.Sanitize(command.ContentHtml);

        post.Update(
            command.Title,
            command.Excerpt,
            sanitizedHtml,
            command.CoverImageUrl,
            command.SeoTitle,
            command.SeoDescription);

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(post));
    }

    public async Task<Result<PostResponse>> ChangeStatusAsync(Guid id, ChangePostStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var post = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return Result.Failure<PostResponse>(DomainError.NotFound("ไม่พบบทความ"));
        }

        post.ChangeStatus(command.Status, clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(post));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var post = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบบทความ"));
        }

        repository.Remove(post);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<PagedResult<PostResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.GetPagedAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return PagedResult<PostResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize);
    }

    public async Task<Result<PostResponse>> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var post = await repository.GetPublishedBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (post is null)
        {
            return Result.Failure<PostResponse>(DomainError.NotFound("ไม่พบบทความ"));
        }

        return Result.Success(ToResponse(post));
    }

    public async Task<PagedResult<PostResponse>> ListPublishedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.GetPublishedPagedAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return PagedResult<PostResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize);
    }

    private static PostResponse ToResponse(POST p) =>
        new(
            p.POST_ID,
            p.SLUG,
            p.TITLE,
            p.EXCERPT,
            HtmlSanitizerHelper.Sanitize(p.CONTENT_HTML),
            p.COVER_IMAGE_URL,
            p.AUTHOR_USER_ID,
            p.STATUS,
            p.PUBLISHED_AT_UTC,
            p.SEO_TITLE,
            p.SEO_DESCRIPTION);
}
