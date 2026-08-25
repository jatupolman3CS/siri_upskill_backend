using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.RejectInstructorApplication;

public sealed record RejectInstructorApplicationResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    InstructorApplicationStatus Status);
