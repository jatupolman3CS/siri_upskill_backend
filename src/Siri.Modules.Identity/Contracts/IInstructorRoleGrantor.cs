namespace Siri.Modules.Identity.Contracts;

/// <summary>
/// The Identity module's public surface for granting the platform's fixed "Instructor" role to an
/// account — the only way another module may reach this module's Roles/UserRoles data (backend.md/
/// ARCHITECTURE.md §2: "Module A เรียก Module B ผ่าน Contracts/ เท่านั้น ห้าม reference Domain/
/// Infrastructure ของ module อื่น"). First real consumer: Catalog's ApproveInstructorApplication handler
/// (task P1-03) — approving an instructor application must grant the role
/// <c>Siri.SharedKernel.AuthorizationPolicyNames.InstructorOnly</c>-gated endpoints check for, but
/// Roles/UserRoles belong to this module, not Catalog's InstructorProfile.
/// <para>
/// Deliberately narrow — "grant Instructor, specifically" rather than a general "grant any role to any
/// user" surface (backend.md: "ห้ามสร้างชั้น IXxxService ที่มี method รวมทุกอย่าง (god service)"). No
/// other module has a legitimate reason to assign Admin/SuperAdmin from outside this module.
/// </para>
/// </summary>
public interface IInstructorRoleGrantor
{
    /// <summary>
    /// Stages granting the Instructor role to <paramref name="userId"/> on the ambient, request-scoped
    /// <c>AppDbContext</c> — this method does <b>not</b> call <c>SaveChangesAsync</c> itself, the same
    /// "caller controls the transaction boundary" contract <c>Notification.Contracts.IEmailOutbox
    /// .Enqueue</c> already established, so the grant commits atomically together with whatever else the
    /// calling handler is writing in the same request (e.g. Catalog's <c>InstructorProfile.Approve</c>) —
    /// database.md: "การเปลี่ยนแปลงหลายตารางที่ต้อง atomic ... ต้องอยู่ใน transaction เดียว". Both this
    /// module's Infrastructure and the calling module resolve the exact same scoped <c>AppDbContext</c>
    /// instance from DI within one HTTP request, so this composes correctly without either module needing
    /// to know about the other's entities.
    /// <para>
    /// Idempotent — calling this for a user who already holds the role is a safe no-op
    /// (<c>User.AssignRole</c>'s own contract).
    /// </para>
    /// <para>
    /// A caller's own next login/refresh is what actually surfaces this — access tokens are minted with
    /// a point-in-time snapshot of role claims, so an already-issued token does not retroactively gain
    /// the Instructor claim until it is renewed (up to ~15 minutes, or immediately on next login).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="userId"/> does not name an existing
    /// user, or the Instructor role row itself is missing from <c>identity.Roles</c> — both would mean a
    /// real environment/data problem, not something to silently work around (every real caller already
    /// has a trustworthy user id, e.g. an authenticated <c>IUserContext.UserId</c> at some point in the
    /// past).</exception>
    Task GrantAsync(Guid userId, CancellationToken cancellationToken);
}
