# Contract: P11-01 Catalog — `DeliveryFormat` + live-session domain

Status: FROZEN · วันที่: 2026-09-16 · Module: `Siri.Modules.Catalog` (vertical slice, `Features/{UseCase}` handlers) — **แต่ entity class เป็น UPPERCASE** (`COURSE`, `COURSE_SECTION`, ...) พร้อม C# property เป็น PascalCase ปกติ ดูหมายเหตุสำคัญด้านล่างก่อนเริ่ม

ผู้ลงมือที่แนะนำ: **backend-developer (Claude)** — งานนี้แก้ invariant ของ `COURSE.Publish()`/`SubmitForReview()` (business rule ที่พลาดแล้วกระทบทั้งระบบ — คอร์ส Live ที่ publish ได้โดยไม่มีตารางสอนจริง) และเพิ่ม `Contracts/` interface ข้ามโมดูลใหม่ 2 ตัว (`ILiveScheduleReader`, `ILiveMeetingSink`) ตรงตามเกณฑ์ charter ของ system-architect ที่ต้องส่งให้ Claude ไม่ใช่ Antigravity — ตรงกับ `docs/HYBRID_LIVE.md` §8 และ `docs/TASKS.md`'s P11-01 row (`Own: CC`) อยู่แล้ว

**DESIGN_DATABASE: ไม่จำเป็น** — schema delta มีตารางใหม่ 1 ตัวเท่านั้น (`COURSE_LIVE_SESSIONS`), FK strategy ทั้งสองเส้น (Cascade ไป `COURSES`, NoAction ไป `COURSE_EPISODES`) เป็น pattern ที่มี precedent ตรงในโค้ดเบสนี้อยู่แล้ว (`COURSE_SECTION`→`COURSE` Cascade, `COURSE.TrailerMediaAssetId`/`InstructorId` NoAction) — §2 ด้านล่างละเอียดระดับคอลัมน์/index/FK พร้อมเหตุผลแล้ว ให้ backend-developer เขียน entity+configuration+migration ต่อจาก §2 ได้ทันทีโดยไม่ต้องผ่าน DESIGN_DATABASE/DATABASE

---

## ⚠️ แก้ข้อสมมติในคำสั่งงาน — อ่านก่อนเริ่ม

คำสั่งงานที่ระบุมาบอกว่า **"Catalog = vertical slice, PascalCase — NOT UPPERCASE"** อ้างอิง `.claude/rules/backend.md` — ข้อความนี้ยังถูกครึ่งเดียว ผมอ่าน `src/Siri.Modules.Catalog/Domain/COURSE.cs` (ของจริง ไม่ใช่ doc comment) แล้วพบว่า:

- **Business logic pattern** (1 handler ต่อ 1 use case, `Features/{UseCase}/{Command,Validator,Handler,Endpoint,Response}.cs`) — ยังเป็น vertical slice ตรงตาม backend.md จริง
- **แต่ entity class name เป็น UPPERCASE แล้ว**: `COURSE`, `COURSE_SECTION`, `COURSE_EPISODE`, `COURSE_OUTCOME`, `COURSE_REQUIREMENT`, `COURSE_REVIEW`, `CATEGORY`, `INSTRUCTOR_PROFILE`, `EPISODE_ATTACHMENT`, `LEARNING_PATH*`, `WISHLIST_ITEM` — ยืนยันจากไฟล์จริงใน `src/Siri.Modules.Catalog/Domain/`
- **C# property ยังเป็น PascalCase ปกติ** (`Title`, `InstructorId`, `StartsAtUtc` ฯลฯ) — ไม่ใช่ `UPPER_SNAKE_CASE` แบบ 7 โมดูลใหม่ (Commerce/Media/...)
- `.claude/rules/database.md` **ถูกอัปเดตแล้วจริง** (2026-08-29): "ทุก schema (`CATALOG`, `IDENTITY`, ...) ... เป็น UPPERCASE / UPPER_SNAKE_CASE ทั้งหมด" — DB table/column ของ Catalog เป็น UPPERCASE เหมือนกันหมดแล้ว ผ่าน `ApplyUppercaseNamingConventions` convention ที่แปลงชื่อ C# PascalCase property ให้เองอัตโนมัติตอน map เป็น DB column (ไม่ต้องตั้งชื่อ property เป็น UPPER_SNAKE_CASE เอง ต่างจาก 7 โมดูลใหม่ที่ตั้งใจให้ตรงกัน 1:1)
- `docs/DATABASE.md`'s P11–P12 sketch section เขียนกำกับไว้ตรงกันแล้ว: **"Catalog = PascalCase property/ตาราง UPPERCASE ตาม convention เดิมของโมดูล"**

**สรุปสำหรับ P11-01**: entity class ใหม่ต้องชื่อ `COURSE_LIVE_SESSION` (เอกพจน์ ตรง `COURSE_SECTION`/`COURSE_EPISODE`) table `CATALOG.COURSE_LIVE_SESSIONS`, C# property เป็น PascalCase ปกติ (`Title`, `StartsAtUtc`, ...) — ไม่ใช่ PascalCase-ทั้งคลาส-ทั้ง-property แบบที่คำสั่งงานบอก และไม่ใช่ UPPER_SNAKE_CASE-ทั้ง-property แบบ 7 โมดูลใหม่ เป็น pattern ที่สามอยู่ตรงกลาง — **สับสนได้ง่ายมาก ต้องอ่าน `COURSE.cs`/`COURSE_SECTION.cs` ของจริงก่อนเขียนบรรทัดแรกเสมอ**

`.claude/rules/backend.md`'s endpoint section ก็เปลี่ยนไปแล้วด้วย (D-19, 2026-09-01): **MVC Controllers (attribute routing)** แทน Minimal API `MapGroup()` เดิม — แต่ทุก endpoint ใหม่ยังต้องมี minimal-API mapper คู่กันไว้ให้ integration test harness เดิมครอบ (ดู §3 ท้ายไฟล์)

---

## 1. Scope & task IDs

**อยู่ในขอบเขต (P11-01):**
- `COURSE.DeliveryFormat` ใหม่ + entity `COURSE_LIVE_SESSION` ใหม่ (child aggregate ของ `COURSE`)
- Domain method: `COURSE.SetDeliveryFormat`, `AddLiveSession`, `UpdateLiveSession`, `CancelLiveSession`, `AttachSessionRecording`
- แก้ invariant ของ `COURSE.Publish()`/`SubmitForReview()` ให้รองรับ Live/Hybrid (ดู §2.4)
- `Catalog.Contracts.ILiveScheduleReader` (interface + implementation ใน Catalog) + `Catalog.Contracts.ILiveMeetingSink` (interface ประกาศเท่านั้น, implement โดย module `Siri.Modules.Live` ในอนาคต — P11-03) + `NullLiveMeetingSink` default
- `Catalog.Contracts.LiveSessionDisplayStateCalculator` (pure utility, ดู §2.5) — ใช้ร่วมกันโดย P11-07 (Catalog เอง) และ P11-05 (Live module ในอนาคต อ้างอิง `Catalog.Contracts` ได้อยู่แล้วเพราะ implement `ILiveMeetingSink`/consume `ILiveScheduleReader` จากที่นั่น)
- Migration `AddCourseDeliveryFormatAndLiveSessions`
- Unit test ทุก branch ของ invariant ใหม่ (§2.6 มีรายการ)

