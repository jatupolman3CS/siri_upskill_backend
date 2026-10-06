# Contract: P11-13 `AccessDurationDays` นับจาก session แรก (Q13.3)

Status: FROZEN · วันที่: 2026-09-16 · Module: `Siri.Modules.Catalog` (vertical slice — `ILiveScheduleReader` default method, no schema/entity change) + `Siri.Modules.Commerce` (Repository+Service, UPPERCASE entity/DB — `OrderService`, `StripeWebhookHandler`, `PaymentOpsQueueService`)

ผู้ลงมือที่แนะนำ: **backend-developer (Claude)** — ตรงกับ `docs/TASKS.md`'s P11-13 row (`Owner: CC`). งานนี้แก้ enrollment-fulfillment logic โดยตรง (เงิน/สิทธิ์เข้าเรียน) และเพิ่ม method ใหม่ใน `Siri.Modules.Catalog.Contracts.ILiveScheduleReader` (cross-module `Contracts/` interface) — ทั้งสองข้อเป็น charter ของ Claude ตรง ๆ ไม่ใช่ Antigravity ไม่มีงานฝั่ง FE เลย (ไม่มี endpoint ใหม่ ไม่มี response field ใหม่ที่ผู้ใช้เห็น)

**DESIGN_DATABASE/DATABASE: ไม่จำเป็น** — ไม่มี migration เลยในงานนี้ (ดู §0.3) ไม่มีตาราง/คอลัมน์ใหม่ ไม่มี FK ใหม่

---

## 0. สิ่งที่ยืนยันจากโค้ดจริงแล้ว (ไม่เดา)

### 0.1 `ILiveScheduleReader` (P11-01) — ของเดิมมีอะไรอยู่แล้ว

`src/Siri.Modules.Catalog/Contracts/ILiveScheduleReader.cs` (27 บรรทัด):
```csharp
public sealed record LiveSessionInfo(
    Guid SessionId, Guid CourseId, string Title,
    DateTime StartsAtUtc, DateTime EndsAtUtc, LiveSessionStatus Status, Guid? RecordingEpisodeId);

public interface ILiveScheduleReader
{
    Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}
```
`LiveSessionStatus` (Contracts enum, **not** `Domain.CourseLiveSessionStatus`) มีแค่ `Scheduled`/`Cancelled` — เป็น enum คู่ขนานที่ `LiveScheduleReader.ToLiveSessionInfo` map มาจาก domain enum โดยตั้งใจ (module-boundary discipline เดียวกับที่ `CatalogPriceContract` ทำ — ห้าม leak domain enum ข้าม module)

`GetSessionsForCourseAsync`'s implementation จริง (`src/Siri.Modules.Catalog/Infrastructure/LiveScheduleReader.cs:15-24`) **query ทุก session ของคอร์ส ไม่กรอง Status เลย แต่ `.OrderBy(s => s.StartsAtUtc)` ไว้แล้ว**:
```csharp
public async Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken)
{
    return await dbContext.CourseLiveSessions()
        .AsNoTracking()
        .Where(s => s.CourseId == courseId)
        .OrderBy(s => s.StartsAtUtc)
        .Select(s => ToLiveSessionInfo(s))
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);
}
```
DI: `services.AddScoped<Contracts.ILiveScheduleReader, Infrastructure.LiveScheduleReader>();` (`CatalogModule.cs:221`) — scoped, resolved automatically by the container; **ไม่มี test fake ของ `ILiveScheduleReader` อยู่เลยในโค้ดเบสตอนนี้** (grep `tests/**/*.cs` ยืนยันแล้ว, 0 ผลลัพธ์) เพราะไม่เคยมี consumer นอก Catalog module มาก่อนงานนี้

### 0.2 Invariant ที่ทำให้ไม่ต้องเช็ค `DeliveryFormat` ที่ฝั่ง Commerce เลย

