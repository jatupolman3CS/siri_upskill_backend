namespace Siri.Modules.Learning.Application;

/// <summary>Request payload for POST /api/learning/instructor/assignments. Deliberately carries no
/// caller/instructor id — same "resolved from IUserContext, never accepted from the client" reasoning
/// as <see cref="CreateQuizRequest"/>'s own doc comment.</summary>
public sealed record CreateAssignmentRequest(
    Guid EpisodeId,
    string Title,
    string Instructions,
    int? DueDays,
    int MaxFileSizeMb,
    string AllowedExtensions);

/// <summary>Request payload for PUT /api/learning/instructor/assignments/{id}.</summary>
public sealed record UpdateAssignmentRequest(
    string Title,
    string Instructions,
    int? DueDays,
    int MaxFileSizeMb,
    string AllowedExtensions);

public sealed record AssignmentResponse(
    Guid Id,
    Guid EpisodeId,
    string Title,
    string Instructions,
    int? DueDays,
    int MaxFileSizeMb,
    string AllowedExtensions);
