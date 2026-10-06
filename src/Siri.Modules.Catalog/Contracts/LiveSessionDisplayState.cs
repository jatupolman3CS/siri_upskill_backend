namespace Siri.Modules.Catalog.Contracts;

public enum LiveSessionStatus
{
    Scheduled,
    Cancelled,
}

/// <summary>docs/HYBRID_LIVE.md §1.2's state diagram, คำนวณเสมอตอน response ไม่เก็บลง DB.</summary>
public enum LiveSessionDisplayState
{
    Cancelled,
    Upcoming,
    Live,
    Ended,
}

/// <summary>
/// Pure computation shared by every consumer that needs to render a live session's current display
/// state — deliberately in <c>Catalog.Contracts</c>, not <c>Catalog.Domain</c>: a future
/// <c>Siri.Modules.Live</c> (task P11-03/05) can reference <c>Catalog.Contracts</c> (it already does, to
/// implement <see cref="ILiveMeetingSink"/> and consume <see cref="ILiveScheduleReader"/>) but not
/// <c>Catalog.Domain</c> (blocked by <c>Siri.ArchitectureTests</c>' module-boundary rule). Both P11-07's
/// public course-detail read model (this module, today) and P11-05's future instructor
/// <c>/join</c>/<c>my-sessions</c> endpoints must compute the exact same state for the exact same
/// session, or the two surfaces will visibly disagree at the edges (e.g. an off-by-one around
/// <c>StartsAtUtc - 15m</c>) — implementing this twice risks exactly that kind of drift.
/// </summary>
public static class LiveSessionDisplayStateCalculator
{
    /// <summary>docs/HYBRID_LIVE.md §1.2: "หน้าต่างเข้าห้องเริ่มก่อน 15 นาที" — ค่าจริงจะมาจาก config
    /// Live:JoinWindowBeforeMinutes เมื่อ Live module มี Options ของตัวเอง (P11-03/05); P11-01/07 ยังไม่มี
    /// โมดูล Live ให้ผูก config จริง จึงใช้ constant นี้ตรง ๆ ไปก่อน.</summary>
    public const int DefaultJoinWindowBeforeMinutes = 15;

    /// <param name="nowUtc">ต้องเป็น DateTimeKind.Utc เสมอ (มาจาก IClock.UtcNow) — ฟังก์ชันนี้เป็น pure
    /// function ไม่ throw ถ้า Kind ผิด (caller รับผิดชอบเอง) ต่างจาก domain method ที่ throw ตรง ๆ เพราะ
    /// ฟังก์ชันนี้ไม่ได้เขียนอะไรลง DB ไม่มี Npgsql ให้ throw แทน.</param>
    public static LiveSessionDisplayState Compute(
        LiveSessionStatus status,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime nowUtc,
        int joinWindowBeforeMinutes = DefaultJoinWindowBeforeMinutes)
    {
        if (status == LiveSessionStatus.Cancelled)
        {
            return LiveSessionDisplayState.Cancelled;
        }

        if (nowUtc > endsAtUtc)
        {
            return LiveSessionDisplayState.Ended;
        }

        return nowUtc >= startsAtUtc.AddMinutes(-joinWindowBeforeMinutes)
            ? LiveSessionDisplayState.Live
            : LiveSessionDisplayState.Upcoming;
    }
}