`src/Siri.Modules.Catalog/Domain/COURSE.cs` มี 2 guard ที่ประกอบกันแล้วรับประกันว่า **คอร์ส `DeliveryFormat.OnDemand` ไม่มีทางมี `COURSE_LIVE_SESSION` ที่ `Status == Scheduled` อยู่ได้เลย ณ จุดใดก็ตาม**:
- `AddLiveSession` (บรรทัด 482-494): `if (DeliveryFormat == DeliveryFormat.OnDemand) throw new InvalidOperationException("Cannot add a live session to an OnDemand course — change DeliveryFormat first.");` — เพิ่ม session ไม่ได้เลยตอนเป็น OnDemand
- `SetDeliveryFormat` (บรรทัด 463-476): `if (format == DeliveryFormat.OnDemand && _liveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled)) throw new InvalidOperationException("Cannot switch to OnDemand while scheduled live sessions exist — cancel them first.");` — เปลี่ยนกลับเป็น OnDemand ไม่ได้ถ้ายังมี session ที่ยัง Scheduled ค้างอยู่ (ต้อง `CancelLiveSession` ทุกคาบก่อน)

**ผลคือ**: ถ้า query "earliest `Scheduled` session ของ courseId นี้" แล้วได้ `null` กลับมา แปลว่าเป็นได้แค่ 2 กรณีเท่านั้น — (ก) คอร์สเป็น `OnDemand` จริง หรือ (ข) คอร์สเป็น `Live`/`Hybrid` แต่ยังไม่เคยตั้ง session ที่ยัง Scheduled เลย (edge case ตาม Q13.3) — ทั้งสองกรณี Q13.3 ต้องการผลลัพธ์เดียวกันคือ **fallback ไปนับจากวันซื้อ** พอดี ดังนั้น**ไม่จำเป็นต้องรู้ `DeliveryFormat` ของคอร์สที่ฝั่ง Commerce เลย** — เช็คแค่ "มี earliest scheduled session ไหม" ก็ครบทั้ง 3 เคสของ acceptance ในโค้ดเส้นทางเดียว (ดู §2.2 ทำไมถึงไม่แก้ `CoursePriceInfo`)

### 0.3 ทำไมไม่มี migration เลยในงานนี้

`COURSE.AccessDurationDays` (nullable `int`) และ `COURSE.DeliveryFormat` มีอยู่แล้วจริงจาก P11-01/ก่อนหน้า (`COURSE.cs:90,100`) — งานนี้แค่เปลี่ยน**สูตรคำนวณ** `ExpiresAtUtc` ตอน fulfill order เท่านั้น ไม่เพิ่มคอลัมน์ใหม่บน `COURSE` หรือ `ENROLLMENT` เลย

### 0.4 จุดคำนวณ `ExpiresAtUtc` จริงในโค้ดตอนนี้ — **มี 3 จุด ไม่ใช่ 2 จุดตามที่ task row เขียนไว้**

Grep `AccessDurationDays` ทั่ว `Siri.Modules.Commerce` เจอโค้ดซ้ำกันตัวต่อตัว (pattern เดียวกันเป๊ะ) อยู่ **3 จุด**:

1. **`StripeWebhookHandler.HandlePaymentIntentSucceededAsync`** (`StripeWebhookHandler.cs:206-214`) — เส้นทางหลัก: Stripe webhook ยืนยันจ่ายเงินสำเร็จ
2. **`OrderService.CreateAsync`'s zero-amount fast path** (`OrderService.cs:129-138`) — โปรโมชั่นโค้ดส่วนลด 100% ที่ทำให้ `TotalAmount == 0m` (mark paid + enroll ทันทีไม่ต้องรอ Stripe)
3. **`PaymentOpsQueueService.ResolveAsync`'s `ReopenAndFulfillOrder`/`GrantAccessOnly` branch** (`PaymentOpsQueueService.cs:256-263`) — แอดมินแก้ปัญหา payment ที่ค้างใน ops queue ด้วยมือ (เช่น Stripe แจ้งสำเร็จช้ากว่า order หมดอายุ) แล้วให้สิทธิ์เข้าเรียนย้อนหลัง

**`docs/TASKS.md`'s P11-13 row พูดถึงแค่จุด (1)/(2) เท่านั้น ไม่ได้พูดถึงจุด (3) เลย** — นี่คือ deviation จาก task description ที่ต้องรายงานให้เจ้าของโปรเจ็คทราบ (ดู "flag" ท้าย report ของ system-architect) แต่**ยังคงอยู่ในสโคปของ contract นี้**: ปล่อยจุด (3) ไว้ด้วยสูตรเดิม (นับจากวันที่แอดมินกดแก้ปัญหา) จะทำให้นักเรียนที่ได้รับสิทธิ์ผ่านเส้นทาง ops-queue ได้ `ExpiresAtUtc` ต่างจากคนที่ได้ผ่าน webhook/free-checkout ปกติสำหรับคอร์ส Live/Hybrid เดียวกัน — เป็น bug จริงชนิดเดียวกับที่ audit ประวัติโปรเจ็คนี้เคยพบมาแล้วหลายรอบ (logic ซ้ำ 3 ที่ แก้ไม่ครบ) จึงรวมจุด (3) เข้าสโคปเพื่อความสอดคล้องกัน — **ถ้าเจ้าของโปรเจ็คไม่ต้องการให้แก้จุด (3) ในงานนี้ ให้บอกก่อน backend-developer เริ่ม แล้ว system-architect จะออก revision ตัดสโคปนี้ออก**

