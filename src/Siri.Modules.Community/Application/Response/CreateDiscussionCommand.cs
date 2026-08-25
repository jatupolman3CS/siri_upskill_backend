namespace Siri.Modules.Community.Application.Response;

/// <summary>Request payload for POST /api/community/discussions. <c>ParentId</c> null = a new top-level
/// post, set = a threaded reply. Deliberately carries no <c>UserId</c> — resolved from the caller's own
/// <c>IUserContext.UserId</c>, never accepted from the client (.claude/rules/backend.md: "userId มาจาก
/// IUserContext เท่านั้น ห้ามรับ userId มาจาก request body").</summary>
public sealed record CreateDiscussionCommand(Guid CourseId, Guid? EpisodeId, Guid? ParentId, string Body);
