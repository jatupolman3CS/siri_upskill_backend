# Contract: P11-11 ปิดรับสมัคร + เพดานที่นั่ง (Q13.1/Q13.2)

Status: FROZEN · วันที่: 2026-09-16 · Module: `Siri.Modules.Catalog` (vertical slice, `Features/{UseCase}` handlers — entity class `COURSE` is UPPERCASE, C# **property stays PascalCase**, same as every other Catalog field; see `COURSE.cs`'s own doc comment and `docs/contracts/P11-01-catalog-live-sessions.md`'s "⚠️ แก้ข้อสมมติ" section if this is confusing) + `Siri.Modules.Commerce` (Repository+Service, UPPERCASE entity/DB — `OrderService`, `ORDER`, `ORDER_ITEM`)

ผู้ลงมือที่แนะนำ: **backend-developer (Claude)** — งานนี้ (ก) แตะเงิน/checkout โดยตรง (`OrderService.CreateAsync`/`CancelAsync`, `OrderExpiryJob`) (ข) เพิ่ม method ใหม่ใน `Siri.Modules.Catalog.Contracts.ICatalogPriceContract` ซึ่งเป็น cross-module `Contracts/` interface — ทั้งสองข้อตรง charter ของ system-architect ที่ต้องส่งให้ Claude ไม่ใช่ Antigravity ตรงกับ `docs/TASKS.md`'s P11-11 row (`Owner: CC`) อยู่แล้ว ไม่มีงานฝั่ง FE ในสโคปนี้เลย (ดู §0 ท้ายเรื่อง scope)

**DESIGN_DATABASE/DATABASE: ไม่จำเป็น** — schema delta คือ 3 คอลัมน์ใหม่บนตารางเดิม (`CATALOG.COURSES`) ไม่มีตารางใหม่ ไม่มี FK ใหม่ ไม่มี index ใหม่ (ดูเหตุผลใน §2) — §2 ด้านล่างละเอียดระดับคอลัมน์พร้อมเหตุผลแล้ว ให้ backend-developer แก้ entity+configuration+migration ต่อจาก §2 ได้ทันที

---

## 0. สิ่งที่ยืนยันจากโค้ดจริงแล้ว (ไม่เดา)

- **`src/Siri.Modules.Catalog/Domain/COURSE.cs`** — ไม่มี `EnrollmentDeadlineUtc`/`MaxSeats`/`SeatsUsed` อยู่เลยตอนนี้ ฟิลด์ทั้งสามเป็นของใหม่ล้วน `SetAccessDuration` (บรรทัด ~256) คือ precedent ของ validation style ที่ต้องเลียนแบบ (`if (x is <= 0) throw ArgumentOutOfRangeException`)
- **`src/Siri.Modules.Catalog/Features/CreateLiveSession/Handler.cs:58-60`** และ **`UpdateLiveSession/Handler.cs:66`** — precedent จริงของการเช็ค `DateTimeKind.Utc` ที่ชั้น Handler (ไม่ใช่ FluentValidation Validator) แล้วคืน `DomainError.Validation(...)` — โค้ดใหม่ต้องเลียนแบบ pattern นี้เป๊ะ ไม่ใช่ย้ายไปเช็คใน Validator
- **`src/Siri.Modules.Catalog/Features/SetCourseDeliveryFormat/{Command,Validator,Handler,Endpoint,Response}.cs`** + **`src/Siri.Api/Controllers/Catalog/InstructorCoursesController.cs:506-530`** — คู่ตัวอย่างเต็มรูปแบบของ endpoint ที่ทำ ownership check + domain call + MVC controller + minimal-API mapper คู่กัน ใช้เป็นแม่แบบตรง ๆ ของงานนี้ (ดู §3)
- **`src/Siri.Modules.Commerce/Application/OrderService.cs`** อ่านทั้งไฟล์แล้ว — `CreateAsync` (บรรทัด 28-113) มี `ICatalogPriceContract catalogPriceContract` inject อยู่แล้ว (ไม่ต้องเพิ่ม DI ใหม่ที่นี่) เรียก `pricingEngine.CalculatePricingAsync` ที่บรรทัด 46 **หลัง**เช็ค duplicate-enrollment (บรรทัด 39-44) เท่านั้น — ยังไม่มีการเช็ค deadline/seat cap เลยสักจุด `CancelAsync` (บรรทัด 115-138) **ไม่ได้ห่อด้วย `ExecuteInTransactionAsync` เลย** (mutate + `SaveChangesAsync` ตรง ๆ) ต่างจาก `CreateAsync`/`OrderExpiryJob.ExpireOrderAsync` ที่ห่อไว้ — งานนี้ต้องแก้จุดนี้ด้วย (ดู §1.3)
- **`src/Siri.Modules.Commerce/Infrastructure/PromoCodeRepository.cs:49-58`** (`TryRedeemAsync`'s stage 2) — **นี่คือ precedent ของ atomic-SQL-counter ที่มีอยู่แล้วจริงในโค้ดเบสนี้**: `dbContext.PromoCodes().Where(p => ... && p.REDEEMED_COUNT < p.MAX_REDEMPTIONS).ExecuteUpdateAsync(s => s.SetProperty(p => p.REDEEMED_COUNT, p => p.REDEEMED_COUNT + 1), ct)` แล้วเช็ค `affected == 0`. งานนี้ทำ pattern เดียวกันเป๊ะกับ `COURSES.SeatsUsed`/`MaxSeats` — **ไม่ต้องคิด pattern ใหม่**
- **`src/Siri.Modules.Commerce/Infrastructure/OrderRepository.cs:61-93`** (`ExecuteInTransactionAsync<T>`) — commit/rollback ตัดสินจาก `result is Result { IsSuccess: false }` เท่านั้น การคืน `Result.Failure<OrderResponse>(...)` จากใน delegate จึงพอให้ rollback อัตโนมัติ ไม่ต้องเขียน compensating-undo เอง
- **`src/Siri.Modules.Commerce/Infrastructure/OrderExpiryJob.cs:77-115`** (`ExpireOrderAsync`) — ห่อ transaction อยู่แล้ว, revert promo code ที่บรรทัด 107-110 ก่อน `SaveChangesAsync` — เป็นจุดคู่ขนานที่ต้อง release seat ด้วยเหตุผลเดียวกับที่ต้อง revert promo code
- **`ICatalogPriceContract.cs`** มี default-interface-method หลายตัวอยู่แล้ว (เช่น `GetMediaAssetIdForEpisodeAsync`, `GetCourseIdsByInstructorUserIdAsync`, `GetEpisodesForCoursesAsync`) เพื่อเพิ่ม method ใหม่โดยไม่ทำให้ implementer เดิมพัง — **grep แล้วมี 17 ไฟล์ทดสอบที่ implement `ICatalogPriceContract` เป็น fake** (`tests/Siri.UnitTests/{Analytics,Commerce,Community,Learning,Media,Payout}/*.cs`) การเพิ่ม method ใหม่แบบ **ไม่มี default body** จะทำให้ compile พังทั้ง 17 ไฟล์ — งานนี้ต้องใช้ default-interface-method pattern เดียวกันเพื่อไม่แตะไฟล์เหล่านั้นเลย ยกเว้น `FakeCatalogPriceContract` ใน `tests/Siri.UnitTests/Commerce/OrderServiceTests.cs` ที่ต้อง override จริงเพื่อเขียนเทสต์ใหม่ของงานนี้ (ดู §5)

---

## 1. Scope & task IDs

**อยู่ในขอบเขต (P11-11):**
- `COURSE.EnrollmentDeadlineUtc` (nullable `DateTime`), `COURSE.MaxSeats` (nullable `int`), `COURSE.SeatsUsed` (`int`, default 0) — ใหม่ทั้งสาม, ใช้ได้กับคอร์สทุก `DeliveryFormat` (ไม่ผูกกับ Live/Hybrid — ดูเหตุผลใน §1.1)
- Domain method `COURSE.SetEnrollmentPolicy(DateTime? enrollmentDeadlineUtc, int? maxSeats)` (ดู §2.3)
- `PUT /api/catalog/instructor/courses/{id}/enrollment-policy` (ดู §3)
- `ICatalogPriceContract` เพิ่ม 3 method: `GetEnrollmentPoliciesAsync`, `TryReserveSeatAsync`, `ReleaseSeatAsync` (ดู §2.4) + implementation ใน `CatalogPriceContract.cs`
- แก้ `OrderService.CreateAsync` (deadline check + atomic seat reservation), `OrderService.CancelAsync` (seat release + ห่อ transaction ที่ขาดอยู่), `OrderExpiryJob.ExpireOrderAsync` (seat release) — ดู §1.2/1.3
- Migration `AddCourseEnrollmentDeadlineAndSeatCap`
- Unit test: `COURSE.SetEnrollmentPolicy` ทุก branch, `SetCourseEnrollmentPolicyCommandValidator`, `OrderService` (deadline-passed → 409 ก่อนเรียก pricing, seat-full → 409 ก่อนสร้าง order, OnDemand/ไม่ตั้งค่าไม่กระทบ) — extend `FakeCatalogPriceContract` ใน `OrderServiceTests.cs`
- Integration test (Testcontainers Postgres, ห้าม InMemory): race ที่นั่งสุดท้าย, deadline ผ่านแล้ว 409, cancel/expire คืนที่นั่ง, OnDemand ไม่กระทบ — ดู §5

**นอกขอบเขต (ห้ามทำในงานนี้):**
- **Public read model** (`GET /courses/{slug}`, `courses/search`) — ไม่เพิ่ม `enrollmentDeadlineUtc`/`maxSeats`/`seatsUsed` ในนั้นเลย งานนี้เป็น BE enforcement ล้วน ไม่มีฝั่งแสดงผลให้ผู้เรียนเห็น "เหลือ N ที่นั่ง"/"ปิดรับสมัครใน X วัน" — เป็น task ในอนาคตถ้าต้องการ (P11-11 ไม่ได้ขอ และไม่มี acceptance ข้อไหนพูดถึง read model เลย)
- **Frontend ใด ๆ** — ไม่มี UI ให้ผู้สอนตั้งค่าฟิลด์เหล่านี้ในงานนี้ (course builder UI เป็นงานแยก ถ้าต้องการภายหลัง)
- **Refund หลังจ่ายเงินแล้ว** — `SeatsUsed` ไม่ถูกลดเมื่อ order ที่ `Paid` แล้วถูก refund (ดูเหตุผลใน §1.4) — คนละเรื่องกับ Q13.4 (ห้าม refund หลังเข้าเรียนสด, P11-12) ซึ่งเป็น task อื่น
- Q13.3 (`AccessDurationDays` นับจาก live session แรก) = **P11-13** คนละ task — งานนี้ไม่แตะ `AccessDurationDays`/`ExpiresAtUtc` เลย

### 1.1 ทำไม EnrollmentDeadlineUtc/MaxSeats ไม่ผูกกับ DeliveryFormat

อ่าน Q13.1/Q13.2 ใน `docs/DECISIONS.md` แล้ว (บรรทัด ~165-166) ทั้งสองข้อเขียนว่า **"ผู้สอนตั้งเองต่อคอร์ส"** โดยไม่มีเงื่อนไขจำกัดที่ `DeliveryFormat` เลย (ต่างจาก Q13.3 ที่เขียนชัดว่า "สำหรับคอร์ส Live/Hybrid เท่านั้น") — แม้ชื่องานจะอยู่ในเวฟ P11 (Hybrid Live) แต่ฟีเจอร์นี้ทั่วไปกว่านั้น (เช่น cohort-based OnDemand ที่อยากปิดรับสมัครหรือจำกัดที่นั่งก็ทำได้) จึงไม่ใส่ guard ผูกกับ `DeliveryFormat` ใน `SetEnrollmentPolicy`/endpoint นี้ ข้อความ acceptance เดิม **"คอร์ส OnDemand ไม่ถูกกระทบ (ไม่มี field พวกนี้แสดงผล)"** อ่านแล้วหมายถึง **regression safety** (คอร์สเดิมที่ไม่เคยตั้งค่าพวกนี้ = ค่า default null/0 = enforcement เป็น no-op โดยอัตโนมัติ) ไม่ใช่ "ห้ามใช้ฟีเจอร์นี้กับ OnDemand" — ถ้าตีความต่างจากนี้ ต้องถามเจ้าของโปรเจ็คก่อน เพราะเป็นการอ่านสเปคคนละแบบที่กระทบ endpoint guard โดยตรง

---

## 2. Schema delta

### 2.1 `CATALOG.COURSES` — คอลัมน์ใหม่ 3 ตัว

| Column | Type | Nullable | Default | หมายเหตุ |
|---|---|---|---|---|
| `ENROLLMENT_DEADLINE_UTC` | `timestamptz(3)` | NULL | — | `null` = ไม่ปิดรับสมัคร (ค่า default ของทุกคอร์สเดิม) |
| `MAX_SEATS` | `integer` | NULL | — | `null` = ไม่จำกัดที่นั่ง (ค่า default ของทุกคอร์สเดิม) |
| `SEATS_USED` | `integer` | NOT NULL | `0` | นับ order ที่ "จอง" ที่นั่งไปแล้ว (เพิ่มตอนสร้าง order ที่มี `MaxSeats` ตั้งไว้ ไม่ว่า order จะจ่ายเงินสำเร็จหรือยัง — ลดลงเมื่อ order ถูกยกเลิก/หมดอายุก่อนจ่าย, ดู §1.4) |

C# property ใหม่บน `COURSE` (PascalCase ปกติ — Catalog ไม่ใช่ UPPERCASE-property module, ดู header):
```csharp
public DateTime? EnrollmentDeadlineUtc { get; private set; }
public int? MaxSeats { get; private set; }
public int SeatsUsed { get; private set; }
```

`CourseConfiguration.cs` เพิ่ม (วางต่อจากบล็อก `RejectionReason`/ก่อนบล็อก stats — ตำแหน่งไม่สำคัญมาก แต่ให้อยู่ใกล้ property ที่เกี่ยวข้องกัน):
```csharp
builder.Property(c => c.EnrollmentDeadlineUtc).HasPrecision(3);
builder.Property(c => c.MaxSeats);
builder.Property(c => c.SeatsUsed).IsRequired().HasDefaultValue(0);
```

**ไม่ต้องเพิ่ม index ใหม่**: ทุกจุดที่อ่าน/เขียนคอลัมน์เหล่านี้กรองด้วย `Id` (PK) เสมอ (`WHERE "ID" = @courseId AND (...)`) ไม่มี query ไหนสแกนทั้งตาราง `COURSES` ด้วยเงื่อนไขของคอลัมน์เหล่านี้เลย

**Migration**: `AddCourseEnrollmentDeadlineAndSeatCap` — รันคำสั่งมาตรฐานของ repo นี้:
```
dotnet ef migrations add AddCourseEnrollmentDeadlineAndSeatCap --project src/Siri.Persistence --startup-project src/Siri.Api
```
อ่าน migration ที่ generate ออกมาก่อนใช้ตาม database.md เสมอ — ควรเห็นแค่ `AddColumn` 3 บรรทัดใน `Up()` (ไม่มี `DropColumn`/`AlterColumn`/shadow property ใด ๆ) เพราะเป็น additive ล้วนบนตารางเดิม ไม่กระทบแถวที่มีอยู่ (ทุกคอร์สเดิมได้ `NULL, NULL, 0` อัตโนมัติ)

### 2.2 ทำไมไม่ต้องผ่าน DESIGN_DATABASE

ไม่มีตารางใหม่ ไม่มี FK ใหม่ (ไม่มี relationship ข้าม entity เลย — ทั้งสามคอลัมน์เป็น scalar property ธรรมดาบน `COURSE` เอง) ไม่มี cascade path ให้ต้องคิด ไม่มี index strategy ที่ซับซ้อน — เข้าเงื่อนไข "single simple column addition" ตาม system-architect's charter ตรง ๆ

### 2.3 Domain method — `COURSE.SetEnrollmentPolicy`

วางต่อจาก `SetAccessDuration` (มี validation style เดียวกัน — `ArgumentOutOfRangeException` สำหรับค่าตัวเลขผิด, `ArgumentException` สำหรับ non-UTC `DateTime` ตาม precedent ของ `ValidateSessionWindow`/`AddLiveSession`):

```csharp
/// <summary>ตั้งนโยบายปิดรับสมัคร/เพดานที่นั่ง (task P11-11, Q13.1/Q13.2 — docs/contracts/P11-11-enrollment-deadline-seat-cap.md).
/// ใช้ได้กับคอร์สทุก DeliveryFormat โดยตั้งใจ ไม่ผูกกับ Live/Hybrid (ดู contract §1.1). ลด maxSeats ให้ต่ำกว่า
/// SeatsUsed ปัจจุบันได้โดยไม่ throw — แค่หยุดขายที่นั่งใหม่ ไม่กระทบคนที่ enroll ไปแล้ว (ตัดสินใจแล้ว ไม่ใช่ช่องโหว่ที่ลืมเช็ค).
/// SeatsUsed เองไม่ถูกแตะโดย method นี้เลย (แก้ผ่าน ICatalogPriceContract.TryReserveSeatAsync/ReleaseSeatAsync
/// ด้วย raw ExecuteUpdateAsync จาก Commerce เท่านั้น — ดู contract §2.4).</summary>
public void SetEnrollmentPolicy(DateTime? enrollmentDeadlineUtc, int? maxSeats)
{
    if (enrollmentDeadlineUtc is { Kind: not DateTimeKind.Utc })
    {
        throw new ArgumentException(
            "enrollmentDeadlineUtc must be UTC (database.md: Npgsql throws on non-Utc DateTime).",
            nameof(enrollmentDeadlineUtc));
    }

    if (maxSeats is <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(maxSeats), maxSeats, "maxSeats must be a positive integer, or null for unlimited seats.");
    }

    EnrollmentDeadlineUtc = enrollmentDeadlineUtc;
    MaxSeats = maxSeats;
}
```

ไม่ต้องเช็ค `Status`/`DeliveryFormat`/deadline-ในอดีตหรือไม่ — ตั้งใจปล่อยให้ตั้งค่าย้อนเวลาได้ (เช่น ปิดรับสมัครทันทีฉุกเฉินโดยตั้ง deadline เป็นอดีต) และใช้ได้กับคอร์สทุกสถานะยกเว้น `Archived` (guard นี้อยู่ที่ Handler ไม่ใช่ domain method — ดู §3, มิเรอร์ `SetCourseDeliveryFormatHandler`'s `Archived` guard เป๊ะ)

### 2.4 `ICatalogPriceContract` — เพิ่ม 3 method (มี default body ทั้งหมด — ดู §0 เรื่อง 17 ไฟล์ fake)

เพิ่มใน `src/Siri.Modules.Catalog/Contracts/ICatalogPriceContract.cs`:

```csharp
public sealed record CourseEnrollmentPolicyInfo(Guid CourseId, DateTime? EnrollmentDeadlineUtc, int? MaxSeats, int SeatsUsed);

public interface ICatalogPriceContract
{
    // ... methods เดิมทั้งหมดไม่เปลี่ยน ...

    /// <summary>อ่านนโยบายปิดรับสมัคร/เพดานที่นั่งของ courseIds ที่ระบุ — คืนเฉพาะ courseId ที่มีอยู่จริง (ไม่ throw
    /// ถ้าบาง id ไม่พบ — ผู้เรียก เช่น OrderService ต้องพึ่งจุดอื่นที่เช็ค "คอร์สมีอยู่จริงและ Published" อยู่แล้ว เช่น
    /// GetPublishedCoursePricesAsync's count check ใน PricingEngine, method นี้ไม่ทำซ้ำ). Default: dictionary ว่าง
    /// (= ไม่มีนโยบายอะไรเลยสำหรับทุกคอร์ส, ปลอดภัยสำหรับ implementer เดิมที่ไม่รู้จัก method นี้).</summary>
    Task<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>> GetEnrollmentPoliciesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>>(new Dictionary<Guid, CourseEnrollmentPolicyInfo>());

    /// <summary>
    /// จองที่นั่งแบบ atomic ที่ระดับ SQL หนึ่งที่นั่งบน courseId — SQL เดียวกับ pattern ที่มีอยู่แล้วจริงใน
    /// PromoCodeRepository.TryRedeemAsync's global-quota step (ExecuteUpdateAsync +
    /// WHERE MaxSeats IS NULL OR SeatsUsed &lt; MaxSeats แล้วเช็ค affected rows): เมื่อเรียกจากภายใน
    /// IOrderRepository.ExecuteInTransactionAsync การจองและการสร้าง order จะ atomic ร่วมกัน (rollback พร้อมกัน
    /// ถ้าขั้นตอนอื่นในธุรกรรมเดียวกันล้มเหลวทีหลัง). คืน true = จองสำเร็จ (นับรวมกรณี MaxSeats เป็น null ซึ่งจอง
    /// สำเร็จเสมอแต่ SeatsUsed ยังถูกนับเพิ่มไว้จริง เผื่อวันหลังผู้สอนเพิ่งมาตั้ง MaxSeats ทีหลัง ตัวนับจะสะท้อนของจริง
    /// ไม่ใช่เริ่มนับจาก 0 ใหม่). คืน false = เต็มแล้ว (หรือ courseId ไม่มีอยู่จริง). Default: true เสมอ (ปลอดภัยสำหรับ
    /// implementer เดิม — เท่ากับไม่มีการจำกัดที่นั่ง).
    /// </summary>
    Task<bool> TryReserveSeatAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>คืนที่นั่งหนึ่งที่ (SeatsUsed - 1, floor ที่ 0 ด้วย WHERE SeatsUsed &gt; 0 — ไม่มีทางติดลบ) — เรียกคู่กับ
    /// ทุกจุดที่ order ที่เคยเรียก TryReserveSeatAsync สำเร็จถูกยกเลิก/หมดอายุ**ก่อนจ่ายเงิน** เท่านั้น (ดู contract
    /// §1.4 ว่าทำไม refund หลังจ่ายเงินไม่เรียก method นี้). ปลอดภัยเรียกซ้ำ/เรียกกับคอร์สที่ไม่มี MaxSeats (no-op
    /// ทั้งคู่). Default: no-op (ปลอดภัยสำหรับ implementer เดิม).</summary>
    Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

**Implementation** ใน `CatalogPriceContract.cs`:

```csharp
public async Task<IReadOnlyDictionary<Guid, CourseEnrollmentPolicyInfo>> GetEnrollmentPoliciesAsync(
    IEnumerable<Guid> courseIds,
    CancellationToken cancellationToken)
{
    var idList = courseIds.Distinct().ToList();
    if (idList.Count == 0)
    {
        return new Dictionary<Guid, CourseEnrollmentPolicyInfo>();
    }

    var policies = await dbContext.Courses()
        .AsNoTracking()
        .Where(c => idList.Contains(c.Id))
        .Select(c => new CourseEnrollmentPolicyInfo(c.Id, c.EnrollmentDeadlineUtc, c.MaxSeats, c.SeatsUsed))
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

    return policies.ToDictionary(p => p.CourseId);
}

public async Task<bool> TryReserveSeatAsync(Guid courseId, CancellationToken cancellationToken)
{
    var affected = await dbContext.Courses()
        .Where(c => c.Id == courseId && (c.MaxSeats == null || c.SeatsUsed < c.MaxSeats))
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.SeatsUsed, c => c.SeatsUsed + 1), cancellationToken)
        .ConfigureAwait(false);

    return affected == 1;
}

public async Task ReleaseSeatAsync(Guid courseId, CancellationToken cancellationToken)
{
    await dbContext.Courses()
        .Where(c => c.Id == courseId && c.SeatsUsed > 0)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.SeatsUsed, c => c.SeatsUsed - 1), cancellationToken)
        .ConfigureAwait(false);
}
```

หมายเหตุสำคัญ: `ExecuteUpdateAsync` เขียนตรงลง DB ทันที ข้าม change tracker/interceptor ทั้งหมด (**`RowVersion`/`UpdatedAtUtc`/`UpdatedBy` ไม่ถูกแตะ**) — เป็นพฤติกรรมเดียวกับ `PROMO_CODE.REDEEMED_COUNT` ที่มีอยู่แล้วจริง ไม่ใช่บั๊กใหม่ ปลอดภัยเพราะ Commerce ไม่เคย track `COURSE` entity ผ่าน EF เลย (ไม่มี reference ไปที่ `Siri.Modules.Catalog.Domain` เลยด้วยซ้ำ — เข้าถึงได้แค่ผ่าน `Contracts/` เท่านั้น) จึงไม่มี tracked-entity ตัวไหนใน DbContext เดียวกันที่จะเห็นค่าเก่าค้างแล้วเขียนทับตอน `SaveChangesAsync` ภายหลัง

---

## 3. API contract

### `PUT /api/catalog/instructor/courses/{id}/enrollment-policy`

- Auth: class-level `[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]` (มีอยู่แล้วบน `InstructorCoursesController`) + ownership check ใน handler (ดูล่าง) — ไม่ต้อง rate-limit policy เพิ่ม (endpoint นี้ไม่ใช่กลุ่มเสี่ยงตาม security.md's รายชื่อ — ไม่ใช่ login/refresh/playback/checkout/webhook)
- Route/DI wiring: มิเรอร์ `SetCourseDeliveryFormat` เป๊ะ — เพิ่ม feature folder `src/Siri.Modules.Catalog/Features/SetCourseEnrollmentPolicy/{Command,Validator,Handler,Endpoint,Response}.cs` + controller method ใหม่ใน `InstructorCoursesController.cs` + ลงทะเบียนใน `CatalogModule.cs` อีก 3 จุด (validator DI, handler DI, `instructorCourseGroup.MapSetCourseEnrollmentPolicyEndpoint()`) — จุดเดียวกับที่ `SetCourseDeliveryFormat` ลงทะเบียนอยู่ (`CatalogModule.cs` บรรทัด ~226/231/328)

**Request** (`SetCourseEnrollmentPolicyCommand`):
```json
{ "enrollmentDeadlineUtc": "2026-12-01T00:00:00Z", "maxSeats": 50 }
```
ทั้งสองฟิลด์ nullable/optional (`null` = ไม่จำกัด/ไม่ปิดรับสมัคร) — validator (`SetCourseEnrollmentPolicyCommandValidator`):
```csharp
RuleFor(c => c.MaxSeats).GreaterThan(0).When(c => c.MaxSeats.HasValue)
    .WithMessage("maxSeats must be a positive integer.");
