namespace Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

public sealed record SetCourseEnrollmentPolicyResponse(Guid Id, DateTime? EnrollmentDeadlineUtc, int? MaxSeats, int SeatsUsed);