---

## 1. Scope & task IDs

**อยู่ในขอบเขต (P11-13):**
- `ILiveScheduleReader` เพิ่ม 1 default method: `GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken)` (§2.1) — **ไม่แก้ `LiveScheduleReader.cs`** (implementation จริงใน Catalog.Infrastructure) เลยแม้แต่บรรทัดเดียว เพราะ default method ประกอบจาก `GetSessionsForCourseAsync` ที่มีอยู่แล้วพอ
- แก้จุดคำนวณ `ExpiresAtUtc` ทั้ง 3 จุดใน §0.4 ให้เรียก method ใหม่นี้ (§3)
- Unit test ครบ 3 เคส (มี session / ไม่มี session (ครอบคลุมทั้ง "OnDemand" และ "Live/Hybrid ที่ยังไม่ตั้ง session") / ค่า `AccessDurationDays` เป็น `null`) × ทั้ง 3 call site
- Integration test (Testcontainers PostgreSQL จริง) ยืนยัน `ENROLLMENT."ExpiresAtUtc"` จริงหลัง webhook/free-checkout เมื่อคอร์สมี session ตั้งไว้แล้ว

**นอกขอบเขต (ห้ามทำในงานนี้):**
- **Public read model** (`GET /courses/{slug}`) — ไม่มี field ใหม่ให้ผู้เรียนเห็นว่า "จะหมดอายุวันไหน" คำนวณล่วงหน้า งานนี้เป็น fulfillment-time calculation ล้วน
- **Frontend ใด ๆ** — ไม่มี response shape เปลี่ยนเลย (`ExpiresAtUtc`/`ENROLLMENT.ExpiresAtUtc` เป็น field ที่มีอยู่แล้ว แค่ค่าที่คำนวณเปลี่ยนสูตร)
- **`RefundService`/`OrderExpiryJob`** — ไม่เกี่ยวกับการคำนวณ `ExpiresAtUtc` เลย (`OrderExpiryJob` แค่ยกเลิก order ที่ยังไม่จ่ายเงิน, ไม่เคย enroll ใคร)
- **Q13.1/Q13.2 (enrollment deadline/seat cap)** = P11-11 (FROZEN, ทำไปแล้ว) — งานนี้ไม่แตะ `EnrollmentDeadlineUtc`/`MaxSeats`/`SeatsUsed` เลย
- **Q13.4 (ห้าม refund หลังเข้าเรียนสด)** = P11-12 คนละ task
- ถ้าเจ้าของโปรเจ็คตัดสินใจว่าจุด (3) (`PaymentOpsQueueService`) ไม่อยู่ในสโคป — ตัดออกตาม §0.4 ท้ายย่อหน้า

---

## 2. Schema delta

**ไม่มี** — ไม่มี migration, ไม่มีตาราง/คอลัมน์ใหม่ (ดู §0.3)

### 2.1 `ILiveScheduleReader` — เพิ่ม default method ใหม่

เพิ่มใน `src/Siri.Modules.Catalog/Contracts/ILiveScheduleReader.cs` (ต้องเพิ่ม `using System.Linq;` ที่หัวไฟล์ — ไฟล์นี้ไม่มี `using` เลยตอนนี้):