```
(ไม่ validate `Kind` ที่นี่ — ทำที่ Handler ตาม precedent ของ `CreateLiveSession`/`UpdateLiveSession`, ดู §0)

**Handler logic** (มิเรอร์ `SetCourseDeliveryFormatHandler` ทุกจุดที่ทำได้):
1. โหลด course ด้วย `dbContext.Courses().FirstOrDefaultAsync(c => c.Id == id, ct)` → ไม่พบ → `DomainError.NotFound("ไม่พบคอร์สนี้")`
2. โหลด `InstructorProfile` ของ `userId` → ไม่มี หรือ `course.InstructorId != profile.Id` → `DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้")`
3. `course.Status == CourseStatus.Archived` → `DomainError.Conflict($"Cannot change enrollment policy of a course in {course.Status} status.")`
4. `command.EnrollmentDeadlineUtc is { Kind: not DateTimeKind.Utc }` → `DomainError.Validation("enrollmentDeadlineUtc must be UTC (ISO-8601 with Z suffix).")`
5. เรียก `course.SetEnrollmentPolicy(command.EnrollmentDeadlineUtc, command.MaxSeats)` — ห่อ `try/catch (ArgumentOutOfRangeException)` คืน `DomainError.Validation(ex.Message)` (มิเรอร์สไตล์ error handling ของ handler อื่นในโมดูลนี้ที่จับ exception จาก domain แล้วแปลงเป็น `Result`)
6. `SaveChangesAsync`
7. **ไม่ evict output cache** — ฟิลด์เหล่านี้ไม่ปรากฏใน public read model เลย (ดู §1 นอกขอบเขต) จึงไม่มี cache ไหนต้อง invalidate ต่างจาก `SetCourseDeliveryFormatHandler` ที่ evict เพราะ `deliveryFormat` ปรากฏใน `GET /courses/{slug}` จริง (P11-07)
8. คืน `SetCourseEnrollmentPolicyResponse(course.Id, course.EnrollmentDeadlineUtc, course.MaxSeats, course.SeatsUsed)`

