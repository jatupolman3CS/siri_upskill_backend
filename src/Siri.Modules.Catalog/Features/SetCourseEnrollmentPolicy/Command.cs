namespace Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

public sealed record SetCourseEnrollmentPolicyCommand(DateTime? EnrollmentDeadlineUtc, int? MaxSeats);
