using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Community.Application.Response;
using Siri.Modules.Community.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

public sealed class DiscussionService(
    IDiscussionRepository repository,
    ICatalogPriceContract catalogPriceContract)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<Result<DiscussionResponse>> CreateAsync(Guid userId, CreateDiscussionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ParentId.HasValue)
        {
            var parent = await repository.GetByIdAsync(command.ParentId.Value, cancellationToken).ConfigureAwait(false);
            if (parent is null || parent.COURSE_ID != command.CourseId)
            {
                return Result.Failure<DiscussionResponse>(DomainError.NotFound("ไม่พบกระทู้หลัก"));
            }
        }

        var isInstructor = await catalogPriceContract.IsInstructorOwnerOfCourseAsync(command.CourseId, userId, cancellationToken).ConfigureAwait(false);

        var discussion = DISCUSSION.Create(
            command.CourseId,
            command.EpisodeId,
            userId,
            command.ParentId,
            command.Body,
            isInstructor);

        repository.Add(discussion);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(discussion));
    }

    public async Task<Result<PagedResult<DiscussionResponse>>> ListByEpisodeAsync(Guid episodeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize <= 0 || pageSize > MaxPageSize ? DefaultPageSize : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var paged = await repository.ListByEpisodeAsync(episodeId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mapped = paged.Items.Select(ToResponse).ToList();
        return Result.Success(PagedResult<DiscussionResponse>.Create(mapped, paged.TotalCount, effectivePage, effectivePageSize));
    }

    public async Task<Result<DiscussionResponse>> UpvoteAsync(Guid userId, Guid discussionId, CancellationToken cancellationToken)
    {
        var discussion = await repository.GetByIdAsync(discussionId, cancellationToken).ConfigureAwait(false);
        if (discussion is null)
        {
            return Result.Failure<DiscussionResponse>(DomainError.NotFound("ไม่พบกระทู้"));
        }

        discussion.Upvote();
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(discussion));
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid discussionId, CancellationToken cancellationToken)
    {
        var discussion = await repository.GetByIdAsync(discussionId, cancellationToken).ConfigureAwait(false);
        if (discussion is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบกระทู้"));
        }

        if (discussion.USER_ID != userId)
        {
            return Result.Failure(DomainError.Forbidden("คุณไม่มีสิทธิ์ลบกระทู้นี้"));
        }

        repository.Remove(discussion);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private static DiscussionResponse ToResponse(DISCUSSION d) =>
        new(
            d.DISCUSSION_ID,
            d.COURSE_ID,
            d.EPISODE_ID,
            d.USER_ID,
            d.PARENT_ID,
            d.BODY,
            d.IS_INSTRUCTOR_ANSWER,
            d.STATUS,
            d.UPVOTE_COUNT,
            d.CreatedAtUtc);
}
