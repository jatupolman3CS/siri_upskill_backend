namespace Siri.Modules.Catalog.Features.ApplyAsInstructor;

/// <summary>Request payload for POST /api/catalog/instructors/apply. Binds from the JSON request body —
/// deliberately carries no <c>UserId</c> field: the caller is always the authenticated
/// <c>IUserContext.UserId</c> (security.md: "userId มาจาก IUserContext เท่านั้น ห้ามรับจาก body/query"),
/// threaded into <see cref="ApplyAsInstructorHandler"/>'s <c>HandleAsync</c> as a separate parameter —
/// the same split <c>UpdateCategoryHandler</c>/<c>DeleteCategoryHandler</c> already use for their
/// route-bound id.</summary>
public sealed record ApplyAsInstructorCommand(string DisplayName, string? Headline, string Bio);
