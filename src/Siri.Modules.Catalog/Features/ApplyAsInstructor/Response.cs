using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.ApplyAsInstructor;

public sealed record ApplyAsInstructorResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? Headline,
    string Bio,
    decimal RevenueSharePercent,
    InstructorApplicationStatus Status,
    DateTime? ApprovedAtUtc);