**Response** (`SetCourseEnrollmentPolicyResponse`):
```json
{ "id": "...", "enrollmentDeadlineUtc": "2026-12-01T00:00:00Z", "maxSeats": 50, "seatsUsed": 12 }
```

**Error cases**: 401 (ไม่มี token, มาตรฐาน group), 400 (validation — `maxSeats <= 0` หรือ `enrollmentDeadlineUtc` ไม่ใช่ UTC), 403 (ไม่ใช่เจ้าของคอร์ส), 404 (ไม่พบคอร์ส), 409 (คอร์ส `Archived`) — ทั้งหมดผ่าน `DomainError.*.ToProblemHttpResult(...)` มาตรฐานเดิม ไม่มี custom error shape ใหม่

---

## 4. Enforcement ที่ `OrderService`/`OrderExpiryJob` (หัวใจของงานนี้)

### 4.1 `OrderService.CreateAsync` — deadline check (ก่อน pricing)

แทรกหลัง duplicate-enrollment check (บรรทัด 44 เดิม) **ก่อน**เรียก `pricingEngine.CalculatePricingAsync` (บรรทัด 46 เดิม):

```csharp
var enrollmentPolicies = await catalogPriceContract.GetEnrollmentPoliciesAsync(command.CourseIds, cancellationToken).ConfigureAwait(false);
var now = clock.UtcNow;

foreach (var courseId in command.CourseIds.Distinct())
{
    if (enrollmentPolicies.TryGetValue(courseId, out var policy) &&
        policy.EnrollmentDeadlineUtc is { } deadline && now > deadline)
    {
        return Result.Failure<OrderResponse>(DomainError.Conflict("ปิดรับสมัครคอร์สนี้แล้ว"));
    }
}

// courses ที่ต้องจองที่นั่งแบบ atomic ตอนอยู่ในธุรกรรมสร้าง order (§4.2) — คำนวณตรงนี้เพื่อเลี่ยงเรียก
// TryReserveSeatAsync (ซึ่งคือ UPDATE จริงที่ล็อกแถวของ COURSES) กับคอร์สที่ไม่เคยตั้ง MaxSeats เลย — งาน
// UPDATE บน COURSES ทุก order ทุกคอร์สแม้ไม่มี cap จะเป็นต้นทุน lock contention ที่ไม่จำเป็นเลยในระยะยาว
// (ยอมรับ race window แคบมาก: ถ้าผู้สอนเพิ่งตั้ง MaxSeats ระหว่างสองบรรทัดนี้กับตอนเข้าธุรกรรมจริง อาจมี
// order เดียวที่หลุดผ่านโดยไม่เช็ค cap ใหม่ — ยอมรับได้ ไม่กระทบเคส "สองคนแย่งที่นั่งสุดท้ายของคอร์สที่ตั้ง
// cap ไว้อยู่ก่อนแล้ว" ที่ acceptance ต้องการพิสูจน์)
var seatCappedCourseIds = command.CourseIds.Distinct()
    .Where(id => enrollmentPolicies.TryGetValue(id, out var policy) && policy.MaxSeats.HasValue)
    .ToList();
```

