namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// The Notification module's public surface for queuing an outbound email — the only way another
/// module may reach this module's outbox (backend.md/ARCHITECTURE.md §2: "Module A เรียก Module B
/// ผ่าน Contracts/ เท่านั้น ห้าม reference Domain/Infrastructure ของ module อื่น"). Wraps
/// <c>Domain.EMAIL_OUTBOX_MESSAGE.Enqueue</c> without exposing that Domain type — or the fact that an
/// outbox table backs it at all — to callers outside this module.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>
    /// Stages a new email on the ambient, request-scoped <c>AppDbContext</c> — this method does
    /// <b>not</b> call <c>SaveChangesAsync</c> itself. The caller controls the transaction boundary,
    /// typically so the queued email commits atomically together with whatever else the caller's own
    /// handler is writing in the same request (database.md: "การเปลี่ยนแปลงหลายตารางที่ต้อง atomic
    /// ... ต้องอยู่ใน transaction เดียว"). Both this module's Infrastructure and the calling module
    /// resolve the exact same scoped <c>AppDbContext</c> instance from DI within one HTTP request, so
    /// this composes correctly without either module needing to know about the other's entities.
    /// </summary>
    void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey);
}