```csharp
using System.Linq;

namespace Siri.Modules.Catalog.Contracts;

public sealed record LiveSessionInfo(...);   // ไม่เปลี่ยน

public interface ILiveScheduleReader
{
    Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
    Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Earliest <see cref="LiveSessionStatus.Scheduled"/> session for <paramref name="courseId"/>
    /// by <see cref="LiveSessionInfo.StartsAtUtc"/>, or <c>null</c> if none exists (task P11-13, Q13.3 —
    /// docs/contracts/P11-13-access-duration-first-session.md). <c>null</c> covers two cases the caller
    /// must NOT try to tell apart: the course is <c>DeliveryFormat.OnDemand</c> (which can never have a
    /// Scheduled session — see <c>COURSE.AddLiveSession</c>/<c>SetDeliveryFormat</c>'s own guards), or it's
    /// Live/Hybrid but no session has been scheduled yet. Both map to the exact same Q13.3 fallback
    /// ("count access from purchase date"), so the caller needs nothing more specific than this.
    /// <para>
    /// Default method built directly on <see cref="GetSessionsForCourseAsync"/> instead of a new EF query
    /// in <c>LiveScheduleReader</c> — no other consumer needs anything more targeted than "all sessions for
    /// this course, filtered/sorted in memory", and per-course session counts are small (a handful, not
    /// thousands), so the extra round trip through the full list is not a real cost.
    /// </para></summary>
    async Task<LiveSessionInfo?> GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var sessions = await GetSessionsForCourseAsync(courseId, cancellationToken).ConfigureAwait(false);
        return sessions
            .Where(s => s.Status == LiveSessionStatus.Scheduled)
            .OrderBy(s => s.StartsAtUtc)
            .FirstOrDefault();
    }
}
```

ไม่ต้องแก้ `LiveScheduleReader.cs` (Catalog.Infrastructure) เลย — default body เพียงพอ และไม่มี implementer อื่นของ `ILiveScheduleReader` ในโค้ดเบสให้ต้องกังวลเรื่อง compile พัง (ต่างจาก `ICatalogPriceContract` ที่ P11-11 ต้องระวัง 17 ไฟล์ fake — ที่นี่มี **0 fake ที่มีอยู่ก่อน** เพราะไม่เคยมี consumer นอก Catalog มาก่อน)

### 2.2 ทำไมไม่แก้ `CoursePriceInfo`/`ICatalogPriceContract` เลย

พิจารณาแล้วปฏิเสธทางเลือก "เพิ่ม `DeliveryFormat` เข้า `CoursePriceInfo` แล้วเช็คที่ฝั่ง Commerce ก่อนเรียก `ILiveScheduleReader`" ด้วยเหตุผล 2 ข้อ:
1. **ไม่จำเป็นจริง** — §0.2 พิสูจน์แล้วว่า invariant ของ `COURSE` domain ทำให้ "มี earliest scheduled session หรือไม่" เพียงพอต่อการตัดสินใจครบทั้ง 3 เคสของ Q13.3 อยู่แล้ว
2. **ชนกับ module-boundary discipline ที่มีอยู่แล้วในโค้ดเบส** — `DeliveryFormat` เป็น enum ใน `Siri.Modules.Catalog.Domain` ไม่ใช่ `Contracts` การ expose ผ่าน `CoursePriceInfo` (record ใน `Contracts`) ตรง ๆ จะทำให้ `Siri.Modules.Commerce` มี type dependency ไปที่ `Catalog.Domain` ผ่าน `CoursePriceInfo.DeliveryFormat`'s type ซึ่ง `Siri.ArchitectureTests`' module-boundary rule (Commerce ห้าม reference `Catalog.Domain`) มีโอกาสจับได้ — เป็นเหตุผลเดียวกับที่ `LiveScheduleReader.ToLiveSessionInfo` ต้อง map `Domain.CourseLiveSessionStatus` → `Contracts.LiveSessionStatus` แยกต่างหากแทนที่จะ expose ตรง ๆ (ดู §0.1) — ถ้าจะทำจริงต้องสร้าง `Contracts.DeliveryFormat` คู่ขนานเพิ่มอีกตัว ซึ่งเป็นงานเกินความจำเป็นเมื่อเทียบกับข้อ 1

**ผลคือ `ICatalogPriceContract`/`CoursePriceInfo` ไม่ถูกแตะเลยในงานนี้** — ไม่มี test fake ตัวไหนใน 30+ จุดที่สร้าง `CoursePriceInfo(...)` ทั่ว `tests/Siri.UnitTests/Commerce/*.cs` ต้องแก้เลย

---

## 3. การแก้โค้ด 3 จุด (หัวใจของงานนี้)