(`pricingResult = await pricingEngine.CalculatePricingAsync(...)` และโค้ดถัดจากนั้นทั้งหมดคงเดิม จนถึง `return await orderRepository.ExecuteInTransactionAsync(...)`)

### 4.2 `OrderService.CreateAsync` — atomic seat reservation (ในธุรกรรมเดียวกับสร้าง order)

แก้ delegate ของ `ExecuteInTransactionAsync` (บรรทัด 67-112 เดิม) — เพิ่ม loop จองที่นั่งเป็นบรรทัดแรกสุดในนั้น **ก่อน** `orderRepository.AddAsync(order, ...)`:

```csharp
return await orderRepository.ExecuteInTransactionAsync(async () =>
{
    foreach (var courseId in seatCappedCourseIds)
    {
        var reserved = await catalogPriceContract.TryReserveSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
        if (!reserved)
        {
            return Result.Failure<OrderResponse>(DomainError.Conflict("คอร์สนี้ที่นั่งเต็มแล้ว"));
        }
    }

    await orderRepository.AddAsync(order, cancellationToken).ConfigureAwait(false);
    // ... โค้ดเดิมทั้งหมดที่เหลือไม่เปลี่ยน (promo redeem, zero-amount fast path, return Success) ...
}, cancellationToken).ConfigureAwait(false);
```

