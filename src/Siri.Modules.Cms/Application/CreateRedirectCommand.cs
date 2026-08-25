namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/redirects. Binds from the JSON request body. All three fields
/// are structurally required — <see cref="Domain.REDIRECT"/>'s own doc comment already notes there is
/// nothing left over for a future Update to own, which is why this module has no
/// <c>UpdateRedirectCommand</c> at all (unlike Banner/MenuItem/Post): changing any field of an existing
/// redirect rule is a delete-and-recreate, not an edit — see <see cref="RedirectService"/>'s own doc
/// comment.
/// </summary>
public sealed record CreateRedirectCommand(string FromPath, string ToPath, int StatusCode);