ทั้ง 3 จุดเปลี่ยนจาก pattern เดิม (sync `.Select()` คำนวณ `expiresAtUtc` จาก `_clock.UtcNow`/`clock.UtcNow` ตรง ๆ) เป็น **async loop** (ต้อง `await` เรียก `ILiveScheduleReader` ต่อคอร์ส — LINQ `.Select()` แบบเดิม await ไม่ได้) พร้อม fallback เดิมเป๊ะเมื่อไม่มี session

### 3.1 `StripeWebhookHandler.cs`

**Constructor**: เพิ่ม `ILiveScheduleReader liveScheduleReader` เป็น field ใหม่ `_liveScheduleReader` — แทรกต่อจาก `catalogPriceContract` (บรรทัด 27/43/58 เดิม ก่อน `learningAccessContract`) ทั้งใน field declaration, constructor parameter list, และ assignment (3 จุดในไฟล์เดียวกัน)

**แก้ enrollment-grant block** (`StripeWebhookHandler.cs:206-214` เดิม):
```csharp
// เดิม (sync .Select()):
var enrollmentGrants = courseIds
    .Select(courseId =>
    {
        DateTime? expiresAtUtc = coursePrices.TryGetValue(courseId, out var enrolledCourseInfo) && enrolledCourseInfo.AccessDurationDays is { } days
            ? _clock.UtcNow.AddDays(days)
            : null;
        return new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAtUtc);
    })
    .ToList();
```
เปลี่ยนเป็น:
```csharp
// P11-13 (Q13.3): Live/Hybrid courses count AccessDurationDays from the first scheduled live
// session's StartsAtUtc, not the purchase date — async per course, so this can no longer be a plain
// LINQ .Select(). GetEarliestScheduledSessionAsync returning null covers both "course is OnDemand"
// and "Live/Hybrid but no session scheduled yet" — both fall back to counting from now, exactly the
// pre-P11-13 behavior (see docs/contracts/P11-13-access-duration-first-session.md §0.2).
var enrollmentGrants = new List<CourseEnrollmentGrant>(courseIds.Count);
foreach (var courseId in courseIds)
{
    DateTime? expiresAtUtc = null;
    if (coursePrices.TryGetValue(courseId, out var enrolledCourseInfo) && enrolledCourseInfo.AccessDurationDays is { } days)
    {
        var earliestSession = await _liveScheduleReader.GetEarliestScheduledSessionAsync(courseId, cancellationToken).ConfigureAwait(false);
        expiresAtUtc = earliestSession is not null
            ? earliestSession.StartsAtUtc.AddDays(days)
            : _clock.UtcNow.AddDays(days);
    }
    enrollmentGrants.Add(new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAtUtc));
}
```
โค้ดที่เหลือทั้งหมด (revenue split, receipt email) **ไม่เปลี่ยน**

### 3.2 `OrderService.cs`

**Constructor**: `OrderService` เป็น primary constructor — เพิ่ม `ILiveScheduleReader liveScheduleReader` เข้า parameter list ต่อจาก `catalogPriceContract` (บรรทัด 12 เดิม, ก่อน `learningAccessContract`):
```csharp
public sealed class OrderService(
    IOrderRepository orderRepository,
    IPromoCodeRepository promoCodeRepository,
    ICatalogPriceContract catalogPriceContract,
    ILiveScheduleReader liveScheduleReader,
    ILearningAccessContract learningAccessContract,
    IPricingEngine pricingEngine,
    IClock clock)
```
เพิ่ม `using Siri.Modules.Catalog.Contracts;` มีอยู่แล้ว (ใช้ `ICatalogPriceContract` อยู่แล้ว) — `ILiveScheduleReader` อยู่ namespace เดียวกัน ไม่ต้องเพิ่ม `using` ใหม่