ถ้าคอร์สที่สองในตะกร้าจองที่นั่งไม่สำเร็จ (คอร์สแรกจองสำเร็จไปแล้ว) — คืน `Result.Failure` จาก delegate ทำให้ `ExecuteInTransactionAsync`'s `result is Result { IsSuccess: false }` sees `IsSuccess == false` → `RollbackAsync` อัตโนมัติ → การจองที่นั่งของคอร์สแรกที่สำเร็จไปแล้วในธุรกรรมเดียวกัน**ถูก rollback ไปด้วย** ไม่ต้องเขียน compensating-undo เอง (นี่คือเหตุผลที่ต้องจองที่นั่ง**ในธุรกรรม** ไม่ใช่ก่อนเข้าธุรกรรม)

### 4.3 `OrderService.CancelAsync` — ปล่อยที่นั่ง + ห่อ transaction ที่ขาดอยู่

โค้ดเดิม (บรรทัด 115-138) mutate + `SaveChangesAsync` ตรง ๆ ไม่มี transaction ห่อเลย (ต่างจาก `CreateAsync`/`OrderExpiryJob`) — ต้องห่อด้วย `ExecuteInTransactionAsync` เพื่อให้การปล่อยที่นั่งใหม่นี้ atomic กับ `MarkCancelled`/`RevertRedemptionAsync`/`SaveChangesAsync` ทั้งชุด (database.md: "การเปลี่ยนแปลงหลายตารางที่ต้อง atomic ... ต้องอยู่ใน transaction เดียว" — เข้าเงื่อนไขนี้ตรง ๆ พอดี เป็นช่องโหว่เดิมที่ยังไม่เคยมีใครแก้ ไม่ใช่ scope creep เพราะงานนี้ต้องแก้ไฟล์นี้อยู่แล้วเพื่อเพิ่ม seat release):

