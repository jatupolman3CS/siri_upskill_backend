using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.ApproveInstructorApplication;

public sealed record ApproveInstructorApplicationResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    InstructorApplicationStatus Status,
    DateTime? ApprovedAtUtc);
