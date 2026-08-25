using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.GetMyInstructorProfile;

public sealed record InstructorProfileResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? Headline,
    string Bio,
    decimal RevenueSharePercent,
    InstructorApplicationStatus Status,
    DateTime? ApprovedAtUtc);
