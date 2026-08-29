using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCourseReview;

public sealed record CreateCourseReviewRequest(int Rating, string? Comment);

public sealed record CourseReviewDto(
    Guid Id,
    Guid CourseId,
    Guid UserId,
    string UserName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc);

public sealed class CreateCourseReviewHandler(
    AppDbContext dbContext,
    ILearningEnrollmentChecker enrollmentChecker,
    IUserContactReader userContactReader,
    IClock clock)
{
    public async Task<Result<CourseReviewDto>> HandleAsync(
        Guid courseId,
        Guid userId,
        CreateCourseReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<CourseReviewDto>(DomainError.Forbidden("ไม่พบข้อมูลผู้ใช้"));
        }

        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<CourseReviewDto>(DomainError.NotFound("ไม่พบคอร์สเรียน"));
        }

        var isEnrolled = await enrollmentChecker.HasActiveEnrollmentAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        if (!isEnrolled)
        {
            return Result.Failure<CourseReviewDto>(DomainError.Validation("คุณต้องลงทะเบียนเรียนคอร์สนี้ก่อนจึงจะสามารถเขียนรีวิวได้"));
        }

        var existingReview = await dbContext.CourseReviews()
            .FirstOrDefaultAsync(r => r.CourseId == courseId && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        COURSE_REVIEW review;
        if (existingReview is not null)
        {
            existingReview.Update(request.Rating, request.Comment, clock.UtcNow);
            review = existingReview;
        }
        else
        {
            var createResult = COURSE_REVIEW.Create(courseId, userId, request.Rating, request.Comment, clock.UtcNow);
            if (createResult.IsFailure)
            {
                return Result.Failure<CourseReviewDto>(createResult.Error);
            }

            review = createResult.Value;
            dbContext.CourseReviews().Add(review);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Update COURSE denormalized rating stats
        var reviews = await dbContext.CourseReviews()
            .Where(r => r.CourseId == courseId && r.IsPublished)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var avgRating = reviews.Count > 0
            ? Math.Round((decimal)reviews.Average(r => r.Rating), 1)
            : 0m;

        course.UpdateRatingStats(avgRating, reviews.Count);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (_, userName) = await userContactReader.GetUserContactInfoAsync(userId, cancellationToken).ConfigureAwait(false);

        return Result.Success(new CourseReviewDto(
            review.Id,
            review.CourseId,
            review.UserId,
            userName ?? "ผู้เรียน",
            review.Rating,
            review.Comment,
            review.CreatedAtUtc));
    }
}
