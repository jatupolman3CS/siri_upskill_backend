# Contract: X-31 ประกาศ (Announcement) ไม่เคยถูกส่งถึงใครเลย

Status: FROZEN · วันที่: 2026-09-16 · Module: **Notification** (vertical slice, PascalCase) เจ้าของ endpoint/entity เดิม + **Learning** (Repository+Service, UPPERCASE) เป็นผู้ implement contract ใหม่ที่ Notification ประกาศไว้

ผู้ลงมือที่แนะนำ: **Claude (backend-developer)** — งานนี้เพิ่ม/แก้ cross-module `Contracts/` interface 2 ตัว (`ILearningAccessContract.GetActiveEnrolledUserIdsAsync`, `Notification.Contracts.IAnnouncementRecipientResolver` ใหม่) ซึ่งตามกฎของทีมเป็นงานของ Claude เสมอไม่ว่าจะดูเล็กแค่ไหน ไม่ใช่ Antigravity

---

## 1. Scope & task IDs

แก้ `X-31` (docs/TASKS.md แถว X-31, แก้ไข 2026-09-16): ประกาศ (`ANNOUNCEMENT`) ที่ผู้สอนสร้างผ่าน `POST /api/notifications/announcements` ไม่เคยถูกส่งถึงผู้เรียนจริงเลย ไม่ว่าจะเป็นอีเมลหรือ in-app notification ทั้ง immediate และ scheduled

**อยู่ในขอบเขต:**
- เพิ่ม `ILearningAccessContract.GetActiveEnrolledUserIdsAsync` (default interface method ใหม่ + real override ใน `LearningAccessContract`)
- เพิ่ม cross-module contract ใหม่ `Siri.Modules.Notification.Contracts.IAnnouncementRecipientResolver` (ประกาศใน Notification, implement ใน Learning) แก้ปัญหา circular project reference (ดูเหตุผลเต็มใน §3.1)
- เพิ่ม `ANNOUNCEMENT.DispatchStatus` (enum ใหม่ `AnnouncementDispatchStatus { Pending, Sent }`) แทนการใช้ `SentAtUtc` เป็นตัวบอกสถานะ (ของเดิมตั้งค่าไว้ตอนสร้างทันทีโดยไม่มีอะไรถูกส่งจริง — นี่คือ root cause ของบั๊ก)
- เพิ่ม recurring job ใหม่ `AnnouncementDispatchJob` (ทุก 1 นาที, คู่กับ `EmailOutboxSenderJob`) ที่ resolve ผู้รับจริง + สร้าง `USER_NOTIFICATION` ทุกคนที่ active-enrolled + enqueue อีเมลผ่าน `IEmailOutbox` เมื่อ `SendEmail=true` + เรียก `ANNOUNCEMENT.MarkSent` ด้วยจำนวนผู้รับจริง
- แก้ `ANNOUNCEMENT.Create` ให้เริ่มที่ `DispatchStatus=Pending`, `SentAtUtc=null` เสมอ (ทั้ง immediate และ scheduled) — `SentAtUtc` มีความหมายเดียวคือ "เวลาที่ dispatch จริงเสร็จ" เท่านั้น
- Migration `AddAnnouncementDispatchStatus` (เพิ่ม 1 คอลัมน์ + index, ไม่มีตารางใหม่)
- Unit/integration test ครบตาม §5

**นอกขอบเขต (ตัดสินใจแล้ว พร้อมเหตุผล — อย่าทำเพิ่มเงียบ ๆ):**
- **Scheduled-dispatch เป็น job แยกจาก immediate ไหม?** ไม่แยก — ใช้ `AnnouncementDispatchJob` ตัวเดียวคุมทั้งสองเคส (query `DispatchStatus==Pending AND (ScheduledAtUtc==null OR ScheduledAtUtc<=now)`) เพราะ "immediate" กับ "scheduled ที่ถึงเวลาแล้ว" เป็นเงื่อนไขเดียวกันในทางปฏิบัติ ไม่มีเหตุผลต้องแยกกลไก — decision นี้ปิดคำถาม "ScheduledAtUtc support เป็น follow-up แยกไหม" ในคำสั่งงานแล้ว: **ไม่แยก ทำพร้อมกันในงานนี้**
- **ไม่แก้ FE** (`instructor-announcements-page.ts`) — response DTO เพิ่ม field `dispatchStatus` แบบ additive เท่านั้น ไม่กระทบ field เดิม ดู §4 สำหรับสิ่งที่ FE ควรทำต่อ (แนะนำ ไม่บังคับ ไม่ใช่ task ID ใน TASKS.md)
- **ไม่แก้ `GetCourseAnnouncementsAsync`'s authorization** (ตอนนี้ authenticated ทั่วไปเห็นประกาศของคอร์สไหนก็ได้ ไม่เช็ค enrollment) — เป็นช่องโหว่คนละประเภทกับ X-31 (ข้อมูลรั่วไม่ใช่ delivery ไม่ทำงาน), นอกขอบเขตงานนี้ รายงานแยกถ้าต้องการให้แก้
- **ไม่แตะ class naming ของ `ANNOUNCEMENT`/`CONTACT_MESSAGE`/`EMAIL_OUTBOX_MESSAGE`** — พบว่าทั้ง 3 entity ของ Notification module ใช้ UPPERCASE class name ทั้งที่ backend.md ระบุ Notification เป็น vertical-slice+PascalCase (ไม่ใช่ 1 ใน 7 โมดูล D-17) นี่คือ drift ที่เกิดมาก่อนงานนี้ (property ยังเป็น PascalCase ปกติ ไม่ใช่ `UPPER_SNAKE_CASE` เต็มรูปแบบ) — ตามด้วย convention เดิมของไฟล์ที่มีอยู่แล้วสำหรับ column ใหม่ ไม่ rename อะไรเพิ่ม เพราะเป็น refactor ข้ามงานที่ไม่ได้ถูกขอ
- **ไม่เพิ่ม rate limit ใหม่ให้ `POST /api/notifications/announcements`** — endpoint เดิมไม่มี `.RequireRateLimiting()` อยู่แล้ว การเพิ่ม/ไม่เพิ่มไม่ใช่สิ่งที่บั๊กนี้ทำให้แย่ลง (ปัญหาคือ "ส่งไม่ถึง" ไม่ใช่ "สร้างถี่เกินไป") นอกขอบเขต