**แก้ zero-amount fast path** (`OrderService.cs:129-138` เดิม, อยู่ใน `if (pricing.TotalAmount == 0m)` block):
```csharp
// เดิม:
var enrollmentGrants = command.CourseIds
    .Select(courseId =>
    {
        coursePrices.TryGetValue(courseId, out var courseInfo);
        DateTime? expiresAt = courseInfo?.AccessDurationDays.HasValue == true
            ? clock.UtcNow.AddDays(courseInfo.AccessDurationDays.Value)
            : null;
        return new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAt);
    })
    .ToList();
```
เปลี่ยนเป็น (pattern เดียวกับ §3.1 เป๊ะ):
```csharp
var enrollmentGrants = new List<CourseEnrollmentGrant>(command.CourseIds.Count);
foreach (var courseId in command.CourseIds)
{
    DateTime? expiresAt = null;
    if (coursePrices.TryGetValue(courseId, out var courseInfo) && courseInfo.AccessDurationDays is { } days)
    {
        var earliestSession = await liveScheduleReader.GetEarliestScheduledSessionAsync(courseId, cancellationToken).ConfigureAwait(false);
        expiresAt = earliestSession is not null
            ? earliestSession.StartsAtUtc.AddDays(days)
            : clock.UtcNow.AddDays(days);
    }
    enrollmentGrants.Add(new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAt));
}
```
บล็อกนี้อยู่ใน `orderRepository.ExecuteInTransactionAsync(async () => { ... })` อยู่แล้ว (delegate เป็น `async` แล้ว) — ไม่ต้องเปลี่ยนอะไรเพิ่มเพื่อรองรับ `await`

### 3.3 `PaymentOpsQueueService.cs` (ดู §0.4 ก่อนเริ่ม — ยืนยันกับเจ้าของโปรเจ็คก่อนถ้าจะตัดออก)

