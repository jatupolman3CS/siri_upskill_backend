namespace Siri.SharedKernel;

/// <summary>
/// The current authenticated request's identity. Handlers must read the acting user from this
/// abstraction — never accept a <c>userId</c> from the request body (.claude/rules/backend.md:
/// "อ่าน user ปัจจุบันจาก IUserContext เท่านั้น ห้ามรับ userId มาจาก request body").
/// Interface only for now; the real implementation (reading the JWT principal from
/// <c>HttpContext</c>) ships with the Identity module.
/// </summary>
public interface IUserContext
{
    /// <summary>Id of the current user, or <c>null</c> when the request is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>Roles assigned to the current user. Empty when anonymous.</summary>
    IReadOnlyCollection<string> Roles { get; }

    bool IsAuthenticated { get; }
}
