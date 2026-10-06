namespace Siri.Modules.Catalog.Domain;

/// <summary>docs/HYBRID_LIVE.md §1.1 — Live/Hybrid ต้องมี COURSE_LIVE_SESSION อย่างน้อย 1 คาบก่อนจะ publish ได้
/// (ดู COURSE.Publish's ตัว invariant ที่แก้แล้ว) OnDemand คือค่าเดิมของทุกคอร์สก่อนหน้านี้ทั้งหมด.</summary>
public enum DeliveryFormat
{
    OnDemand,
    Live,
    Hybrid,
}