**นอกขอบเขต (ห้ามทำใน P11-01 นี้ — งานของ task อื่น):**
- HTTP endpoint/controller/handler สำหรับ live-session CRUD (§3 ด้านล่างคือ**ข้อกำหนด**ให้ P11-02 ทำ ไม่ใช่ให้ P11-01 ทำ)
- Read model (`CourseDetailResponse`/`SearchCourses`) เพิ่ม `deliveryFormat`/`liveSchedule`/`format` filter (§4 คือข้อกำหนดให้ P11-07 ทำ)
- `AI_ENRICHMENT_ENABLED`, `GOOGLE_ATTENDEE_SYNC_ENABLED` บน `COURSES` — `docs/DATABASE.md`'s sketch แนะนำใส่ใน migration เดียวกันเพื่อความสะดวก แต่ทั้งสองคอลัมน์นี้เป็น opt-in flag ของฟีเจอร์ที่ยังบล็อกอยู่ (P12-01 ติด Q12, P11-04 ติด Q11) — **ตัดสินใจไม่รวมเข้ามาใน P11-01** เพื่อไม่ให้ schema ของฟีเจอร์ที่ยังไม่ผ่านคำถามธุรกิจถูกสร้างล่วงหน้า (workflow.md rule 2: เช็ค blocking decision ก่อน) แต่ละ task (P11-04, P12-01) จะเพิ่มคอลัมน์ของตัวเองด้วย migration แยกตอนถูกปลดบล็อก
- `Siri.Modules.Live` module เอง, `Siri.Integrations.Google`, Hangfire job `live-meeting-sync` — ทั้งหมดคือ P11-03 (ติด Q10, สถานะ `BLOCK`)
- ILiveMeetingSink's real implementation — Catalog ได้แค่ `NullLiveMeetingSink`
- `Live:JoinWindowBeforeMinutes` เป็น Options/config ตัวจริง — P11-01 ใส่แค่ `const int DefaultJoinWindowBeforeMinutes = 15` ไว้ให้เรียกใช้ทันที (ดู §2.5), ผูกเป็น config จริงเมื่อ Live module มี (P11-03/05)

---

## 2. Schema delta

### 2.1 `CATALOG.COURSES` — คอลัมน์ใหม่ 1 ตัว

| Column | Type | Nullable | Default | หมายเหตุ |
|---|---|---|---|---|
| `DELIVERY_FORMAT` | `varchar(20)` | NOT NULL | `'OnDemand'` | string enum ผ่าน `.HasConversion<string>().HasMaxLength(20)` — pattern เดียวกับ `Level`/`Language`/`Status` ใน `CourseConfiguration.cs` เป๊ะ |

C# property ใหม่บน `COURSE`:
```csharp
public DeliveryFormat DeliveryFormat { get; private set; } = DeliveryFormat.OnDemand;
```
ค่า default ตรงกับ enum's ตัวแรก (`OnDemand = 0`) ทั้งสองชั้น (C# default + DB `DEFAULT 'OnDemand'`) — แถวเดิมทุกแถวไม่ถูกแตะ ไม่ต้อง data-migration script

