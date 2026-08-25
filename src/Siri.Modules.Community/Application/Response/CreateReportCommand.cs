namespace Siri.Modules.Community.Application.Response;

/// <summary>Request payload for POST /api/community/reports. Deliberately carries no
/// <c>ReportedByUserId</c> — resolved from the caller's own <c>IUserContext.UserId</c>, same reasoning as
/// <c>CreateDiscussionCommand</c>'s own doc comment.</summary>
public sealed record CreateReportCommand(Guid DiscussionId, string Reason);
