namespace Siri.Modules.Catalog.Contracts;

/// <summary>แจ้ง Siri.Modules.Live (ยังไม่มีอยู่จริง — P11-03) ว่า live session ถูกสร้าง/แก้/ยกเลิก เพื่อ stage
/// แถว LIVE.SESSION_MEETINGS บน AppDbContext เดียวกัน — pattern เดียวกับ Identity.Contracts
/// .IInstructorRoleGrantor (P1-03): stage ไม่ save เอง, ผู้เรียก (Catalog's handler เอง — P11-02) เป็นคน
/// SaveChangesAsync ครั้งเดียวพร้อมกับการเปลี่ยนแปลงบน COURSE_LIVE_SESSION.
/// <para>
/// **ห้าม implementation เรียก SaveChangesAsync เอง** — ผิด contract นี้ทันที (จะทำให้เกิด 2 transaction
/// แยกที่ partial-commit ได้ถ้าอย่างใดอย่างหนึ่ง fail).
/// </para>
/// <para>
/// เมธอดรับแค่ sessionId เพราะ implementation ที่แท้จริง (P11-03) ไม่ต้องรู้ Title/เวลา ตอน stage —
/// Hangfire job live-meeting-sync (ทำงาน async แยกทีหลัง ไม่ใช่ synchronous ภายใน handler นี้) เป็นคนอ่าน
/// รายละเอียดจริงผ่าน ILiveScheduleReader.GetSessionAsync ตอนถึงคิวประมวลผล — กัน staleness ถ้า session ถูก
/// แก้อีกรอบก่อน job จะรันจริง.
/// </para></summary>
public interface ILiveMeetingSink
{
    Task OnSessionScheduledAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>เรียกหลัง COURSE.UpdateLiveSession สำเร็จ — P11-03's implementation มีหน้าที่ bump
    /// SESSION_MEETINGS.SEQUENCE + ตั้งกลับเป็น Pending ให้ live-meeting-sync ไป patch Google Calendar event
    /// ใหม่ (docs/HYBRID_LIVE.md §2.1).</summary>
    Task OnSessionChangedAsync(Guid sessionId, CancellationToken cancellationToken);

    Task OnSessionCancelledAsync(Guid sessionId, CancellationToken cancellationToken);
}