---

## 2. Schema delta

**ตาราง `NOTIFY.ANNOUNCEMENTS`** (มีอยู่แล้ว จาก `ANNOUNCEMENT.cs`/`AnnouncementConfiguration.cs`) — เพิ่ม 1 คอลัมน์ใหม่:

| Property (C#, PascalCase — ตาม convention เดิมของไฟล์นี้) | Column (DB, auto-uppercased โดย `ApplyUppercaseNamingConventions`) | Type | Nullable | หมายเหตุ |
|---|---|---|---|---|
| `DispatchStatus` | `DISPATCH_STATUS` | `character varying(32)` (`HasConversion<string>()`, ตาม pattern `EmailOutboxStatus`) | NOT NULL | enum `AnnouncementDispatchStatus { Pending, Sent }` — ดู §3.3 |

Index ใหม่: `HasIndex(a => new { a.DispatchStatus, a.ScheduledAtUtc }).HasDatabaseName("IX_ANNOUNCEMENTS_DISPATCH_STATUS_SCHEDULED_AT_UTC")` (49 ไบต์ ผ่านกฎ 63-byte identifier limit) — รองรับ `AnnouncementDispatchJob`'s due-row query (`DispatchStatus lead, ScheduledAtUtc` เป็น second key column สำหรับ range scan ของ `<= now` — เหตุผลเดียวกับ `IX_EMAIL_OUTBOX_STATUS_NEXT_RETRY_AT_UTC`)

**Migration ชื่อ `AddAnnouncementDispatchStatus`**

⚠️ **จุดที่พลาดไม่ได้ — ต้องแก้ migration ที่ generate ออกมาด้วยมือก่อนใช้ (database.md บังคับให้อ่าน migration ก่อนใช้ทุกครั้งอยู่แล้ว):**

`dotnet ef migrations add` จะ scaffold คอลัมน์ `NOT NULL` ใหม่บนตารางที่มีอยู่แล้วโดยไม่มี default ที่ใช้งานได้ (ค่า default ของ `string` คือ `""` ซึ่งไม่ match กับ enum member ไหนเลยใน `AnnouncementDispatchStatus` — จะ deserialize ไม่ได้ทันทีที่มีแถวเก่าอยู่จริง) **ต้องแก้ `AddColumn<string>(...)` ใน `Up()` ให้ใส่ `defaultValue: "Sent"` ตรง ๆ ด้วยมือ**

เหตุผลที่ต้องเป็น `"Sent"` ไม่ใช่ `"Pending"`: แถวเก่าทุกแถวที่มีอยู่ก่อน migration นี้ (ทั้งที่ `SentAtUtc` ถูกตั้งผิดตอนสร้างเพราะบั๊ก X-31 เอง และแถวที่ตั้งเวลาไว้แต่ `ScheduledAtUtc` ยังไม่เคยถูกอ่านโดย job ไหนเลย) **ต้อง backfill เป็น `Sent` เท่านั้น ห้ามเป็น `Pending`** — ถ้า backfill เป็น `Pending` แล้ว `AnnouncementDispatchJob` (ตัวใหม่จากงานนี้) จะไปดึงประกาศเก่าที่อาจมีอายุเป็นเดือนขึ้นมา "ส่งจริง" ทันทีที่ deploy เสร็จ ทำให้ผู้เรียนได้รับอีเมล/แจ้งเตือนเกี่ยวกับประกาศเก่าที่ไม่มีใครตั้งใจจะส่งซ้ำ — เป็นความเสี่ยง production ที่ต้องกันไว้ล่วงหน้า ไม่ใช่เดาเอาทีหลัง

**อย่าใส่ `.HasDefaultValue()` ใน `AnnouncementConfiguration` fluent config** — ปล่อยให้ default อยู่แค่ใน migration statement (การ backfill ครั้งเดียว) เท่านั้น โค้ด `ANNOUNCEMENT.Create()` เป็นทางเดียวที่สร้าง entity นี้และตั้งค่า `DispatchStatus` ชัดเจนเองเสมอ (`Pending` ทุกครั้ง) จึงไม่ต้องมี default ที่ระดับ model — ตรงกับที่ `EmailOutboxStatus` ก็ไม่มี `.HasDefaultValue()` เช่นกัน

**ก่อน deploy จริง:** แจ้งเจ้าของโปรเจ็คตรง ๆ ว่า migration นี้ backfill ประกาศเก่าทั้งหมดเป็น `Sent` (= "ถือว่าจบแล้ว ไม่ส่งซ้ำ") ถ้ามีประกาศที่ตั้งเวลาไว้ล่วงหน้าจริงและยังต้องการให้ส่งจริงตอนนี้ ต้องแก้ด้วยมือเป็นกรณีไป (เช่น สร้างประกาศใหม่) ไม่ใช่ผลข้างเคียงอัตโนมัติของ deploy — **นี่ไม่ใช่ DECISIONS.md Q ที่ค้างอยู่ แต่เป็นความเสี่ยง operational ที่ต้องแจ้งก่อนใช้จริง**

---

## 3. API contract

### 3.1 เหตุผลที่ต้องมี `IAnnouncementRecipientResolver` (จุดออกแบบที่สำคัญที่สุดของ contract นี้)

Notification module **อ้างอิง (`ProjectReference`) module อื่นไม่ได้เลยสักตัว** โดยไม่ทำให้เกิด circular project reference — ตรวจสอบ dependency graph จริงแล้ว:

- `Siri.Modules.Identity.csproj` → `Siri.Modules.Notification.csproj` (มีอยู่แล้ว, สำหรับ `IEmailOutbox` ใน Register/ForgotPassword)
- `Siri.Modules.Catalog.csproj` → `Siri.Modules.Identity.csproj` + `Siri.Modules.Notification.csproj` (มีอยู่แล้ว)
- `Siri.Modules.Learning.csproj` → `Siri.Modules.Catalog.csproj` + `Siri.Modules.Identity.csproj` (มีอยู่แล้ว)
- `Siri.Modules.Notification.csproj` → **ไม่มีการอ้างอิง module อื่นเลยสักตัว** (มีแค่ SharedKernel/Persistence/Integrations.Email)

ถ้า Notification → Identity โดยตรง: ปิดวง `Identity → Notification → Identity` ทันที
ถ้า Notification → Learning โดยตรง: ปิดวง `Learning → Identity → Notification → Learning` (ผ่าน Learning→Identity ที่มีอยู่แล้ว) เช่นกัน

**Notification module จึงเป็น "sink" โดยตั้งใจ (หรืออย่างน้อยก็เป็นสภาพจริงของ graph ตอนนี้)** — module อื่นอ้างอิงเข้ามาเพื่อใช้ `IEmailOutbox`/`ICourseOwnershipVerifier` ได้ แต่ Notification เองอ้างอิงออกไปไม่ได้ วิธีแก้ที่มีอยู่แล้วในโค้ดเบส (ใช้ซ้ำ ไม่ใช่คิดใหม่): **`ICourseOwnershipVerifier`** (`Siri.Modules.Notification/Contracts/ICourseOwnershipVerifier.cs`) — Notification เป็นคนประกาศ interface เอง แล้ว Catalog (module ที่มีข้อมูลจริง และ**สามารถ**อ้างอิง Notification.csproj ได้อยู่แล้วโดยไม่ปิดวง) เป็นคน implement ผ่าน `CatalogPriceContract`, register จาก `CatalogModule.AddCatalogModule` — `CreateAnnouncementHandler` inject `ICourseOwnershipVerifier` ตรง ๆ โดยไม่รู้เลยว่า implementation จริงอยู่ที่ module ไหน

**Contract นี้ใช้ pattern เดียวกันเป๊ะ เป็น instance ที่สองของ pattern นี้ในโค้ดเบส:**

- ประกาศ `IAnnouncementRecipientResolver` ใน `Siri.Modules.Notification.Contracts`
- Implement ใน `Siri.Modules.Learning.Infrastructure.Contracts.AnnouncementRecipientResolver` (Learning มีทั้งข้อมูล enrollment ของตัวเอง **และ**อ้างอิง `Identity.Contracts.IUserContactReader` อยู่แล้ว — ดูหลักฐานที่ `CertificateService.cs` ใช้ `IUserContactReader` จริงอยู่แล้วสำหรับใบประกาศนียบัตร)
- Register จาก `LearningModule.AddLearningModule` (Learning module composition root — เหมือนที่ Catalog register `ICourseOwnershipVerifier` จาก `CatalogModule` เอง)
- ต้องเพิ่ม `<ProjectReference Include="..\Siri.Modules.Notification\Siri.Modules.Notification.csproj" />` ใน `Siri.Modules.Learning.csproj` — **ปลอดภัย ไม่ปิดวง** เพราะ Notification ไม่อ้างอิงกลับไปที่ใครเลย

### 3.2 `Siri.Modules.Learning.Contracts.ILearningAccessContract` — สมาชิกใหม่ (default interface method)

ไฟล์: `src/Siri.Modules.Learning/Contracts/ILearningAccessContract.cs`

```csharp
/// <summary>
/// User ids ที่มี enrollment active/ไม่หมดอายุใน <paramref name="courseId"/> ตอนนี้ — กติกา "active"
/// เดียวกับที่ <see cref="HasActiveEnrollmentAsync"/> ใช้ (Status == Active AND (ExpiresAtUtc is null
/// OR ExpiresAtUtc > now)) แค่คืนทั้งเซ็ตของคอร์สเดียว แทนที่จะเช็คทีละคน
/// เพิ่มเข้ามาเพื่อ X-31 (ส่งประกาศคอร์สถึงผู้เรียนจริง — ดู
/// Siri.Modules.Notification.Contracts.IAnnouncementRecipientResolver ซึ่งเรียกผ่าน
/// Siri.Modules.Learning.Infrastructure.Contracts.AnnouncementRecipientResolver) และออกแบบ signature
/// ให้ reuse ได้กับ P11-04 (live-session invite job, docs/HYBRID_LIVE.md, ยังติด Q10/Q11) ในอนาคต —
/// ตัว method นี้เองไม่มีอะไรเกี่ยวกับ Live เลย และ ship ได้ตอนนี้โดยไม่ต้องรอ P11
/// Default implementation คืนเซ็ตว่างเปล่า เพื่อให้ implementer อื่นของ interface นี้ (เช่น test
/// double ในโมดูลอื่น) คอมไพล์ผ่านได้โดยไม่ต้อง opt-in — มีแค่
/// <c>Infrastructure.Contracts.LearningAccessContract</c> เท่านั้นที่ override ด้วย query จริง
/// </summary>
Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(
    Guid courseId,
    CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
```

**Real override** ใน `src/Siri.Modules.Learning/Infrastructure/Contracts/LearningAccessContract.cs` (ต้องเพิ่ม `using Microsoft.EntityFrameworkCore;` สำหรับ `ToListAsync`):

```csharp
public async Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(Guid courseId, CancellationToken cancellationToken)
{
    if (courseId == Guid.Empty)
    {
        return new HashSet<Guid>();
    }

    var now = clock.UtcNow;
    var userIds = await enrollmentRepository.Query()
        .Where(e => e.COURSE_ID == courseId
            && e.STATUS == EnrollmentStatus.Active
            && (!e.EXPIRES_AT_UTC.HasValue || e.EXPIRES_AT_UTC.Value > now))
        .Select(e => e.USER_ID)
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

    return userIds.ToHashSet();
}
```

`enrollmentRepository.Query()` มีอยู่แล้ว (`IEnrollmentRepository.Query()`, `AsNoTracking` ตาม doc comment ของมันเอง) — ไม่ต้องเพิ่ม repository method ใหม่

### 3.3 `Siri.Modules.Notification.Contracts.IAnnouncementRecipientResolver` — ไฟล์ใหม่

ไฟล์ใหม่: `src/Siri.Modules.Notification/Contracts/IAnnouncementRecipientResolver.cs`

```csharp
namespace Siri.Modules.Notification.Contracts;

/// <summary>
/// ผู้รับประกาศ 1 คน — user ที่มี enrollment active ในคอร์สของประกาศนั้น พร้อมข้อมูลติดต่อที่ใช้ส่งจริงได้
/// </summary>
public sealed record AnnouncementRecipient(Guid UserId, string Email, string DisplayName);

/// <summary>
/// รวม "ใครมีสิทธิ์ได้รับประกาศคอร์สนี้" (Learning.Contracts.ILearningAccessContract
/// .GetActiveEnrolledUserIdsAsync) กับ "ติดต่อคนนั้นยังไงจริง" (Identity.Contracts.IUserContactReader
/// .GetUsersContactInfoAsync) ไว้หลังอินเทอร์เฟซเดียว ประกาศไว้ที่นี่ (Notification.Contracts) แทนที่
/// Notification จะเรียก Learning/Identity ตรง ๆ เพราะ Notification อ้างอิง project ทั้งสองไม่ได้โดยไม่
/// ปิดวง (ดูเหตุผลเต็มใน docs/contracts/X-31-announcement-delivery.md §3.1) — pattern เดียวกับ
/// <see cref="ICourseOwnershipVerifier"/> (ประกาศที่นี่ implement โดย Catalog) นี่คือ instance ที่สอง
/// implement โดย <c>Siri.Modules.Learning.Infrastructure.Contracts.AnnouncementRecipientResolver</c>
/// registered จาก <c>LearningModule.AddLearningModule</c>
/// </summary>
public interface IAnnouncementRecipientResolver
{
    /// <summary>
    /// ผู้รับทุกคนที่มี enrollment active ใน <paramref name="courseId"/> ตอนนี้ พร้อม contact info —
    /// user ที่ active enrolled แต่ resolve contact info ไม่ได้ (ไม่ควรเกิดจริงเพราะ
    /// identity.Users.Email เป็น NOT NULL แต่ป้องกันไว้) จะถูกข้ามเงียบ ๆ ไม่นับ ไม่ส่ง
    /// </summary>
    Task<IReadOnlyList<AnnouncementRecipient>> GetRecipientsAsync(
        Guid courseId,
        CancellationToken cancellationToken);
}
```

**Implementation** ไฟล์ใหม่: `src/Siri.Modules.Learning/Infrastructure/Contracts/AnnouncementRecipientResolver.cs`

```csharp
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

public sealed class AnnouncementRecipientResolver(
    ILearningAccessContract learningAccessContract,
    IUserContactReader userContactReader) : IAnnouncementRecipientResolver
{
    public async Task<IReadOnlyList<AnnouncementRecipient>> GetRecipientsAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var userIds = await learningAccessContract
            .GetActiveEnrolledUserIdsAsync(courseId, cancellationToken)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
        {
            return Array.Empty<AnnouncementRecipient>();
        }

        var contactInfo = await userContactReader
            .GetUsersContactInfoAsync(userIds, cancellationToken)
            .ConfigureAwait(false);

        var recipients = new List<AnnouncementRecipient>(userIds.Count);
        foreach (var userId in userIds)
        {
            if (contactInfo.TryGetValue(userId, out var contact) && !string.IsNullOrWhiteSpace(contact.Email))
            {
                recipients.Add(new AnnouncementRecipient(userId, contact.Email, contact.DisplayName));
            }
        }

        return recipients;
    }
}
```

**DI/project wiring:**

`src/Siri.Modules.Learning/Siri.Modules.Learning.csproj` — เพิ่ม (พร้อม comment อธิบายเหตุผลไม่ปิดวง เหมือน pattern ที่ `Siri.Modules.Catalog.csproj`/`Siri.Modules.Commerce.csproj` ใช้อยู่แล้ว):
```xml
<ProjectReference Include="..\Siri.Modules.Notification\Siri.Modules.Notification.csproj" />
```

`src/Siri.Modules.Learning/LearningModule.cs` — ในบล็อก `// --- Cross-module contracts ---` เพิ่ม:
```csharp
services.AddScoped<Notification.Contracts.IAnnouncementRecipientResolver, Infrastructure.Contracts.AnnouncementRecipientResolver>();
```

### 3.4 `ANNOUNCEMENT` domain — แก้ 2 จุด

ไฟล์: `src/Siri.Modules.Notification/Domain/ANNOUNCEMENT.cs`

เพิ่ม property `public AnnouncementDispatchStatus DispatchStatus { get; private set; }`

`Create` — **ลบ** `SentAtUtc = scheduledAtUtc is null ? clock.UtcNow : null` (นี่คือบั๊ก) **เปลี่ยนเป็น**:
```csharp
ScheduledAtUtc = scheduledAtUtc,
SentAtUtc = null,
DispatchStatus = AnnouncementDispatchStatus.Pending,
RecipientCount = 0,
```

`MarkSent` (มีอยู่แล้ว มีเทสต์คลุมแล้วด้วย — แค่เพิ่ม 1 บรรทัด) เพิ่ม `DispatchStatus = AnnouncementDispatchStatus.Sent;` เข้าไปในเมธอดเดิม (ท้าย `SentAtUtc`/`RecipientCount` assignment)

ไฟล์ใหม่: `src/Siri.Modules.Notification/Domain/AnnouncementDispatchStatus.cs`

```csharp
namespace Siri.Modules.Notification.Domain;

/// <summary>
/// สถานะการส่งจริงของ <see cref="ANNOUNCEMENT"/> — คนละมิติกับ <see cref="ANNOUNCEMENT.SendEmail"/>
/// (fan-out ต้อง enqueue อีเมลด้วยไหม) และ <see cref="ANNOUNCEMENT.ScheduledAtUtc"/> (fan-out
/// ทำได้เมื่อไหร่) X-31 เพิ่มเข้ามาแทนที่การพึ่ง <see cref="ANNOUNCEMENT.SentAtUtc"/> เพียงอย่างเดียว
/// เป็นตัวบอก "ส่งแล้วหรือยัง" (ของเดิมตั้งค่าตอนสร้างทันทีโดยไม่มีอะไรถูกส่งจริงเลย) — ตอนนี้
/// "ส่งแล้ว" เป็น fact ที่ถูกบันทึกเฉพาะตอน <see cref="ANNOUNCEMENT.MarkSent"/> ถูกเรียกจริงเท่านั้น
/// (จาก <c>Infrastructure/AnnouncementDispatchJob.cs</c> หลัง resolve ผู้รับและ stage
/// อีเมล/in-app notification เสร็จ) มีแค่ 2 ค่า (ไม่มี "Failed") เพราะ
/// <c>AnnouncementDispatchJob</c> ทำ fan-out ของประกาศ 1 ฉบับใน <c>SaveChangesAsync</c> เดียว — รัน
/// พังแล้วไม่มีอะไรถูกบันทึกเลย แถวจะถูก retry ทั้งก้อนในรอบถัดไปโดยอัตโนมัติ (เหตุผลเดียวกับที่
/// <c>EMAIL_OUTBOX_MESSAGE</c> ใช้ nullable <c>NextRetryAtUtc</c> แทนการเพิ่มสถานะที่ 3 — ดู
/// <see cref="EmailOutboxStatus"/>'s doc comment)
/// </summary>
public enum AnnouncementDispatchStatus
{
    /// <summary>ยังไม่ส่ง — เพิ่งสร้าง (immediate), หรือรอ ScheduledAtUtc ถึงเวลา, หรือถึงเวลาแล้ว
    /// รอ AnnouncementDispatchJob รอบถัดไป</summary>
    Pending,

    /// <summary>Fan-out เสร็จแล้ว — ผู้เรียนที่ active-enrolled ตอนนั้นทุกคนมี in-app notification
    /// แล้ว และ (ถ้า SendEmail) มี outbox row แล้ว Terminal</summary>
    Sent,
}
```

`AnnouncementConfiguration.cs` เพิ่ม:
```csharp
builder.Property(a => a.DispatchStatus)
    .HasConversion<string>()
    .HasMaxLength(32)
    .IsRequired();

builder.HasIndex(a => new { a.DispatchStatus, a.ScheduledAtUtc })
    .HasDatabaseName("IX_ANNOUNCEMENTS_DISPATCH_STATUS_SCHEDULED_AT_UTC");
```

### 3.5 `AnnouncementDispatchJob` — ไฟล์ใหม่

ไฟล์ใหม่: `src/Siri.Modules.Notification/Infrastructure/AnnouncementDispatchJob.cs`

```csharp
using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>
/// Recurring job (ลงทะเบียนทุก 1 นาทีจาก Siri.Workers/RecurringJobsRegistration.cs คู่กับ
/// EmailOutboxSenderJob) ที่ทำให้ ANNOUNCEMENT ถูกส่งจริง (X-31) — หา row ที่ถึงกำหนด, resolve ผู้รับ
/// จริงผ่าน IAnnouncementRecipientResolver, stage USER_NOTIFICATION ให้ทุกคนที่ active-enrolled
/// ตอนนั้น + (ถ้า SendEmail) EMAIL_OUTBOX_MESSAGE ต่อคน แล้วปิดด้วย ANNOUNCEMENT.MarkSent(จำนวนจริง)
/// — ทุกอย่างของประกาศ 1 ฉบับอยู่ใน SaveChangesAsync เดียว (all-or-nothing ต่อฉบับ ต่อรัน)
/// </summary>
public sealed class AnnouncementDispatchJob(
    AppDbContext dbContext,
    IAnnouncementRecipientResolver recipientResolver,
    IEmailOutbox emailOutbox,
    IClock clock,
    ILogger<AnnouncementDispatchJob> logger)
{
    /// <summary>จำนวน ANNOUNCEMENT สูงสุดต่อรัน (ไม่ใช่จำนวนผู้รับ) — คุมงานสูงสุดต่อนาทีถ้าหลายคอร์ส
    /// ประกาศพร้อมกัน การ stage แถว outbox/notification เป็นแค่ insert ไม่ใช่การส่งจริง (การส่งจริงถูก
    /// throttle แยกอยู่แล้วที่ EmailOutboxSenderJob's BatchSize=100/นาที) จึงไม่ต้อง cap จำนวนผู้รับ
    /// ต่อฉบับ — ให้ RecipientCount เป็นตัวเลขจริงตัวเดียวจบในรันเดียว ไม่ต้องแบ่งหลายรอบ</summary>
    private const int MaxAnnouncementsPerRun = 10;

    private const string AnnouncementNotificationType = "course.announcement";

    // ยังไม่มี slug ให้ deep-link หน้าคอร์สจาก Notification/Learning โดยไม่เพิ่ม cross-module call
    // อีกเส้นแค่เพื่อ cosmetic link — ชี้ไปหน้ารายการคอร์สของฉันแทน ปรับได้ทีหลังถ้ามีหน้า
    // course-announcements ของ FE จริง
    private const string AnnouncementLinkUrl = "/my-courses";

    [DisableConcurrentExecution(timeoutInSeconds: 110)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var due = await dbContext.Announcements()
            .Where(a => a.DispatchStatus == AnnouncementDispatchStatus.Pending
                && (a.ScheduledAtUtc == null || a.ScheduledAtUtc <= now))
            .OrderBy(a => a.Id) // UUIDv7 PK -> เก่าสุดก่อน เหมือน EmailOutboxSenderJob
            .Take(MaxAnnouncementsPerRun)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var announcement in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var recipients = await recipientResolver
                    .GetRecipientsAsync(announcement.CourseId, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var recipient in recipients)
                {
                    dbContext.UserNotifications().Add(USER_NOTIFICATION.Create(
                        recipient.UserId,
                        AnnouncementNotificationType,
                        announcement.Title,
                        announcement.Body,
                        AnnouncementLinkUrl,
                        clock));

                    if (announcement.SendEmail)
                    {
                        emailOutbox.Enqueue(
                            toEmail: recipient.Email,
                            subject: announcement.Title,
                            bodyHtml: BuildEmailBodyHtml(announcement),
                            templateKey: "course-announcement");
                    }
                }

                announcement.MarkSent(recipients.Count, clock);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ยังไม่มีอะไรของ announcement นี้ถูก save (SaveChangesAsync อยู่นอก loop รันครั้งเดียว
                // ต่อรอบ) — log แล้วข้ามไปฉบับถัดไป แถวนี้ค้าง Pending ต่อ รอบหน้า retry ทั้งก้อนเอง
                // ฉบับอื่นในแบตช์เดียวกันไม่ได้รับผลกระทบ
                logger.LogError(
                    ex,
                    "Failed to dispatch announcement {AnnouncementId} for course {CourseId}",
                    announcement.Id,
                    announcement.CourseId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Title/Body เป็น free text จากผู้สอน ไม่ผ่าน HTML sanitizer แบบ CMS content (security.md ครอบคลุม
    // เฉพาะ "HTML จาก CMS") — encode ก่อนฝังใน HTML อีเมลกันเนื้อหาที่มี tag/script หลุดออกไปเป็น HTML
    // จริงในอีเมลที่ส่งออก
    private static string BuildEmailBodyHtml(ANNOUNCEMENT announcement)
    {
        var encodedTitle = WebUtility.HtmlEncode(announcement.Title);
        var encodedBody = WebUtility.HtmlEncode(announcement.Body).Replace("\n", "<br/>", StringComparison.Ordinal);
        return $"<h2>{encodedTitle}</h2><p>{encodedBody}</p>";
    }
}
```

**DI/registration:**

`src/Siri.Modules.Notification/NotificationModule.cs` — ข้าง `services.AddScoped<EmailOutboxSenderJob>();` เพิ่ม:
```csharp
services.AddScoped<AnnouncementDispatchJob>();
```

`src/Siri.Workers/RecurringJobsRegistration.cs` — เพิ่มรายการที่ 8 (namespace `Siri.Modules.Notification.Infrastructure` import อยู่แล้ว):
```csharp
// 8. Drains due course announcements every minute (X-31: nothing ever dispatched these before)
recurringJobManager.AddOrUpdate<AnnouncementDispatchJob>(
    "announcement-dispatch",
    job => job.RunAsync(CancellationToken.None),
    Cron.Minutely());
```

### 3.6 `AnnouncementResponse` DTO — เพิ่ม field (additive, ไม่ breaking)

ไฟล์: `src/Siri.Modules.Notification/Features/CreateAnnouncement/Command.cs`

```csharp
public sealed record AnnouncementResponse(
    Guid Id,
    Guid CourseId,
    Guid InstructorId,
    string Title,
    string Body,
    bool SendEmail,
    DateTime? ScheduledAtUtc,
    DateTime? SentAtUtc,
    int RecipientCount,
    AnnouncementDispatchStatus DispatchStatus,
    DateTime CreatedAtUtc);
```

Serialize เป็น string ("Pending"/"Sent") ผ่าน global `JsonStringEnumConverter` ที่ตั้งไว้ที่ `Program.cs` แล้ว (ตั้งแต่ P1-03) ไม่ต้องทำอะไรเพิ่ม

**3 call site ที่ต้องเพิ่ม `announcement.DispatchStatus`/`a.DispatchStatus` เข้า constructor:**
- `src/Siri.Modules.Notification/Features/CreateAnnouncement/Handler.cs` (บรรทัดสร้าง `AnnouncementResponse` ท้าย `HandleAsync`)
- `src/Siri.Modules.Notification/Features/GetCourseAnnouncements/Handler.cs` (ใน `.Select(a => new AnnouncementResponse(...))`)
- `src/Siri.Modules.Notification/Features/GetInstructorAnnouncements/Handler.cs` (เช่นเดียวกัน)

**Endpoint เดิมไม่เปลี่ยน** (`POST /api/notifications/announcements`, `GET /api/notifications/announcements/course/{courseId}`, `GET /api/notifications/instructor/announcements`) — path, auth policy (`InstructorOnly` สำหรับ create/list-mine, authenticated ทั่วไปสำหรับ get-by-course), ownership check ใน `CreateAnnouncementHandler` (`ICourseOwnershipVerifier`) **ไม่แตะ**

---

## 4. Frontend notes

Route ที่บริโภค API นี้: `frontend/src/app/features/instructor/announcements/instructor-announcements-page.ts` (**มีอยู่แล้ว ต่อ API จริงแล้ว ไม่ใช่ mock**) + `frontend/src/app/features/notification/data/notification-api.models.ts`'s `AnnouncementResponse` interface

**สิ่งที่ต้อง reconcile (ไม่บังคับสำหรับ X-31 นี้ — งานนี้เป็น backend-only ตาม task ID แต่บันทึกไว้ให้ session ที่แก้ FE ครั้งถัดไปเห็น):**
- เพิ่ม `readonly dispatchStatus: 'Pending' | 'Sent';` เข้า `AnnouncementResponse` (TS) — additive, ไม่กระทบ field เดิม
- `instructor-announcements-page.ts`'s บรรทัด 98/133 คำนวณ `sentAtUtc: item.sentAtUtc ?? item.createdAtUtc` แล้วแสดงเป็น "ส่งแล้ว" ทันที — หลัง fix นี้ ประกาศที่เพิ่งสร้างจะมี `sentAtUtc: null` จริง ๆ จนกว่า `AnnouncementDispatchJob` จะรันรอบถัดไป (ไม่เกิน ~1 นาที) ตัว fallback `?? createdAtUtc` เดิมทำให้ UI **ไม่พัง** (ยังโชว์เวลาได้) แต่ `recipientsCount`/`isEmailSent` ที่โชว์ทันทีหลังสร้างจะเป็น 0/optimistic ไปก่อนจนกว่าจะ refresh list ใหม่ — แนะนำ (ไม่บังคับ) เพิ่ม badge "กำลังส่ง" เมื่อ `dispatchStatus === 'Pending'` แทนการโชว์ recipientsCount=0 เป็นตัวเลขสุดท้าย
- SSR/CSR: หน้านี้อยู่ใต้ `/instructor` (CSR + `noindex` ตาม frontend.md อยู่แล้ว) ไม่ต้องเปลี่ยน
- ไม่มี i18n key ใหม่ที่บังคับ (ถ้าเพิ่ม badge สถานะข้างต้นถึงจะต้องเพิ่ม key `instructor.announcements.pending`/`sent` ทั้ง th.json/en.json)

---

## 5. Integration checklist (สำหรับ integrator-qa)

**Unit test:**
- [ ] แก้ `tests/Siri.UnitTests/Notification/AnnouncementTests.cs`'s `Create_WhenValid_SetsPropertiesCorrectly` — **เทสต์นี้ encode บั๊กเดิมไว้เป็นพฤติกรรมที่ถูกต้อง** (`Assert.Equal(now, announcement.SentAtUtc)`) ต้องเปลี่ยนเป็น `Assert.Null(announcement.SentAtUtc)` + `Assert.Equal(AnnouncementDispatchStatus.Pending, announcement.DispatchStatus)` — ถ้า agent เห็นเทสต์นี้แดงหลังแก้โค้ด **ให้แก้เทสต์ ไม่ใช่ revert โค้ด** (นี่คือ regression ที่ตั้งใจ)
- [ ] `MarkSent_UpdatesSentAtUtcAndRecipientCount` — เพิ่ม assert `Assert.Equal(AnnouncementDispatchStatus.Sent, announcement.DispatchStatus)`
- [ ] `Create_WhenScheduled_SentAtUtcIsNull` — ยังผ่านเหมือนเดิม (พฤติกรรมไม่เปลี่ยนสำหรับเคส scheduled) เพิ่ม assert `DispatchStatus == Pending`
- [ ] `LearningAccessContract.GetActiveEnrolledUserIdsAsync` — unit/integration test (ต้องพึ่ง DB เหมือน method อื่นในคลาสนี้): active+ไม่หมดอายุ นับ, `Expired`/`Revoked` ไม่นับ, active แต่ `ExpiresAtUtc <= now` ไม่นับ, คอร์สอื่นไม่ปน
- [ ] `AnnouncementRecipientResolver.GetRecipientsAsync` — unit test ด้วย fake `ILearningAccessContract`+`IUserContactReader`: bundle ถูกต้อง, user ที่ resolve contact ไม่ได้ถูกข้าม ไม่ throw

**Integration test (`AnnouncementDispatchJob`, ตาม pattern integration test อื่นของโมดูลนี้ — Testcontainers PostgreSQL+Redis):**
- [ ] `SendEmail=false` → ไม่มี `EMAIL_OUTBOX_MESSAGE` แถวใหม่เลย แต่ `USER_NOTIFICATION` ถูกสร้างครบทุกคนที่ active-enrolled และ `DispatchStatus` จบที่ `Sent`
- [ ] `SendEmail=true` → `EMAIL_OUTBOX_MESSAGE` และ `USER_NOTIFICATION` จำนวนเท่ากับผู้ enrolled active จริง
- [ ] `RecipientCount` ตรงกับจำนวนที่ active-enrolled-และ-ได้รับแจ้งจริง (ทดสอบด้วย mix: active 3, expired 1, revoked 1 → `RecipientCount == 3`)
- [ ] คอร์สไม่มีผู้เรียนเลย → ไม่ error, `RecipientCount == 0`, `DispatchStatus` จบที่ `Sent` (ไม่ค้าง `Pending` ตลอดไป)
- [ ] ประกาศที่ `ScheduledAtUtc` เป็นอนาคต → ไม่ถูก dispatch (ยังคง `Pending`) จนกว่าเวลาจะถึง
- [ ] ประกาศที่ `ScheduledAtUtc` ถึงกำหนดแล้ว (หรือ null) → ถูก dispatch รอบถัดไปของ job
- [ ] ผู้สอนที่ไม่ใช่เจ้าของคอร์สยังโดน 403 ที่ `POST /api/notifications/announcements` เหมือนเดิม (regression check — งานนี้ไม่แตะ ownership check เดิม)
- [ ] ประกาศ 1 ฉบับใน batch เดียวกัน dispatch fail (จำลองด้วย fake `IAnnouncementRecipientResolver` ที่ throw) → ฉบับอื่นในรันเดียวกันยัง dispatch สำเร็จ (ไม่ใช่ all-or-nothing ข้ามทั้งรัน), ฉบับที่ fail ยังคง `Pending` และถูก retry รอบถัดไป
- [ ] Migration backfill: จำลองแถว `ANNOUNCEMENT` เก่าก่อน apply migration (หรือ verify ผ่าน migration script โดยตรง) → หลัง apply ได้ `DispatchStatus = Sent` ไม่ใช่ `Pending` (กันไม่ให้ deploy ยิงอีเมลย้อนหลังประกาศเก่า — ดู §2)

**จุดที่ต้องเทียบ field-by-field กับ FE:**
- `AnnouncementResponse.dispatchStatus` (ใหม่, additive) ตรงกับ `notification-api.models.ts`'s `AnnouncementResponse` หลังอัปเดต (ถ้า session ถัดไปทำ FE reconciliation ตาม §4)

**Build/test ทั้งชุดต้องผ่านก่อนปิดงาน:** `dotnet build` 0 warning/0 error, `dotnet test` unit+architecture เขียว (ArchitectureTest ต้องยืนยันว่า reference ใหม่ `Learning → Notification` ไม่หลุด module boundary — คาดว่าผ่านเพราะ Notification ไม่มี Domain/Infrastructure ให้ Learning แตะเลย มีแต่ `Contracts/`)

## Changelog

(ยังไม่มี revision หลัง freeze)
