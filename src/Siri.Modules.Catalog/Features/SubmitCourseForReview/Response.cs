using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.SubmitCourseForReview;

public sealed record SubmitCourseForReviewResponse(Guid Id, CourseStatus Status);