**Constructor**: เพิ่ม `ILiveScheduleReader liveScheduleReader` (field `_liveScheduleReader`) ต่อจาก `catalogPriceContract` (parameter ที่ 7 เดิม, บรรทัด 33) — วางไว้ก่อน `emailOutbox` และก่อน optional `IRevenueSplitContract? revenueSplitContract = null` ท้ายสุด (C# บังคับ required param มาก่อน optional param) — แก้ 3 จุดเดิม (field, constructor, assignment) เหมือน §3.1

**แก้ enrollment-grant block** (`PaymentOpsQueueService.cs:256-263` เดิม):
```csharp
// เดิม:
var enrollmentGrants = courseIds
    .Select(courseId =>
    {
        DateTime? expiresAtUtc = coursePrices.TryGetValue(courseId, out var enrolledCourseInfo) && enrolledCourseInfo.AccessDurationDays is { } days
            ? _clock.UtcNow.AddDays(days)
            : null;
        return new CourseEnrollmentGrant(courseId, order.ORDER_ID, expiresAtUtc);
    })
    .ToList();
```
เปลี่ยนเป็น pattern เดียวกับ §3.1/§3.2 เป๊ะ (ตัวแปร `_liveScheduleReader`, `_clock`)

**เมธอดที่ล้อมบล็อกนี้ต้องเป็น `async` อยู่แล้ว** (มี `await _catalogPriceContract...`/`await _learningAccessContract...` ข้างเคียงอยู่แล้ว) — ยืนยันก่อนแก้ว่า `ResolveAsync` (หรือชื่อจริงของ method ที่ครอบ switch-case นี้) เป็น `async Task<...>` จริง ไม่ใช่ sync wrapper

### 3.4 Call site ที่ต้องแก้เพิ่ม (constructor เปลี่ยน parameter list)

**Production**: ไม่ต้องแก้ DI registration ใน `CommerceModule.cs` เลย (`services.AddScoped<OrderService>()`/`AddScoped<StripeWebhookHandler>()`/`AddScoped<PaymentOpsQueueService>()` ไม่มี factory เดิม — container resolve constructor ใหม่ให้เองอัตโนมัติ, ยืนยันแล้วว่า `ILiveScheduleReader` ลงทะเบียน scoped ไว้แล้วใน `CatalogModule.cs:221`)

**Test — ยืนยันแล้วว่ามี call site ที่ต้องแก้ทั้งหมดแค่นี้ (grep ทั่ว repo แล้ว ไม่มีที่อื่น)**:
- `tests/Siri.UnitTests/Commerce/OrderServiceTests.cs:257` (`CreateOrderService` helper — เรียกครั้งเดียว)
- `tests/Siri.UnitTests/Commerce/StripeWebhookHandlerTests.cs:44` (`CreateHandler` helper — เรียกครั้งเดียว)
- `tests/Siri.UnitTests/Commerce/PaymentOpsQueueServiceTests.cs` (`CreateService` helper — เรียกครั้งเดียว)
- `tests/Siri.IntegrationTests/PaymentFulfillmentIntegrationTests.cs:129` (`new OrderService(...)`) และ `:344` (`CreateHandler` ภายในไฟล์นี้เอง, สร้าง `StripeWebhookHandler` ตรง — คนละ helper จาก unit test)

ทุกจุดเป็น real EF-backed `LiveScheduleReader(db)` ในไฟล์ integration test (มี `using Siri.Modules.Catalog.Infrastructure;` อยู่แล้ว, mirror `new CatalogPriceContract(db)`/`new LearningAccessContract(...)` ที่มีอยู่แล้วในไฟล์เดียวกัน) — **ไม่ต้อง fake** ที่ integration test เพราะต่อ Postgres จริงผ่าน Testcontainers อยู่แล้ว

**ไม่มี `PaymentOpsQueueService` ใน integration test เลย** (grep ยืนยันแล้ว 0 ผลลัพธ์) — ไม่มี call site เพิ่มเติมสำหรับจุดนี้

---

## 4. Test double ใหม่ — `FakeLiveScheduleReader`

เพิ่มในทั้ง 3 unit test ไฟล์ (`OrderServiceTests.cs`/`StripeWebhookHandlerTests.cs`/`PaymentOpsQueueServiceTests.cs` — แยกคลาสต่อไฟล์ ตาม convention เดิมที่แต่ละไฟล์มี `FakeCatalogPriceContract` เป็นของตัวเองอยู่แล้ว ไม่ share กัน):

```csharp
private sealed class FakeLiveScheduleReader : ILiveScheduleReader
{
    public readonly Dictionary<Guid, LiveSessionInfo> EarliestSessionByCourseId = [];

    public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by this handler/service under test — only GetEarliestScheduledSessionAsync is called.");

    public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by this handler/service under test — only GetEarliestScheduledSessionAsync is called.");

    public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by this handler/service under test — only GetEarliestScheduledSessionAsync is called.");

    // Explicit override of the default interface method — same shape as FakeCatalogPriceContract's
    // override of ICatalogPriceContract's GetEnrollmentPoliciesAsync/TryReserveSeatAsync/ReleaseSeatAsync
    // in P11-11 — simple per-course lookup instead of composing GetSessionsForCourseAsync in the fake.
    public Task<LiveSessionInfo?> GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult(EarliestSessionByCourseId.TryGetValue(courseId, out var session) ? session : null);
}
```

ตัวอย่างสร้าง `LiveSessionInfo` ในเทสต์:
```csharp
new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", startsAtUtc, startsAtUtc.AddHours(1), LiveSessionStatus.Scheduled, null)
```

---

## 5. Integration checklist (สำหรับ integrator-qa)

**Unit test ใหม่ที่ต้องมี — 3 เคส × 3 call site (9 เทสต์ใหม่ขั้นต่ำ):**
1. **มี earliest scheduled session** — `AccessDurationDays = 30`, `FakeLiveScheduleReader.EarliestSessionByCourseId[courseId]` ตั้งเป็น session ที่ `StartsAtUtc` เป็นอนาคต (เช่น +10 วันจาก clock) → `ExpiresAtUtc` ต้องเท่ากับ `session.StartsAtUtc.AddDays(30)` **ไม่ใช่** `clock.UtcNow.AddDays(30)` (ต้อง assert ตัวเลขจริงต่างกันชัดเจน กันเทสต์ผ่านหลอกเพราะบังเอิญตรงกัน)
2. **ไม่มี session เลย (ครอบคลุมทั้ง OnDemand และ Live/Hybrid ที่ยังไม่ตั้ง session)** — `EarliestSessionByCourseId` ไม่มี entry สำหรับ courseId นี้ (ค่า default ว่างเปล่า) `AccessDurationDays = 30` → `ExpiresAtUtc` ต้องเท่ากับ `clock.UtcNow.AddDays(30)` เป๊ะ (regression: พฤติกรรมก่อน P11-13 ทั้งหมด) — เขียน comment กำกับในเทสต์ว่านี่คือทั้งเคส "OnDemand" และ "Live/Hybrid ไม่มี session" เพราะโค้ดจริงแยกสองเคสนี้ไม่ออก (ดู contract §0.2)
3. **`AccessDurationDays = null`** (lifetime access) — `ExpiresAtUtc` ต้องเป็น `null` เสมอไม่ว่า `FakeLiveScheduleReader` จะตั้ง session ไว้หรือไม่ (ต้อง**ไม่**เรียก `GetEarliestScheduledSessionAsync` เลยด้วยซ้ำ — พิสูจน์ได้ถ้า fake โยน `NotSupportedException` ทุก method ยกเว้นที่ override เอง แต่ `GetEarliestScheduledSessionAsync` ที่ override ไว้จะไม่ throw ก็จริง — ให้เพิ่ม flag `bool WasCalled` ใน fake หรือ assert ทางอ้อมว่าผลลัพธ์ `ExpiresAtUtc == null` พอ เพราะโค้ด §3 เขียนเป็น `if (... AccessDurationDays is { } days)` ที่ short-circuit อยู่แล้วตาม design)

**Integration test ใหม่ (Testcontainers PostgreSQL 17 จริง, ห้าม InMemory):**
- ต่อยอด `PaymentFulfillmentIntegrationTests.cs`: สร้างคอร์ส `DeliveryFormat.Hybrid` + `AccessDurationDays` ตั้งไว้ + `AddLiveSession` หนึ่งคาบในอนาคต → ยิง webhook `payment_intent.succeeded` จริงผ่าน `StripeWebhookHandler` → query `ENROLLMENT` ตรงจาก DB ยืนยัน `ExpiresAtUtc` ตรงกับ `session.StartsAtUtc.AddDays(accessDurationDays)` เป๊ะ (ไม่ใช่ใกล้เคียง `DateTime.UtcNow` ตอนรันเทสต์)
- เคสเดียวกันสำหรับ `OrderService.CreateAsync`'s zero-amount path (โปรโมชั่น 100%)
- Regression: คอร์ส `DeliveryFormat.OnDemand` ที่มี `AccessDurationDays` ตั้งไว้ ผ่าน flow เดิมทั้งหมด (webhook + free-checkout) → `ExpiresAtUtc` ยังคำนวณจากเวลาที่ webhook/order ประมวลผลเหมือนก่อน P11-13 ทุกจุด (proof ว่าไม่ regression คอร์สส่วนใหญ่ในระบบซึ่งยังเป็น OnDemand ทั้งหมด)

**จุดเสี่ยงที่ต้อง field-by-field:**
- Assert ด้วย**ค่าตัวเลขจริง** (`session.StartsAtUtc.AddDays(days)`) ไม่ใช่แค่ "ไม่เท่ากับ null"/"เป็นวันที่ในอนาคต" — ป้องกันเทสต์ผ่านหลอกถ้า implementer ลืมเปลี่ยนสูตรจริงแต่บังเอิญได้ค่าที่ "ดูสมเหตุสมผล"
- ยืนยันว่า `CoursePriceInfo`/`ICatalogPriceContract` **ไม่ถูกแก้เลยแม้แต่บรรทัดเดียว** (ตรงตาม §2.2) — grep `git diff` ของ PR ต้องไม่มี `ICatalogPriceContract.cs`/`CatalogPriceContract.cs` อยู่ในรายการไฟล์ที่แก้
- ยืนยันว่า `LiveScheduleReader.cs` (Catalog.Infrastructure ตัวจริง) **ไม่ถูกแก้เลย** — ยืนยัน default method ใน `ILiveScheduleReader.cs` ทำงานถูกต้องผ่าน concrete implementation เดิมโดยไม่ต้อง override
- 403/404/ownership: **ไม่มีใน scope นี้เลย** — ไม่มี endpoint ใหม่ ไม่มี auth policy ใหม่ ไม่มี rate limit ใหม่ (งานนี้ไม่แตะ API surface เลย)
- ยืนยัน §0.4's จุด (3) (`PaymentOpsQueueService`) ได้รับการยืนยันจากเจ้าของโปรเจ็คแล้วก่อน merge (ดูหมายเหตุท้าย §0.4) — ถ้าเจ้าของโปรเจ็คสั่งตัดออก ให้ลบเฉพาะ §3.3 + เทสต์คู่ของมันออกจาก PR แต่ §3.1/§3.2 ยังทำตามปกติ
- Build 0 warning/0 error, unit+architecture test เขียวเต็ม, integration test ใหม่ต้องรันผ่านจริงด้วย Docker (ไม่ใช่แค่ compile แล้วข้ามด้วย `DockerUnavailableException`) — ถ้าเครื่อง QA ไม่มี Docker ให้รายงานตามจริงว่ายังไม่เคยรันผ่านจริง อย่าบอกว่า "ผ่านแล้ว"

## Changelog

(ยังไม่มี revision หลัง FROZEN)