```csharp
public async Task<Result<OrderResponse>> CancelAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
{
    var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
    if (order is null || order.USER_ID != userId)
    {
        return Result.Failure<OrderResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อที่ระบุ"));
    }

    return await orderRepository.ExecuteInTransactionAsync(async () =>
    {
        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<OrderResponse>(DomainError.Conflict(ex.Message));
        }

        if (order.PROMO_CODE_ID.HasValue)
        {
            await promoCodeRepository.RevertRedemptionAsync(order.PROMO_CODE_ID.Value, order.ORDER_ID, cancellationToken).ConfigureAwait(false);
        }

        foreach (var courseId in order.ORDER_ITEMS.Select(i => i.COURSE_ID).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            await catalogPriceContract.ReleaseSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
        }

        await orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(ToResponse(order));
    }, cancellationToken).ConfigureAwait(false);
}
```

หมายเหตุ: `ReleaseSeatAsync` เรียกไม่มีเงื่อนไข (ไม่กรองด้วย `seatCappedCourseIds` แบบ `CreateAsync`) เพราะเป็นคนละบริบท — ไม่มีข้อมูล policy สดใหม่ให้กรองไว้ล่วงหน้าที่นี่ และ `ReleaseSeatAsync`'s `WHERE SeatsUsed > 0` ทำให้เรียกกับคอร์สที่ไม่เคยมี `MaxSeats` เป็น no-op ที่ปลอดภัยอยู่แล้ว (ต้นทุนคือ 1 `ExecuteUpdateAsync` no-op ต่อคอร์สต่อการยกเลิก 1 ครั้ง ไม่ใช่ทุก order อย่าง `TryReserveSeatAsync` — ยอมรับได้ ไม่คุ้มที่จะเก็บ "seat-capped ตอนสร้าง" ไว้ในตัว order เพื่อ optimize เคสที่เกิดไม่บ่อยเท่า checkout เอง)

