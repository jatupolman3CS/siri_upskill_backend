namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// An iCalendar (RFC 5545/5546) document to travel with an outbound email as a <c>text/calendar</c> MIME part
/// (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md §3.1). <paramref name="Method"/> is the
/// iTIP method — exactly <c>REQUEST</c>, <c>CANCEL</c> or <c>PUBLISH</c> (upper case); <paramref name="IcsContent"/>
/// is the complete document and must start with <c>BEGIN:VCALENDAR</c>. Both are validated when the outbox row is
/// created (<c>EMAIL_OUTBOX_MESSAGE.Enqueue</c>) — a bad value throws there, at the caller, not later in the sender.
/// <para>
/// Whoever builds <paramref name="IcsContent"/> owns its content: it must carry the same <c>METHOD:</c> as
/// <paramref name="Method"/>, escape every free-text value, and must never contain a meeting-room URL
/// (docs/contracts/P11-04 §4.1). The outbox stores what it is given and does not rewrite it.
/// </para>
/// </summary>
public sealed record EmailCalendarPart(string Method, string IcsContent);

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

    /// <summary>
    /// Same as the four-argument overload, optionally attaching an iCalendar part to the email (P11-04).
    /// Like the other overload it stages on the ambient <c>AppDbContext</c> and does <b>not</b> save.
    /// <para>
    /// Has a default body so that every existing <see cref="IEmailOutbox"/> implementer/fake keeps compiling
    /// and keeps working for plain emails: <paramref name="calendar"/> <c>null</c> falls through to the
    /// four-argument overload; a non-null part throws <see cref="NotSupportedException"/> on an implementation
    /// that does not override this method (silently dropping an invite's calendar would be worse than failing).
    /// The real <c>EmailOutbox</c> overrides it.
    /// </para>
    /// </summary>
    void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey, EmailCalendarPart? calendar)
    {
        if (calendar is not null)
        {
            throw new NotSupportedException("This IEmailOutbox implementation does not support calendar parts.");
        }

        Enqueue(toEmail, subject, bodyHtml, templateKey);
    }
}