Enum ใหม่ `src/Siri.Modules.Catalog/Domain/DeliveryFormat.cs` (มิเรอร์ `CourseStatus.cs`'s รูปแบบไฟล์เป๊ะ):
```csharp
namespace Siri.Modules.Catalog.Domain;

/// <summary>docs/HYBRID_LIVE.md §1.1 — Live/Hybrid ต้องมี COURSE_LIVE_SESSION อย่างน้อย 1 คาบก่อนจะ publish ได้
/// (ดู COURSE.Publish's ตัว invariant ที่แก้แล้ว) OnDemand คือค่าเดิมของทุกคอร์สก่อนหน้านี้ทั้งหมด.</summary>
public enum DeliveryFormat
{
    OnDemand,
    Live,
    Hybrid,
}
```

### 2.2 `CATALOG.COURSE_LIVE_SESSIONS` — ตารางใหม่

```
COURSE_LIVE_SESSIONS
  Id                  uuid PK (UuidV7.NewId())
  CourseId            uuid NOT NULL  FK → COURSES.Id  ON DELETE CASCADE
  Title               varchar(200) NOT NULL
  Description         text NULL
  StartsAtUtc         timestamptz(3) NOT NULL
  EndsAtUtc           timestamptz(3) NOT NULL
  SortOrder           int NOT NULL
  Status              varchar(20) NOT NULL           -- 'Scheduled' | 'Cancelled'
  CancelReason        varchar(500) NULL
  RecordingEpisodeId  uuid NULL      FK → COURSE_EPISODES.Id  ON DELETE NO ACTION
  RowVersion          bytea NOT NULL (concurrency token, ConcurrencyTokenInterceptor)
  CreatedAtUtc        timestamptz(3) NOT NULL
  CreatedBy           uuid NULL
  UpdatedAtUtc        timestamptz(3) NULL
  UpdatedBy           uuid NULL
```

**C# entity** `src/Siri.Modules.Catalog/Domain/COURSE_LIVE_SESSION.cs` — `sealed class COURSE_LIVE_SESSION : IAuditable` (ไม่ implement `ISoftDelete` — เหตุผลเดียวกับ `COURSE_SECTION`: lifecycle ของ session คือ Scheduled→Cancelled ผ่าน status ไม่ใช่ soft-delete flag, และ `docs/DATABASE.md`'s soft-delete table list ไม่มีตารางนี้). Property ทั้งหมดเป็น PascalCase มาตรฐาน 1:1 กับตารางข้างบน. Constructor `private`, factory `internal static Create(...)` เรียกได้เฉพาะจาก `COURSE.AddLiveSession` (composition เดียวกับ `COURSE_SECTION`/`COURSE_EPISODE` — session ที่ไม่มี course เจ้าของไม่มีความหมาย) — mutator method ทั้งหมด (`Reschedule`/`Cancel`/`AttachRecording`) เป็น `internal`, เรียกได้เฉพาะจาก `COURSE`'s เอง method ที่ตรงกัน (§2.3)

**FK reasoning:**
- `CourseId → COURSES` = **Cascade** — genuine composition เหมือน `COURSE_SECTION`/`COURSE_OUTCOME`/`COURSE_REQUIREMENT` ทุกตัว: session ที่ไม่มี course เจ้าของไม่มีความหมาย, course ถูก hard-delete (ข้าม soft-delete interceptor) ต้องพา session ไปด้วย — **ตรวจแล้วว่าไม่มี multiple-cascade-path ชนกัน**: `COURSE_LIVE_SESSIONS` มี FK ออกแค่ 2 เส้น (`CourseId` Cascade, `RecordingEpisodeId` NoAction) ไม่เหมือน `COURSE_EPISODES` ที่เคยมี FK คู่ไป `COURSES` (ผ่าน `SectionId` Cascade + `CourseId` NoAction) — ที่นี่มีแค่เส้นเดียวไป `COURSES` เลยไม่มีความเสี่ยง "multiple cascade paths" แบบที่ P1-02's history เจอมาก่อน
- `RecordingEpisodeId → COURSE_EPISODES` = **NoAction, nullable** — คนละ aggregate โดยธรรมชาติ (episode ถูกลบ/ย้ายผ่าน `COURSE.RemoveEpisode` ซึ่งบล็อกอยู่แล้วถ้าคอร์ส Published/Archived — แต่ course ยังเป็น Draft ได้ระหว่างที่มี live session ที่มี recording แนบ, NoAction ป้องกันไม่ให้ episode ถูกลบแล้วพา session หายไปด้วยเงียบ ๆ) — **ไม่ต้อง data migration พิเศษ**: คอลัมน์นี้ nullable, แถวใหม่ทุกแถวเริ่ม null เสมอ (set ทีหลังผ่าน `AttachSessionRecording`, P11-06)

**Index:**
```csharp
builder.HasIndex(s => new { s.CourseId, s.StartsAtUtc });                     // ordered schedule per course (public read model, builder list)
builder.HasIndex(s => s.StartsAtUtc).HasFilter("\"STATUS\" = 'Scheduled'");   // job หา upcoming sessions ข้ามคอร์ส (P11-03/04's live-meeting-sync, live-invite-reconcile — ยังไม่มีวันนี้ แต่ ILiveScheduleReader.GetUpcomingSessionsAsync ใช้ index นี้ได้ทันที)
```
Filtered index ใช้ syntax PostgreSQL ตาม database.md ("`\"IS_DELETED\" = false` ไม่ใช่ `[IS_DELETED] = 0`") — quote คอลัมน์ `STATUS` (ชื่อจริงหลัง `ApplyUppercaseNamingConventions` แปลง) และค่า enum string ตรงตัว `'Scheduled'` (ไม่ quote เป็น identifier เพราะเป็น value ไม่ใช่ identifier)

**ตรวจ identifier length ก่อน commit**: `COURSE_LIVE_SESSIONS`/คอลัมน์ทั้งหมด/FK constraint name auto-gen — รันแล้วเช็ค `Siri.ArchitectureTests/DatabaseIdentifierLengthTests` ถ้าแดงให้ตั้งชื่อสั้นลงด้วย `HasDatabaseName()`/`HasConstraintName()` ตรง ๆ (ห้าม truncate อัตโนมัติ) — คาดว่าจะผ่านเพราะชื่อทั้งหมดสั้นกว่า `INSTRUCTOR_PROFILES`/`STRIPE_WEBHOOK_EVENTS` ที่ผ่านมาแล้ว แต่ต้องรันจริงยืนยัน ห้ามเดา

**Migration ชื่อ**: `AddCourseDeliveryFormatAndLiveSessions` (ตรงกับที่ `docs/TASKS.md`/`docs/DATABASE.md` ระบุไว้แล้ว) — additive ล้วน (1 `AddColumn` + 1 `CreateTable`), ไม่มี data migration script ที่ต้องเขียนคู่ (ไม่มีความเสี่ยงข้อมูลหาย)

### 2.3 `COURSE` — property/method ใหม่

```csharp
private readonly List<COURSE_LIVE_SESSION> _liveSessions = [];

public IReadOnlyCollection<COURSE_LIVE_SESSION> LiveSessions => _liveSessions.AsReadOnly();

/// <summary>เปลี่ยนรูปแบบการส่งมอบคอร์ส. ปฏิเสธถ้าคอร์สเป็น Archived (เหตุผลเดียวกับ RemoveSection/RemoveEpisode's
/// สถานะที่ห้ามแตะ — แต่ต่างตรงที่ *ไม่* บล็อก Published: ต่างจาก section/episode structure ที่แก้ post-publish
/// จะทำลาย progress ของผู้เรียน, การเปลี่ยน DeliveryFormat ของคอร์ส Live/Hybrid ที่กำลังสอนอยู่จริง (เพิ่ม session
/// รายสัปดาห์ต่อเนื่อง) เป็น flow ปกติที่ต้องรองรับ — ไม่ใช่ edge case) ปฏิเสธถ้าจะเปลี่ยนกลับเป็น OnDemand ทั้งที่ยังมี
/// session Scheduled ค้างอยู่ (ต้อง CancelLiveSession ทุกคาบก่อน — ป้องกัน session ที่ไม่มีความหมายค้างอยู่บนคอร์ส
/// ที่ประกาศตัวเองว่าไม่มีตารางสอนแล้ว) ไม่ re-validate Publish invariant ย้อนหลัง (ดู §2.4's หมายเหตุ "ทำไมไม่
/// re-check ตอนเปลี่ยน format").</summary>
public void SetDeliveryFormat(DeliveryFormat format)
{
    if (Status == CourseStatus.Archived)
    {
        throw new InvalidOperationException($"Cannot change delivery format of a course in {Status} status.");
    }

    if (format == DeliveryFormat.OnDemand && _liveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled))
    {
        throw new InvalidOperationException("Cannot switch to OnDemand while scheduled live sessions exist — cancel them first.");
    }

    DeliveryFormat = format;
}

/// <summary>เพิ่มคาบสอนสดใหม่. SortOrder คำนวณเองจาก _liveSessions.Count (pattern เดียวกับ AddSection/AddEpisode)
/// แต่ใช้จริงแค่เป็น tiebreaker — การเรียงลำดับหลักที่ทุก read model ควรใช้คือ StartsAtUtc ไม่ใช่ SortOrder
/// (ต่างจาก section/episode ที่ SortOrder คือลำดับที่ผู้สอนจงใจจัดเอง ตารางสอนสดเรียงตามเวลาธรรมชาติอยู่แล้ว
/// ไม่มี endpoint ให้ reorder เอง).</summary>
public COURSE_LIVE_SESSION AddLiveSession(string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc, IClock clock)
{
    ArgumentNullException.ThrowIfNull(clock);

    if (Status == CourseStatus.Archived)
    {
        throw new InvalidOperationException($"Cannot add a live session to a course in {Status} status.");
    }

    if (DeliveryFormat == DeliveryFormat.OnDemand)
    {
        throw new InvalidOperationException("Cannot add a live session to an OnDemand course — change DeliveryFormat first.");
    }

    ValidateSessionWindow(startsAtUtc, endsAtUtc, excludingSessionId: null);

    if (startsAtUtc <= clock.UtcNow)
    {
        throw new ArgumentOutOfRangeException(nameof(startsAtUtc), startsAtUtc, "A new live session must start in the future.");
    }

    var session = COURSE_LIVE_SESSION.Create(Id, title, description, startsAtUtc, endsAtUtc, _liveSessions.Count);
    _liveSessions.Add(session);
    return session;
}

/// <summary>แก้เวลา/ชื่อ/คำอธิบายของคาบที่มีอยู่ (reschedule). ต่างจาก AddLiveSession ตรงที่ *ไม่* บังคับว่า
/// startsAtUtc ใหม่ต้องเป็นอนาคต — คาบที่กำลังสอนสดอยู่จริง (StartsAtUtc ผ่านไปแล้วแต่ EndsAtUtc ยังไม่ถึง)
/// ต้องแก้ EndsAtUtc ให้ยาวขึ้นได้ (เช่น "ต่อเวลาอีก 30 นาที") — ธุรกิจต้องการ flow นี้จริงตาม
/// docs/HYBRID_LIVE.md §1.2's "Live" display state ที่ต้อง toggle ปุ่ม "เข้าห้อง" ตามเวลาจริง ไม่ใช่ตาม
/// สมมติฐานว่าคาบที่กำลังสอนอยู่แก้ไม่ได้. Guard เดียวที่มีคือ "คาบที่ EndsAtUtc ผ่านไปแล้วห้ามแก้" (ตาม task
/// spec ตรง ๆ).</summary>
public void UpdateLiveSession(Guid sessionId, string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc, IClock clock)
{
    ArgumentNullException.ThrowIfNull(clock);

    var session = FindLiveSessionOrThrow(sessionId);

    if (session.Status != CourseLiveSessionStatus.Scheduled)
    {
        throw new InvalidOperationException($"Cannot update a live session in {session.Status} status.");
    }

    if (session.EndsAtUtc <= clock.UtcNow)
    {
        throw new InvalidOperationException("Cannot update a live session that has already ended.");
    }

    ValidateSessionWindow(startsAtUtc, endsAtUtc, excludingSessionId: sessionId);

    session.Reschedule(title, description, startsAtUtc, endsAtUtc);
}

/// <summary>ยกเลิกคาบ (soft — Status→Cancelled, ไม่ hard delete แถวทิ้ง; DELETE endpoint ของ P11-02 ก็แม็ปมาที่
/// method นี้เหมือนกัน เพียงแค่ reason เป็น null — ดู §3's หมายเหตุ "DELETE vs POST .../cancel"). reason ว่าง/
/// null ได้ (DELETE ไม่บังคับส่ง เหตุผล) — trim แล้วเก็บ null ถ้าว่างเปล่าหลัง trim.</summary>
public void CancelLiveSession(Guid sessionId, string? reason, IClock clock)
{
    ArgumentNullException.ThrowIfNull(clock);

    var session = FindLiveSessionOrThrow(sessionId);

    if (session.Status != CourseLiveSessionStatus.Scheduled)
    {
        throw new InvalidOperationException($"Cannot cancel a live session in {session.Status} status.");
    }

    if (session.EndsAtUtc <= clock.UtcNow)
    {
        throw new InvalidOperationException("Cannot cancel a live session that has already ended.");
    }

    session.Cancel(reason);
}

/// <summary>ผูกบทเรียนบันทึกเข้ากับคาบ (P11-06 เรียกใช้ — episode ต้องถูกสร้าง/AttachMedia ไปแล้วก่อนโดย
/// AttachSessionRecordingHandler ผ่าน COURSE.AddEpisode/AttachEpisodeMedia ที่มีอยู่แล้วจาก P1-02, ก่อนเรียก
/// method นี้). ไม่เช็ค session.Status เลยโดยตั้งใจ — แนบบันทึกได้แม้คาบจะ Cancelled ก็ตาม (สอนสดไปแล้วเปลี่ยนใจ
/// ยกเลิกคาบที่เหลือ แต่บันทึกของคาบที่สอนไปแล้วยังอัปโหลดได้ปกติ ไม่ใช่ edge case ที่ควรบล็อก) เรียกซ้ำได้
/// (overwrite RecordingEpisodeId เดิม — ไม่ throw ถ้ามีอยู่แล้ว, กรณีผู้สอนอัปโหลดผิดไฟล์แล้วอัปใหม่).</summary>
public void AttachSessionRecording(Guid sessionId, Guid episodeId)
{
    var session = FindLiveSessionOrThrow(sessionId);

    if (!_sections.SelectMany(s => s.Episodes).Any(e => e.Id == episodeId))
    {
        throw new InvalidOperationException($"Episode {episodeId} does not belong to this course.");
    }

    session.AttachRecording(episodeId);
}

private COURSE_LIVE_SESSION FindLiveSessionOrThrow(Guid sessionId) =>
    _liveSessions.FirstOrDefault(s => s.Id == sessionId)
        ?? throw new InvalidOperationException($"Live session {sessionId} was not found on this course.");

/// <summary>ตรวจ duration (15 นาที–8 ชม.) + ไม่ทับกับคาบ Scheduled อื่นในคอร์สเดียวกัน (Cancelled ไม่นับ —
/// เวลาที่คาบถูกยกเลิกไปแล้วว่างให้จองซ้ำได้ปกติ) excludingSessionId กันตัวเองชนตัวเองตอน UpdateLiveSession.
/// Overlap = half-open interval เทียบกันตรงไปตรงมา (a.Start &lt; b.End &amp;&amp; b.Start &lt; a.End) — คาบที่
/// เวลาต่อกันพอดี (EndsAtUtc ของคาบหนึ่ง == StartsAtUtc ของอีกคาบ) ไม่ถือว่าทับกัน.</summary>
private void ValidateSessionWindow(DateTime startsAtUtc, DateTime endsAtUtc, Guid? excludingSessionId)
{
    if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc)
    {
        throw new ArgumentException("startsAtUtc and endsAtUtc must be UTC (database.md: Npgsql throws on non-Utc DateTime).");
    }

    if (endsAtUtc <= startsAtUtc)
    {
        throw new ArgumentException("endsAtUtc must be after startsAtUtc.");
    }

    var duration = endsAtUtc - startsAtUtc;
    if (duration < TimeSpan.FromMinutes(15) || duration > TimeSpan.FromHours(8))
    {
        throw new ArgumentException("Live session duration must be between 15 minutes and 8 hours.");
    }

    var overlaps = _liveSessions.Any(s =>
        s.Id != excludingSessionId &&
        s.Status == CourseLiveSessionStatus.Scheduled &&
        s.StartsAtUtc < endsAtUtc && startsAtUtc < s.EndsAtUtc);

    if (overlaps)
    {
        throw new InvalidOperationException("This time overlaps with another scheduled live session in this course.");
    }
}
```

`COURSE_LIVE_SESSION.cs`'s internal mutators (เรียกจาก `COURSE` เท่านั้น):
```csharp
internal void Reschedule(string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(title);
    Title = title;
    Description = description;
    StartsAtUtc = startsAtUtc;
    EndsAtUtc = endsAtUtc;
}

internal void Cancel(string? reason)
{
    Status = CourseLiveSessionStatus.Cancelled;
    CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}

internal void AttachRecording(Guid episodeId)
{
    RecordingEpisodeId = episodeId;
}
```

Enum ใหม่ `src/Siri.Modules.Catalog/Domain/CourseLiveSessionStatus.cs`:
```csharp
namespace Siri.Modules.Catalog.Domain;

public enum CourseLiveSessionStatus
{
    Scheduled,
    Cancelled,
}
```

### 2.4 แก้ `COURSE.Publish()` / `SubmitForReview()` — invariant ใหม่

**Signature เปลี่ยน (breaking, กระทบ call site ที่มีอยู่แล้ว — ดูท้ายหัวข้อนี้)**: `SubmitForReview()` ต้องรับ `IClock clock` เพิ่ม (`Publish(IClock clock)` มีอยู่แล้วไม่ต้องแก้ signature)

```csharp
public void Publish(IClock clock)
{
    ArgumentNullException.ThrowIfNull(clock);

    if (Status is CourseStatus.Published or CourseStatus.Archived)
    {
        throw new InvalidOperationException($"Cannot publish a course in {Status} status.");
    }

    if (!CanPublishOrSubmit(clock))
    {
        throw new InvalidOperationException(PublishInvariantMessage());
    }

    Status = CourseStatus.Published;
    PublishedAtUtc = clock.UtcNow;
    RejectionReason = null;
}

public void SubmitForReview(IClock clock)
{
    ArgumentNullException.ThrowIfNull(clock);

    if (Status is not (CourseStatus.Draft or CourseStatus.Rejected))
    {
        throw new InvalidOperationException($"Cannot submit a course in {Status} status for review.");
    }

    if (!CanPublishOrSubmit(clock))
    {
        throw new InvalidOperationException(PublishInvariantMessage());
    }

    Status = CourseStatus.InReview;
    RejectionReason = null;
}

/// <summary>docs/HYBRID_LIVE.md §1.1's ตาราง — OnDemand ต้องมี episode ที่มี media (เดิม, ไม่เปลี่ยน) ·
/// Live/Hybrid ต้องมี "session อนาคตหรือ episode มี media" (อย่างใดอย่างหนึ่งพอ — Hybrid ที่ยังไม่ตั้งตาราง
/// สอนสดแต่มี VOD ครบแล้วก็ publish ได้ปกติ, Live ล้วนที่ยังไม่มี episode เลยแต่ตั้งตารางสอนไว้แล้วก็ publish ได้
/// เหมือนกัน — "อย่างใดอย่างหนึ่ง" ไม่ใช่ "ทั้งสอง"). แทนที่ HasEpisodeWithMedia() ตัวเดิมที่ยังอยู่เป็น private
/// helper ตัวหนึ่งในสอง ไม่ใช่ถูกลบทิ้ง.</summary>
private bool CanPublishOrSubmit(IClock clock) => DeliveryFormat switch
{
    DeliveryFormat.OnDemand => HasEpisodeWithMedia(),
    DeliveryFormat.Live or DeliveryFormat.Hybrid => HasFutureScheduledLiveSession(clock) || HasEpisodeWithMedia(),
    _ => throw new UnreachableException($"Unknown {nameof(DeliveryFormat)} value: {DeliveryFormat}."),
};

private bool HasEpisodeWithMedia() => _sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null);

private bool HasFutureScheduledLiveSession(IClock clock) =>
    _liveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > clock.UtcNow);

private string PublishInvariantMessage() => DeliveryFormat == DeliveryFormat.OnDemand
    ? "Cannot publish a course with no episode that has media attached."
    : "Cannot publish a Live/Hybrid course with no future scheduled session and no episode that has media attached.";
```
(ข้อความ error เดิม "Cannot publish a course with no episode that has media attached." **คงไว้เป๊ะสำหรับ OnDemand** — มี unit test ที่ผ่านอยู่แล้วอ้างอิง string นี้ตรง ๆ, `PublishInvariantMessage()` แยก branch เพื่อไม่ทำ regression กับเทสต์เดิม)

**ทำไมไม่ re-validate ตอน `SetDeliveryFormat` เปลี่ยนค่าบนคอร์สที่ Published ไปแล้ว**: invariant นี้เป็น *gate ตอนเปลี่ยนสถานะเข้า* Published/InReview เท่านั้น ไม่ใช่ invariant ที่ต้องคงอยู่ตลอดเวลา (เหมือน `Price`/`Title` ที่ก็ไม่ถูก re-validate ทุกครั้งที่มีการเปลี่ยนแปลงอื่นบนคอร์สที่ Published แล้ว) — คอร์ส OnDemand ที่ Published อยู่แล้วเปลี่ยนเป็น Hybrid โดยยังไม่มี session เลย จะแสดง `liveSchedule` เป็น block ว่าง (P11-07's response) ชั่วคราวจนกว่าจะเพิ่ม session จริง ไม่ใช่สถานะที่ผิดพลาด

**Call site ที่ต้องแก้ตามไปด้วย (อยู่ในขอบเขต P11-01 เพราะจำเป็นให้ build ผ่าน ไม่ใช่ scope creep):**
- `src/Siri.Modules.Catalog/Features/SubmitCourseForReview/Handler.cs:33-35` — `.Include(c => c.Sections).ThenInclude(s => s.Episodes)` ต้องเพิ่ม `.Include(c => c.LiveSessions)` (คนละ `Include` แยก ไม่ใช่ `ThenInclude` ต่อจาก Sections — `LiveSessions` เป็น sibling collection ของ `Sections` บน `COURSE` ไม่ใช่ลูกของ section) — ไม่งั้น `CanPublishOrSubmit`'s `HasFutureScheduledLiveSession` เห็น collection ว่างเปล่าเสมอ (ไม่มี lazy-loading ในโค้ดเบสนี้ เหมือนที่ comment เดิมของไฟล์นี้เตือนไว้เรื่อง Sections/Episodes) — บั๊กเงียบแบบเดียวกับที่ P1-05's history เคยเจอมาก่อนกับ `.Include` ที่ลืม
- ไฟล์เดียวกัน: handler-level pre-check (บรรทัดที่เช็ค `!course.Sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null)` แล้วคืน `NoMediaError`) ต้อง format-aware เหมือนกัน — เพิ่ม `DomainError` ใหม่สำหรับ Live/Hybrid (เช่น `DomainError.Validation("คอร์ส Live/Hybrid ต้องมีคาบสอนสดในอนาคตอย่างน้อย 1 คาบ หรือมีบทเรียนที่แนบวิดีโอแล้ว")`) และเรียก logic เดียวกับ `CanPublishOrSubmit` (ทำ private helper ซ้ำในชั้น handler หรือ expose ผ่าน internal method ก็ได้ — ตัดสินใจตอน implement, ไม่ critical พอจะ freeze ไว้ตรงนี้)
- ไฟล์เดียวกัน: `course.SubmitForReview()` → `course.SubmitForReview(clock)` — handler ต้อง inject `IClock clock` เพิ่ม (constructor parameter list ปัจจุบันคือ `(AppDbContext dbContext, IMediaAssetContract mediaAssets)` — เพิ่ม `IClock clock` เข้าไป)
- `src/Siri.Modules.Catalog/Features/ApproveCourse/Handler.cs:34-36` — `.Include(c => c.Sections).ThenInclude(s => s.Episodes)` เพิ่ม `.Include(c => c.LiveSessions)` เหตุผลเดียวกัน (`IClock clock` มี inject อยู่แล้วในไฟล์นี้ ไม่ต้องเพิ่ม) — handler-level pre-check ของไฟล์นี้ (ผ่าน `CourseMediaReadiness.ValidateAsync`) ก็ต้อง format-aware เหมือนกัน — **อ่าน `CourseMediaReadiness.cs` ก่อนแก้** (ไม่ได้อยู่ในไฟล์ที่ผมอ่านตอนออกแบบ contract นี้ — backend-developer ต้องเช็คเองว่า helper ตัวนี้ควรรับ `DeliveryFormat`/`LiveSessions` เพิ่มด้วยหรือควรแยก validation คนละจุด)
- `CourseConfiguration.cs` — เพิ่ม `builder.Property(c => c.DeliveryFormat).HasConversion<string>().HasMaxLength(20).IsRequired();` และ `builder.Navigation(c => c.LiveSessions).HasField("_liveSessions").UsePropertyAccessMode(PropertyAccessMode.Field);` (pattern เดียวกับ `Sections`/`Outcomes`/`Requirements` เป๊ะ — **ถ้าลืมจะเกิด shadow FK phantom column แบบที่ P1-02's history เจอมาก่อน อ่าน migration ที่ generate ออกมาก่อนใช้เสมอ**)
- ไฟล์ใหม่ `CourseLiveSessionConfiguration.cs` มิเรอร์ `CourseSectionConfiguration.cs`'s รูปแบบ (`.HasOne<COURSE>().WithMany(c => c.LiveSessions).HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Cascade)` — ระบุ navigation `WithMany(c => c.LiveSessions)` ชัดเจน ห้ามเว้นว่าง เหตุผลเดียวกับ comment ใน `CourseSectionConfiguration.cs`)

### 2.5 `Siri.Modules.Catalog.Contracts.LiveSessionDisplayStateCalculator` — utility ใหม่

ไฟล์ใหม่ `src/Siri.Modules.Catalog/Contracts/LiveSessionDisplayState.cs` — อยู่ใน `Catalog.Contracts` (ไม่ใช่ `Catalog.Domain`) โดยตั้งใจ: module อื่น (ในอนาคต `Siri.Modules.Live`, P11-03/05) reference `Catalog.Contracts` ได้อยู่แล้ว (implement `ILiveMeetingSink`/consume `ILiveScheduleReader` จากที่นั่น) แต่ reference `Catalog.Domain` ไม่ได้ (ArchitectureTest บล็อก) — ฟังก์ชันคำนวณ displayState ต้องให้ผลตรงกันทั้งสองฝั่ง (P11-07's public read model ในตอนนี้, P11-05's `/join`/`my-sessions` ในอนาคต) จึงต้องอยู่ที่จุดที่ทั้งคู่ reference ได้ ไม่ใช่ implement ซ้ำสองที่แล้วเสี่ยง drift (เช่น off-by-one ตรงขอบเขต `StartsAtUtc-15m`)

```csharp
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
```

### 2.6 `ILiveScheduleReader` / `ILiveMeetingSink` — Contracts ใหม่

`src/Siri.Modules.Catalog/Contracts/ILiveScheduleReader.cs`:
```csharp
namespace Siri.Modules.Catalog.Contracts;

public sealed record LiveSessionInfo(
    Guid SessionId,
    Guid CourseId,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionStatus Status,
    Guid? RecordingEpisodeId);

/// <summary>Read-side ของตารางสอนสด — implement โดย Catalog เอง (ต่างจาก ILiveMeetingSink ที่ Catalog
/// แค่ประกาศ). ผู้บริโภค: Siri.Modules.Live (P11-03's live-meeting-sync job อ่าน session เดียวตอน sync,
/// P11-04's live-invite-reconcile job อ่าน upcoming ทั้งชุดตอน diff invite) และ Learning (P12's
/// study-plan อ่าน "คาบถัดไป").</summary>
public interface ILiveScheduleReader
{
    Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>เฉพาะ Status == Scheduled ในช่วง [fromUtc, toUtc) — คาบ Cancelled ไม่นับเป็น "upcoming"
    /// (ไม่มีอะไรให้ reconcile invite ต่อ, การยกเลิก invite ของคาบที่ถูก cancel เป็นเส้นทาง reactive แยก
    /// ผ่าน ILiveMeetingSink.OnSessionCancelledAsync ไม่ใช่เส้นทางนี้).</summary>
    Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}
```

`src/Siri.Modules.Catalog/Contracts/ILiveMeetingSink.cs`:
```csharp
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
```

`src/Siri.Modules.Catalog/Infrastructure/NullLiveMeetingSink.cs` — มิเรอร์ `NullAttachmentVirusScanner.cs` เป๊ะ:
```csharp
namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>Default no-op จนกว่า Siri.Modules.Live (P11-03) จะ register implementation จริงทับ —
/// ให้ P11-02's handler เรียก ILiveMeetingSink ได้ตั้งแต่วันแรกโดยไม่ error แม้ Live module จะยังไม่มีอยู่จริง.</summary>
public sealed class NullLiveMeetingSink : ILiveMeetingSink
{
    public Task OnSessionScheduledAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnSessionChangedAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnSessionCancelledAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`CatalogModule.AddCatalogModule`: เพิ่ม 2 บรรทัด (ข้าง `IAttachmentVirusScanner` registration ที่มีอยู่แล้ว บรรทัด 208):
```csharp
services.AddSingleton<Contracts.ILiveMeetingSink, Infrastructure.NullLiveMeetingSink>();
services.AddScoped<Contracts.ILiveScheduleReader, Infrastructure.LiveScheduleReader>();
```
(`LiveScheduleReader` — implementation ใหม่ query `dbContext.Set<COURSE_LIVE_SESSION>()` ตรง ๆ ผ่าน `AppDbContextCatalogExtensions`'s accessor ใหม่ `CourseLiveSessions()`, map เข้า `LiveSessionInfo`/`LiveSessionStatus` ของ Contracts เอง — ไม่ expose `Domain.COURSE_LIVE_SESSION`/`Domain.CourseLiveSessionStatus` ออกนอกโมดูลเด็ดขาด)

เพิ่ม accessor ใน `AppDbContextCatalogExtensions.cs`:
```csharp
public static DbSet<COURSE_LIVE_SESSION> CourseLiveSessions(this AppDbContext context) => context.Set<COURSE_LIVE_SESSION>();
```

### 2.7 Unit test ที่ต้องมี (ทุก branch)

`COURSE_LIVE_SESSION`-related (ต่อใน `CourseTests.cs` เดิม หรือไฟล์ใหม่ `CourseLiveSessionTests.cs` — ตัดสินใจตอน implement):
- `AddLiveSession`: สำเร็จบนคอร์ส Live/Hybrid, ปฏิเสธบน OnDemand, ปฏิเสธบน Archived, ปฏิเสธ duration < 15 นาที, ปฏิเสธ duration > 8 ชม., ปฏิเสธทับกับคาบ Scheduled อื่น, **ไม่ปฏิเสธ**ทับกับคาบ Cancelled, ปฏิเสธ startsAtUtc ในอดีต, ปฏิเสธ Kind != Utc, SortOrder เพิ่มตามลำดับที่เพิ่ม
- `UpdateLiveSession`: สำเร็จ (รวม "ต่อเวลา" กรณี StartsAtUtc ผ่านไปแล้วแต่ EndsAtUtc ยังไม่ถึง), ปฏิเสธถ้า EndsAtUtc เดิมผ่านไปแล้ว, ปฏิเสธถ้า session Cancelled, ปฏิเสธทับกับคาบอื่น (ไม่รวมตัวเอง — พิสูจน์ excludingSessionId ทำงานถูก), ปฏิเสธ sessionId ไม่มีจริง
- `CancelLiveSession`: สำเร็จ (มี/ไม่มี reason), ปฏิเสธถ้า EndsAtUtc ผ่านไปแล้ว, ปฏิเสธถ้า Cancelled อยู่แล้ว (idempotent-not — เรียกซ้ำต้อง error ไม่ใช่ no-op เงียบ ๆ)
- `AttachSessionRecording`: สำเร็จ (รวมกรณี session Cancelled ก็ยัง attach ได้), ปฏิเสธถ้า episodeId ไม่ใช่ episode ของคอร์สนี้, เรียกซ้ำ overwrite ค่าเดิมได้ไม่ throw
- `SetDeliveryFormat`: สำเร็จทุกทิศทาง (OnDemand↔Live↔Hybrid), ปฏิเสธ→OnDemand ขณะมี Scheduled session ค้าง, **สำเร็จ**→OnDemand ถ้า session ทั้งหมด Cancelled แล้ว, ปฏิเสธบน Archived

`Publish`/`SubmitForReview` (ขยาย `CourseTests.cs` เดิม):
- OnDemand: พฤติกรรมเดิมทั้งหมดต้องผ่านเหมือนเดิม (regression — มี episode media → publish ได้, ไม่มี → error message ตรงกับสตริงเดิมเป๊ะ)
- Live/Hybrid: publish ได้ด้วย future Scheduled session อย่างเดียว (ไม่มี episode media เลย), publish ได้ด้วย episode media อย่างเดียว (ไม่มี session เลย), publish ได้ด้วยทั้งสอง, **ปฏิเสธ**ถ้ามีแต่ session ในอดีต (StartsAtUtc <= now) ไม่มี episode media, ปฏิเสธถ้ามีแต่ session Cancelled ไม่มี episode media, `SubmitForReview(clock)`'s branch เดียวกันครบชุด

`LiveSessionDisplayStateCalculator`:
- Cancelled ชนะทุกกรณี (แม้เวลาจะบอกว่า Ended ก็ตาม)
- ขอบเขตพอดี: `nowUtc == startsAtUtc.AddMinutes(-15)` → Live (inclusive), `nowUtc == startsAtUtc.AddMinutes(-15).AddTicks(-1)` → Upcoming
- `nowUtc == endsAtUtc` → Live (ไม่ใช่ Ended — `>` ไม่ใช่ `>=` ใน guard), `nowUtc == endsAtUtc.AddTicks(1)` → Ended
- joinWindowBeforeMinutes กำหนดเองได้ (ไม่ hardcode 15 ใน logic)

---

## 3. API contract — สำหรับ P11-02 (Antigravity, เริ่มได้ทันทีที่ FROZEN + P11-01 merge)

**Module/pattern**: `Siri.Modules.Catalog`, vertical slice (`Features/{UseCase}/{Command,Validator,Handler,Response}.cs`) — endpoint class name/method/DTO เป็น PascalCase ปกติ (ไม่ใช่ entity ที่เป็น UPPERCASE — อย่าสับสน)

**Controller ใหม่**: `src/Siri.Api/Controllers/Catalog/LiveSessionsController.cs` — ไฟล์แยกจาก `InstructorCoursesController.cs` (1 controller ต่อ resource ตาม backend.md), `[Route("api/catalog/instructor/courses/{courseId:guid}/live-sessions")]`, `[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]` ที่ระดับ class, `[EnableRateLimiting("default")]` ทุก action (ไม่มี endpoint ไหนในกลุ่มนี้ต้องการ policy พิเศษกว่า "default" — ไม่ใช่ login/checkout/webhook)

**`PUT .../delivery-format` อยู่บน `InstructorCoursesController.cs` เดิม** (route prefix `api/catalog/instructor/courses` ตรงกันพอดีอยู่แล้ว ไม่ต้องเปิด controller ใหม่)

**Ownership pattern — ใช้ query ตรง ไม่ใช่ `ICatalogPriceContract`**: ภายใน Catalog เอง handler ทุกตัวเช็ค ownership ด้วย query ตรง (`dbContext.InstructorProfiles().FirstOrDefaultAsync(p => p.UserId == userId)` แล้วเทียบ `course.InstructorId != instructorProfile.Id`) — pattern เดียวกับ `CreateCourseSectionHandler.cs` เป๊ะ **ไม่ใช่**เรียก `ICatalogPriceContract.IsInstructorOwnerOfCourseAsync` (contract ตัวนั้นมีไว้ให้โมดูลอื่นเรียกข้ามเข้ามา ไม่ใช่ให้ Catalog เรียกตัวเอง)

**404 vs 403**: distinct เสมอ (course ไม่พบ = 404, พบแต่ไม่ใช่เจ้าของ = 403) — pattern เดียวกับ `UpdateCourseHandler`/`CreateCourseSectionHandler` ทุกตัว ไม่ collapse (เหตุผลเดิม: course id ไม่ใช่ secret ระดับ session)

**Handler orchestration (ทุก endpoint ที่แก้ state)**: เรียก domain method (`course.AddLiveSession(...)` ฯลฯ) → เรียก `ILiveMeetingSink`'s เมธอดที่ตรงกัน (stage, ไม่ save) → `dbContext.SaveChangesAsync()` **ครั้งเดียว** → ถ้าคอร์ส `Status == Published` ให้ `await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, ct)` (หลัง commit สำเร็จเท่านั้น — ตำแหน่งเดียวกับที่ `ApproveCourseHandler` ทำ) เพราะ P11-07's public course detail แสดง `liveSchedule` ที่ cache ไว้อยู่ — คอร์ส Draft ไม่มีอะไรให้ evict (ไม่เคยอยู่ใน public cache)

### 3.1 `POST /api/catalog/instructor/courses/{courseId}/live-sessions`
Request:
```json
{ "title": "Kickoff เซสชั่นแรก", "description": "แนะนำคอร์ส + Q&A", "startsAtUtc": "2026-10-01T03:00:00Z", "endsAtUtc": "2026-10-01T05:00:00Z" }
```
Validation (FluentValidation, `CreateLiveSessionCommand(string Title, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc)`): `Title` required, ≤200 · `Description` ≤2000 · `EndsAtUtc > StartsAtUtc` (redundant กับ domain แต่ error message เร็วกว่า) — **`startsAtUtc`/`endsAtUtc` ต้อง parse เป็น `DateTimeKind.Utc`**: string ต้องมี `Z` suffix เสมอ — **ตรวจสอบพฤติกรรมจริงของ System.Text.Json's default `DateTime` converter บน .NET 10 ด้วย unit test เล็ก ๆ ก่อนเขียนอย่างอื่น** (ห้ามเดา — ถ้า offset ที่ไม่ใช่ `Z` (เช่น `+07:00`) พาร์สแล้วได้ `Kind=Local` แทน `Utc`, `Course.AddLiveSession`'s `ValidateSessionWindow` จะ throw `ArgumentException` ที่ handler ต้อง map เป็น 400 ไม่ใช่ 500 ดิบ — Npgsql เองก็ throw คนละแบบถ้าหลุดไปถึง `SaveChangesAsync` โดยไม่ผ่าน guard นี้ก่อน)

Response `201 Created`, `Location: /api/catalog/instructor/courses/{courseId}/live-sessions/{id}`:
```json
{ "id": "...", "courseId": "...", "title": "...", "description": "...", "startsAtUtc": "...", "endsAtUtc": "...", "sortOrder": 0, "status": "Scheduled", "cancelReason": null, "recordingEpisodeId": null }
```
(`LiveSessionResponse(Guid Id, Guid CourseId, string Title, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc, int SortOrder, CourseLiveSessionStatus Status, string? CancelReason, Guid? RecordingEpisodeId)` — `CourseLiveSessionStatus` enum ออกเป็น string ผ่าน global `JsonStringEnumConverter` อัตโนมัติ ไม่ต้อง `.ToString()` เอง)

Errors: `404` course ไม่พบ · `403` ไม่ใช่เจ้าของ · `409` `DeliveryFormat == OnDemand` ("เปลี่ยนรูปแบบคอร์สเป็น Live หรือ Hybrid ก่อนเพิ่มคาบสอนสด") · `409` course `Archived` · `409` เวลาทับกับคาบอื่น · `400` duration ผิดช่วง / `startsAtUtc` ไม่ใช่อนาคต / validation อื่น

### 3.2 `PUT .../live-sessions/{sessionId}`
Request/Response shape เดียวกับ 3.1 (ไม่มี `Location` header, `200 OK`)
Errors: `404` course หรือ session ไม่พบ (ไม่ต้องแยก — เมื่อ ownership คอร์สผ่านแล้ว sessionId ไม่มีจริงไม่ใช่การรั่วข้อมูลข้าม user) · `403` ไม่ใช่เจ้าของคอร์ส · `409` session ไม่ใช่ `Scheduled` (Cancelled แล้ว) · `409` session `EndsAtUtc` ผ่านไปแล้ว · `409` ทับกับคาบอื่น · `400` duration/validation

### 3.3 `DELETE .../live-sessions/{sessionId}`
ไม่มี request body. Handler เรียก `course.CancelLiveSession(sessionId, reason: null, clock)`
Response: `204 No Content`
Errors: `404` · `403` · `409` (Cancelled อยู่แล้ว / EndsAtUtc ผ่านไปแล้ว)

### 3.4 `POST .../live-sessions/{sessionId}/cancel`
Request: `{ "reason": "ผู้สอนติดธุระเร่งด่วน" }` — `CancelLiveSessionCommand(string Reason)`, validate `Reason` required ≤500
Response: `200 OK` คืน `LiveSessionResponse` (Status=`Cancelled`, CancelReason=ค่าที่ส่ง) — **ต่างจาก DELETE ตรงที่คืน resource กลับ** (ให้ builder UI แสดงเหตุผลที่เพิ่งบันทึกได้ทันทีไม่ต้อง refetch)
Errors: เหมือน 3.3 + `400` ถ้าไม่ส่ง `reason`

**หมายเหตุ "DELETE vs POST .../cancel"**: ทั้งคู่แม็ปเข้า domain method **เดียวกัน** (`COURSE.CancelLiveSession`) — task spec (`docs/HYBRID_LIVE.md`/`TASKS.md`) ไม่ได้แยก domain method "remove" ต่างหาก (มีแค่ 4 เมธอดตามที่ระบุในคำสั่งงาน: Add/Update/Cancel/AttachRecording — ไม่มี Remove) และ database.md's blanket "ห้าม hard delete" instinct ขยายมาถึงตารางนี้ด้วยแม้จะไม่ได้อยู่ใน explicit list (Orders/Payments/...) ตรง ๆ (เหตุผลเดียวกับที่ `CourseConfiguration.cs`'s doc comment ให้ไว้กับ `InstructorId`'s FK) DELETE จึงเป็นแค่ REST-conventional alias ของ cancel-ไม่มีเหตุผล ไม่ใช่ SQL DELETE จริง — **ห้าม implement เป็น hard delete แถวทิ้งเด็ดขาด**

### 3.5 `PUT /api/catalog/instructor/courses/{id}/delivery-format` (บน `InstructorCoursesController.cs`)
Request: `{ "deliveryFormat": "Hybrid" }` — `SetCourseDeliveryFormatCommand(DeliveryFormat DeliveryFormat)`
Response: `200 OK`: `{ "id": "...", "deliveryFormat": "Hybrid" }`
Errors: `404` · `403` · `409` `Archived` · `409` เปลี่ยนเป็น `OnDemand` ขณะมี Scheduled session ค้างอยู่ ("ยกเลิกทุกคาบสอนสดก่อนเปลี่ยนกลับเป็น OnDemand")
ไม่เรียก `ILiveMeetingSink` (การเปลี่ยน format เองไม่กระทบ meeting ที่มีอยู่)

### 3.6 นอกขอบเขตของ P11-02 (งานของ task อื่น — อย่าทำที่นี่)
- `POST .../live-sessions/{sessionId}/recording` — P11-06's เอง contract (จะออกทีหลัง เมื่อ P11-06 เริ่ม)
- `GET .../live-sessions`/`GET .../live-sessions/{sessionId}` (อ่านฝั่งผู้สอนพร้อม meetUrl/sync status/roster) — P11-05 (`GET /api/live/instructor/sessions/{sid}`, ติด Q10, module `Siri.Modules.Live` ยังไม่มี)

### 3.7 Minimal-API mapper คู่ (harness เดิม)
`docs/HYBRID_LIVE.md` §5: "ทุก endpoint ใหม่ต้องมีทั้ง MVC controller และ minimal-API mapper ถ้าจะให้ integration test harness เดิม (`_app.Map*Endpoints()`) ครอบ — หรือย้าย harness ไปใช้ `WebApplicationFactory` (บันทึกใน P11-30)" — **P11-30 ยังไม่เริ่ม ณ วันที่เขียน contract นี้** ดังนั้น P11-02 default เป็น: เขียน `Endpoint.cs` คู่กันทุกตัว (มิเรอร์ `GetCourseDetailEndpoint`'s รูปแบบ — `MapPost`/`MapPut`/`MapDelete` เรียก handler ตัวเดียวกับที่ controller เรียก) **เช็คก่อนเริ่มว่า P11-30 landed หรือยัง** (ดู `docs/TASKS.md`'s Status column ของ P11-30) — ถ้า landed แล้วและเปลี่ยน harness ไป `WebApplicationFactory` จริง ให้ข้ามการเขียน `Endpoint.cs` คู่ได้เลย (จะกลายเป็นโค้ดที่ไม่มีใครเรียก)

---

## 4. Frontend read model — สำหรับ P11-07 (Antigravity, เริ่มได้ทันทีที่ FROZEN)

**Module**: `Siri.Modules.Catalog` — แก้ `GetCourseDetail`/`SearchCourses` features เดิม (ไม่ใช่ FE repo — คำว่า "frontend" ในหัวข้อนี้หมายถึง "read model ที่ frontend จะบริโภค" endpoint ยังอยู่ backend repo นี้ FE ต่อ API ทำใน repo `siri_upskill_ui` เป็นคนละ task/contract)

### 4.1 `GetCourseDetailResponse` (`src/Siri.Modules.Catalog/Features/GetCourseDetail/Response.cs`)
เพิ่ม field ต่อท้าย record เดิม (additive, ไม่ reorder positional field เดิม — C# record positional param เรียงตำแหน่งสำคัญกับ caller ที่มีอยู่แล้ว):
```csharp
public sealed record CourseDetailResponse(
    /* ...ทุก field เดิมคงเดิมทั้งหมด... */
    bool IsWishlisted,
    DeliveryFormat DeliveryFormat,
    CourseDetailLiveSchedule? LiveSchedule = null);

/// <summary>null เมื่อ DeliveryFormat == OnDemand เท่านั้น (ไม่มีอะไรให้แสดง) — Live/Hybrid ได้ค่านี้เสมอแม้
/// Sessions จะว่างเปล่า (ยังไม่ตั้งตารางสอน — ไม่ error, แค่ list ว่าง).</summary>
public sealed record CourseDetailLiveSchedule(
    string Timezone,
    int UpcomingCount,
    int PastCount,
    DateTime? NextStartsAtUtc,
    IReadOnlyList<CourseDetailLiveSession> Sessions);

/// <summary>**ไม่มี meetUrl โดยตั้งใจ — เป็น security requirement ไม่ใช่แค่ scope** (docs/HYBRID_LIVE.md §2.1:
/// "Meet URL ไม่เคยอยู่ใน response สาธารณะ") ต้องมี integration test ยืนยันด้วย JSON path ตรง ๆ ว่าไม่มี
/// property ชื่อ meetUrl/meetingUrl หลุดออกมาเลย (ดู §5 integration checklist).</summary>
public sealed record CourseDetailLiveSession(
    Guid Id,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    bool HasRecording);
```

**Handler logic** (`GetCourseDetailHandler.cs`): ถ้า `course.DeliveryFormat != DeliveryFormat.OnDemand` → query `COURSE_LIVE_SESSIONS` ที่ `Status == Scheduled` เรียงตาม `StartsAtUtc` ASC (ไม่รวม Cancelled — หน้า public ไม่ต้องโชว์คาบที่ยกเลิกไปแล้ว) → `Sessions` = ทั้งหมด map ผ่าน `LiveSessionDisplayStateCalculator.Compute(LiveSessionStatus.Scheduled, s.StartsAtUtc, s.EndsAtUtc, clock.UtcNow)` (ทุกแถวที่ query มาคือ Scheduled อยู่แล้ว ผลลัพธ์เป็นได้แค่ Upcoming/Live/Ended ไม่มีทาง Cancelled) → `UpcomingCount` = count ที่ `DisplayState` เป็น `Upcoming` หรือ `Live` (นับรวมคาบที่กำลังสอนอยู่ว่า "ยังมีสิทธิ์เข้าฟัง" ไม่ใช่แค่ที่ยังไม่เริ่ม) → `PastCount` = count ที่ `Ended` → `NextStartsAtUtc` = `StartsAtUtc` ของแถวแรกที่ `DisplayState != Ended` (`null` ถ้าไม่มี) → `Timezone` = constant `"Asia/Bangkok"` (ไม่ใช่ config — ทั้งแพลตฟอร์มมีโซนเวลาเดียว, ค่านี้บอก FE ว่าจะแปลง UTC ไปแสดงโซนไหน ไม่ใช่ metadata ของ session เอง) — handler ต้อง `GetCourseLiveSessions()` accessor ใหม่จาก §2.6 (`.AsNoTracking()`, ตาม database.md query rule)

### 4.2 `SearchCoursesQuery`/`SearchCoursesResponse` (`src/Siri.Modules.Catalog/Features/SearchCourses/`)
`Query.cs` เพิ่ม `DeliveryFormat? Format` ต่อท้าย positional param
`Response.cs`:
```csharp
public sealed record CourseSearchResultItem(
    /* ...ทุก field เดิมคงเดิม... */
    bool IsWishlisted,
    DeliveryFormat DeliveryFormat,
    DateTime? NextStartsAtUtc = null);  // null สำหรับ OnDemand หรือ Live/Hybrid ที่ไม่มี Scheduled session อนาคต

public sealed record CourseSearchFacets(
    IReadOnlyList<CategoryFacet> Categories,
    IReadOnlyList<LevelFacet> Levels,
    IReadOnlyList<InstructorFacet> Instructors,
    IReadOnlyList<FormatFacet> Formats);

public sealed record FormatFacet(DeliveryFormat Format, int Count);
```
Filter: `query.Format is not null` → `WHERE DeliveryFormat == query.Format` (เพิ่มในจุดเดียวกับ filter `Level`/`CategoryId` เดิม)
Facet: คำนวณจาก search-matched + Published set **ก่อน**กรอง filter ของ request นี้เอง — pattern เดียวกับ `CategoryFacet`/`InstructorFacet` เป๊ะ (ดู `CourseSearchFacets`'s doc comment เดิมเรื่อง "v1 simplification" — ใช้เหตุผลเดียวกัน ไม่ต้องเขียนซ้ำ)
`NextStartsAtUtc` ต่อ card: query แบบ join/subquery หา `MIN(StartsAtUtc)` ของ `COURSE_LIVE_SESSIONS` ที่ `Status == Scheduled && StartsAtUtc > now` ต่อ `CourseId` — **ห้าม N+1** (ต้อง projection รวมใน query เดียวกับที่ดึง `CourseSearchResultItem` อื่น ไม่ใช่ query แยกทีละคอร์สในหน้าผลลัพธ์)

### 4.3 Output cache
ไม่มีอะไรเพิ่มนอกจาก §3's ข้อกำหนดที่ P11-02 ทำอยู่แล้ว (evict tag `courses` ทุกครั้งที่ live session เปลี่ยนบนคอร์สที่ Published) — P11-07 เอง**ไม่ต้อง**เพิ่มจุด evict ใหม่ (อ่านอย่างเดียว)

### 4.4 Sitemap
ไม่เปลี่ยน (`docs/HYBRID_LIVE.md` §5 ยืนยันชัดแล้ว)

---

## 5. Integration checklist (สำหรับ integrator-qa)

- **Security-critical — ต้องมี automated test ไม่ใช่แค่ manual check**: `GET /api/catalog/courses/{slug}` และ `GET /api/catalog/courses/search` response body (deserialize เป็น raw `JsonDocument`, เดินทุก property name แบบ recursive) ต้อง**ไม่มี** property ชื่อที่มีคำว่า `meetUrl`/`meetingUrl`/`meet_url` ที่ไหนเลยในทุก response แม้คอร์สจะเป็น Live/Hybrid ที่มี session Synced จริงก็ตาม (docs/HYBRID_LIVE.md §2.1's hard requirement)
- IDOR: instructor A แก้/ยกเลิก/ดูคาบของ instructor B's คอร์ส → 403 หรือ 404 ตามที่ระบุใน §3 (ไม่ใช่ 200)
- Overlap 409: สร้างคาบซ้อนเวลากับคาบที่มีอยู่แล้ว (ทั้ง exact overlap และ partial overlap) → 409, สร้างคาบที่ต่อกันพอดี (ไม่ overlap) → 201 สำเร็จ
- Duration boundary: 14:59 → 400, 15:00 → 201, 8:00:00 → 201, 8:00:01 → 400
- `DeliveryFormat == OnDemand` → POST live-sessions → 409
- `SetDeliveryFormat(OnDemand)` ขณะมี Scheduled session → 409, ขณะมีแต่ Cancelled session → 200 สำเร็จ
- Publish invariant ทุก format × ทุกเงื่อนไข (ตาราง §1.1 ทั้ง 3 คอลัมน์ × เงื่อนไขมี/ไม่มี session/episode) — อย่างน้อย 6 เคส
- DELETE กับ POST .../cancel ทั้งคู่ต้อง**ไม่**ลบแถวจริง — query ด้วย raw SQL/`IgnoreQueryFilters` (ไม่จำเป็นเพราะไม่มี soft-delete filter บนตารางนี้ — query ตรงพอ) ยืนยันแถวยังอยู่ Status=Cancelled
- Cache eviction: publish คอร์ส Live → เพิ่ม session ใหม่ → `GET courses/{slug}` เห็น session ใหม่ทันที (ไม่ใช่ค้าง cache เดิม 5 นาที) — black-box test แบบเดียวกับ `CourseReadModelTests.cs` เดิม (P1-07)
- `ILiveMeetingSink`/`ILiveScheduleReader` ที่ implement จริงยังไม่มี (Live module ยังไม่สร้าง) — integration test ของ P11-01/02/07 ทดสอบผ่าน `NullLiveMeetingSink` เท่านั้น (พฤติกรรม no-op) — **ห้าม** integration test เขียน fake `ILiveMeetingSink` ของตัวเองมาแทนที่ DI registration เพื่อ "ทดสอบ staging" เพราะยังไม่มี implementation จริงให้ทดสอบตาม (P11-03 จะเป็นเจ้าของเทสต์นั้น)
- `SubmitCourseForReview(clock)`/`Publish(clock)`'s error message string สำหรับ OnDemand **ต้องตรงเป๊ะกับก่อนแก้** (regression check บน unit test ที่มีอยู่แล้ว)

## Changelog
(ยังไม่มี revision หลัง FROZEN)