### 4.4 `OrderExpiryJob.ExpireOrderAsync` — ปล่อยที่นั่ง

เพิ่ม dependency ใหม่ `ICatalogPriceContract catalogPriceContract` เข้า constructor ของ `OrderExpiryJob` (field `_catalogPriceContract`) — DI ไม่ต้องแก้อะไรเพิ่มที่ registration (`ICatalogPriceContract` ลงทะเบียน global อยู่แล้ว, container resolve constructor param ใหม่ให้เองอัตโนมัติ) **แต่ต้องแก้ 2 จุดที่ `new OrderExpiryJob(...)` ตรง ๆ**:
- `tests/Siri.UnitTests/Commerce/OrderExpiryJobTests.cs:29`
- `tests/Siri.IntegrationTests/OrderExpiryIntegrationTests.cs:155`

แก้ `ExpireOrderAsync` (ในธุรกรรมที่ห่ออยู่แล้ว) — เพิ่มก่อน `SaveChangesAsync` บรรทัดสุดท้าย:
```csharp
currentOrder.MarkCancelled();

if (currentOrder.PROMO_CODE_ID.HasValue)
{
    await _promoCodeRepository.RevertRedemptionAsync(currentOrder.PROMO_CODE_ID.Value, currentOrder.ORDER_ID, cancellationToken).ConfigureAwait(false);
}

foreach (var courseId in currentOrder.ORDER_ITEMS.Select(i => i.COURSE_ID).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
{
    await _catalogPriceContract.ReleaseSeatAsync(courseId, cancellationToken).ConfigureAwait(false);
}

await _orderRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

### 4.5 ทำไม refund (หลังจ่ายเงินแล้ว) ไม่เรียก `ReleaseSeatAsync`

`RefundService`/webhook flow ไม่อยู่ในสโคปของงานนี้เลย — ตัดสินใจโดยตั้งใจว่า **`SeatsUsed` ไม่ลดเมื่อ order ที่ `Paid` แล้วถูก refund ภายหลัง** (ต่างจาก cancel/expire ที่เกิด**ก่อน**จ่ายเงิน) เหตุผล: (1) ไม่มี acceptance ข้อไหนของ P11-11 พูดถึง refund เลย (2) การคืนที่นั่งหลังขายไปแล้วเป็นการตัดสินใจเชิงธุรกิจแยกต่างหาก (ที่นั่งที่เคย "เต็ม" แล้วเปิดใหม่เพราะมีคนขอเงินคืนอาจไม่ใช่พฤติกรรมที่ต้องการเสมอไป — ขึ้นกับว่าเป็น cohort ที่ตารางสอนคงที่อยู่แล้วหรือเปล่า) (3) ถ้าต้องการเปลี่ยนพฤติกรรมนี้ทีหลัง เป็นการแก้ `RefundService`'s success path เพิ่ม `ReleaseSeatAsync` เรียกเดียว ไม่กระทบ contract นี้เลย — ถ้าเจ้าของโปรเจ็คต้องการให้ refund คืนที่นั่งด้วย ต้องบอกก่อน เพราะเป็น business decision ใหม่ ไม่ใช่สิ่งที่ Q13.1/13.2 ระบุไว้

---

## 5. Integration checklist (สำหรับ integrator-qa)

**Unit test ใหม่ที่ต้องมี:**
- `COURSE.SetEnrollmentPolicy`: `maxSeats <= 0` → throw, non-UTC `enrollmentDeadlineUtc` → throw, `null`/`null` ผ่าน, ตั้ง `maxSeats` ต่ำกว่า `SeatsUsed` ปัจจุบันไม่ throw (มิเรอร์ `SetAccessDuration`'s test style ใน `CourseTests.cs`)
- `SetCourseEnrollmentPolicyCommandValidator`: `maxSeats = 0`/ติดลบ → fail, `null` ผ่าน
- `OrderServiceTests.cs` — ต้อง **extend `FakeCatalogPriceContract`** ให้ override `GetEnrollmentPoliciesAsync`/`TryReserveSeatAsync`/`ReleaseSeatAsync` (configurable ต่อเทสต์) แล้วเพิ่ม: (1) deadline ผ่านแล้ว → `Result.Failure` `Conflict`, ยืนยันว่า `FakeOrderRepository`'s order list ยังว่าง (ไม่ถึงขั้นสร้าง order) (2) `TryReserveSeatAsync` คืน false → `Result.Failure` `Conflict`, order ไม่ถูก persist (3) คอร์สที่ `MaxSeats`/`EnrollmentDeadlineUtc` เป็น `null` ทั้งคู่ (ค่า default) → พฤติกรรมเดิมไม่เปลี่ยนเลย (regression) — **อย่าแตะไฟล์ fake อีก 16 ไฟล์ที่ grep เจอใน §0** เพราะ default-interface-method ทำให้ compile ผ่านโดยไม่ต้องแก้
- `OrderExpiryJobTests.cs`: เพิ่ม fake/mock `ICatalogPriceContract` เข้า constructor call, พิสูจน์ `ReleaseSeatAsync` ถูกเรียกด้วย courseId ที่ถูกต้องตอน expire order ที่มี `ORDER_ITEMS`

**Integration test ใหม่ (Testcontainers PostgreSQL 17 จริง, ห้าม InMemory — ตาม backend.md):**
- ต่อยอด `tests/Siri.IntegrationTests/OrderEndpointsTests.cs` หรือสร้างไฟล์ใหม่คู่กัน (`SeatCapAndEnrollmentDeadlineTests.cs`): (1) **race สองคำขอสร้าง order พร้อมกัน** บนคอร์สที่ `MaxSeats = 1, SeatsUsed = 0` (`Task.WhenAll` ยิง `OrderService.CreateAsync` สองครั้งพร้อมกันจาก DbContext/scope คนละตัว) — ต้องมีคำขอเดียวสำเร็จ อีกคำขอได้ `Conflict`, และ `COURSES.SeatsUsed` จบที่ `1` เป๊ะ (ไม่ใช่ `2`) (2) `EnrollmentDeadlineUtc` เป็นอดีต → `CreateAsync` คืน `Conflict` ก่อนแม้แต่จะเรียก pricing (พิสูจน์ทางอ้อมว่าไม่มีการสร้าง `ORDER`/`ORDER_ITEM` แถวใหม่เลย) (3) cancel order ที่จองที่นั่งไปแล้วก่อนจ่ายเงิน → `COURSES.SeatsUsed` ลดกลับ (4) ต่อยอด `OrderExpiryIntegrationTests.cs` เพิ่มเคส: order หมดอายุอัตโนมัติ (`OrderExpiryJob`) บนคอร์สที่มี `MaxSeats` → `SeatsUsed` ลดกลับเช่นกัน (5) คอร์ส `MaxSeats`/`EnrollmentDeadlineUtc` เป็น `null` ทั้งคู่ (ค่า default ของทุกคอร์สเดิม) → ซื้อผ่านได้ปกติทุกจุดเหมือนก่อนงานนี้ (regression เต็ม flow ผ่าน HTTP จริงถ้าเป็นไปได้ ไม่ใช่แค่เรียก service ตรง)
- ยืนยัน migration `AddCourseEnrollmentDeadlineAndSeatCap` apply แล้ว schema มีคอลัมน์ครบ (ตรวจผ่าน `dotnet ef migrations list` + อ่านไฟล์ migration ที่ generate จริง ไม่ใช่แค่เชื่อว่าถูก)

**จุดเสี่ยงที่ต้อง field-by-field:**
- `PUT .../enrollment-policy`'s response field names ตรงกับ camelCase ที่กำหนดใน §3 เป๊ะ (`enrollmentDeadlineUtc`, `maxSeats`, `seatsUsed`)
- 403 vs 404 ของ ownership check ต้องแยกกันจริง (ไม่ collapse) — มิเรอร์ `SetCourseDeliveryFormatHandler` ที่มีอยู่แล้ว
- `ExecuteUpdateAsync` ทั้งสามจุดต้อง**ไม่**ผ่าน `AsNoTracking()`/tracked query ปนกัน (ต้องเป็น `dbContext.Courses().Where(...).ExecuteUpdateAsync(...)` ตรง ๆ ไม่มี `.Include`/`.AsNoTracking()` ปนซึ่งไม่มีผลอะไรกับ `ExecuteUpdateAsync` แต่ใส่ผิดจุดจะทำให้โค้ดอ่านสับสน)
- ยืนยันว่า 17 ไฟล์ fake ใน §0 **ไม่ถูกแก้เลย** (compile ผ่านด้วย default-interface-method) ยกเว้น `OrderServiceTests.cs`/`OrderExpiryJobTests.cs`/`OrderExpiryIntegrationTests.cs` ตามที่ระบุไว้ชัดเจนแล้ว
- Build 0 warning/0 error, unit+architecture test เขียวเต็ม, integration test ใหม่ต้องรันผ่านจริงด้วย Docker (ไม่ใช่แค่ compile แล้วข้ามด้วย `DockerUnavailableException` เหมือนเทสต์ที่เหลือ) — ถ้าเครื่อง QA ไม่มี Docker ให้รายงานตามจริงว่ายังไม่เคยรันผ่านจริง อย่าบอกว่า "ผ่านแล้ว"

## Changelog

(ยังไม่มี revision หลัง FROZEN)
