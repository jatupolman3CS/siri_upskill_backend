# Contract: P11-10 Instructor dashboard summary — KPI จริง + `nextSession` + `liveAttendance` (เลิกข้อมูลปลอมทั้ง FE และ backend)

Status: FROZEN · วันที่: 2026-10-06 · Module: `Siri.Modules.Analytics` (Repository+Service/UPPERCASE entity — แต่ feature นี้เป็น handler `Features/InstructorAnalytics`) + Contracts ใหม่ใน `Catalog`, `Payout`, `Learning` + อ่าน `Live.Contracts`

ผู้ลงมือที่แนะนำ: **Claude `backend-developer`** — มี Contracts ข้ามโมดูลใหม่ 3 ตัว + ตัวเลขรายได้ของผู้สอน (เงิน) + ข้อมูลส่วนบุคคลของผู้เรียน · ส่วนจอ FE (`instructor-dashboard-page`) = `frontend-developer` หรือ Antigravity ได้หลัง contract นี้ FROZEN และ endpoint พร้อม · ไม่มี schema delta → ไม่ต้อง DATABASE

**Dependency:** P11-05 (`ILiveAttendanceReader`) + P11-03 (`ILiveScheduleReader` ขยาย §4.1) · JSON/TS → `P11-FE-live-dto-appendix.md` §E

---

## 0. ข้อเท็จจริงจากโค้ด — สิ่งที่ "ปลอม" อยู่จริง (เจ้าของสั่ง: ห้ามมี mock)

**FE** `siri_upskill_ui/src/app/features/instructor/dashboard/instructor-dashboard-page.html` (อ่านแล้ว):
| จุด | ค่าที่ hardcode | แหล่งความจริงที่ต้องใช้ |
|---|---|---|
| การ์ดรายได้ | `฿45,200` / `+14.5% จากเดือนที่แล้ว` | `kpis.netRevenueThisMonth` / `netRevenueChangePercent` |
| การ์ดนักเรียน | `1,280` / `+82 คนในสัปดาห์นี้` | `kpis.totalStudents` / `newStudentsLast7Days` |
| การ์ดคอร์ส | `courses().length` จาก **`searchCourses` สาธารณะ (คอร์สของ *ทุกคน*)** + "ทุกคอร์สเปิดรับสมัครปกติ" | `kpis.publishedCourseCount` / `totalCourseCount` |
| การ์ดรีวิว | `4.9 ★` / `184 รีวิว` | `kpis.ratingAverage` / `ratingCount` |
| ตารางคอร์ส | แสดงคอร์ส **ของทุกคน** และ badge "เผยแพร่แล้ว" ทุกแถวตายตัว | `courseSummaries[]` (ของผู้สอนคนนี้ + `status` จริง) |

**Backend** `GetInstructorAnalyticsHandler.cs` (อ่าน method body แล้ว) — **ข้อมูลที่สร้างขึ้นเองอยู่แล้ว** (ต้องแก้ใน task นี้ด้วย ไม่งั้นยังมี mock):
1. `totalWatchMinutes = totalViews * 6` ("Average 6 minutes per view estimate") — ตัวเลขประมาณการที่ไม่ได้มาจากข้อมูล
2. `contact.Email ?? "student@example.com"` และ `DisplayName ?? "ผู้เรียน"` — อีเมลปลอมหลุดใน response
3. `titles … ?? "Unknown Course"` และ `courseTitle ?? "คอร์สเรียน"` — ชื่อปลอม
4. (สังเกต ไม่แก้ใน task นี้) `Payout/InstructorPayoutController` ส่ง **`userId`** เข้า `InstructorEarningsService` แต่ `REVENUE_SPLITS.INSTRUCTOR_ID` ถูกเขียนด้วย **`InstructorProfile.Id`** (`CoursePriceInfo.InstructorId = course.InstructorId`; `StripeWebhookHandler.cs:277,305`) และ `PayoutBatchService.cs:240` เทียบ `item.INSTRUCTOR_ID != userId` — **ดูเหมือน identity ไม่ตรงกัน** (หน้า earnings/ใบหัก ณ ที่จ่าย อาจคืนว่างเสมอ) ยัง **ไม่ได้ verify กับ DB จริง** → ควรเปิดเป็นงานแยก (เสนอ `X-33`) — **task นี้ต้องใช้ `InstructorProfile.Id` ที่ถูกต้องและห้ามลอกพฤติกรรมนั้น**
5. `REVENUE_SPLIT.PERIOD_KEY` = เดือน **UTC** `yyyy-MM` ณ เวลาสร้าง split (`RevenueSplitContract.cs:30`) และ refund หลังจ่ายเงินแล้วสร้างแถว adjustment ติดลบในงวดปัจจุบัน — การรวม `INSTRUCTOR_AMOUNT` (STATUS ≠ Reversed) ต่อ `PERIOD_KEY` จึงเป็น "รายได้สุทธิ" ที่ถูกต้องและมี index รองรับอยู่แล้ว (`IX_REVENUE_SPLITS_PAYOUT` = INSTRUCTOR_ID, PERIOD_KEY, STATUS) → **ไม่ต้องมี migration**

## 1. Scope
**ใน:** Contracts ใหม่ (§2) · ขยาย `GET /api/analytics/instructor/dashboard/summary` (additive) (§3) · แก้ข้อมูลปลอมในข้อ 1–3 ข้างบน (§4) · unit/integration test · spec จอ FE (appendix §E)
**นอก:** แก้ปัญหา identity ของ Payout earnings (ข้อ 4 — งานแยก) · เปลี่ยน `AnalyticsRollupJob` · roster ต่อคาบ (อยู่ P11-05) · หน้า `/instructor/sessions/:id` (อยู่ P11-05 API / FE P11-25)

## 2. Contracts ข้ามโมดูลใหม่ (ทั้งหมด additive)

### 2.1 `Catalog.Contracts.IInstructorCourseStatsReader` (Catalog implement, Scoped)
```csharp
public sealed record InstructorCourseStat(Guid CourseId, string Title, string Slug, string Status, string DeliveryFormat,
    decimal Price, int EnrollmentCount, decimal RatingAverage, int RatingCount, string? ThumbnailUrl);   // Status/DeliveryFormat = ชื่อ enum เป็น string (ไม่ leak Domain enum)
public sealed record InstructorCourseStatsInfo(Guid? InstructorProfileId, IReadOnlyList<InstructorCourseStat> Courses);
public interface IInstructorCourseStatsReader
{   /// InstructorProfileId = null ถ้าผู้ใช้ไม่มีโปรไฟล์ผู้สอน (Courses ว่าง) — ≤200 คอร์ส เรียง CreatedAtUtc ล่าสุดก่อน; อ่าน denormalized EnrollmentCount/RatingAverage/RatingCount ของ COURSE
    Task<InstructorCourseStatsInfo> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken ct); }
```
query เดียว join `InstructorProfiles()` → `Courses()` (global soft-delete filter ตัดคอร์สที่ลบ) `.AsNoTracking()` ฉายเป็น record ตรง ๆ

### 2.2 `Payout.Contracts.IInstructorRevenueReader` (Payout implement, Scoped)
```csharp
public interface IInstructorRevenueReader
{   /// Σ REVENUE_SPLITS.INSTRUCTOR_AMOUNT ที่ INSTRUCTOR_ID == instructorProfileId, PERIOD_KEY == periodKey ("yyyy-MM"), STATUS != Reversed (รวมแถว adjustment ติดลบ)
    Task<decimal> GetNetRevenueForPeriodAsync(Guid instructorProfileId, string periodKey, CancellationToken ct); }
```
ใช้ `IRevenueSplitRepository.Query()` + `SumAsync(r => (decimal?)r.INSTRUCTOR_AMOUNT) ?? 0m` · **พารามิเตอร์เป็น `InstructorProfile.Id`** (ไม่ใช่ userId — ดู §0 ข้อ 4)

### 2.3 `Learning.Contracts.ILearningAnalyticsContract` — เพิ่ม default method (คืนค่าว่าง/0 เป็น default)
```csharp
public sealed record LearnerCounts(int DistinctLearners, int DistinctLearnersSince);
Task<LearnerCounts> GetLearnerCountsAsync(IReadOnlyCollection<Guid> courseIds, DateTime sinceUtc, CancellationToken ct);
    // DistinctLearners = COUNT(DISTINCT USER_ID) ของ enrollment STATUS ∈ {Active, Expired} ในคอร์สเหล่านั้น (Revoked ไม่นับ) · …Since = เฉพาะที่ ENROLLED_AT_UTC >= sinceUtc
Task<long> GetWatchedSecondsAsync(IReadOnlyCollection<Guid> courseIds, DateTime sinceUtc, CancellationToken ct);
    // Σ EPISODE_PROGRESS.WATCHED_SECONDS ของ progress ที่ UPDATED_AT_UTC >= sinceUtc และ enrollment อยู่ในคอร์สเหล่านั้น (ข้อมูลจริงจากการดู ไม่ใช่ประมาณการ — ความหมาย: "วินาทีสะสมของแถวที่มีการขยับในช่วง")
```
`LearningAnalyticsContract` override ด้วย query เดียวต่อเมธอด (join `Enrollments` ↔ `EpisodeProgresses`) · `.AsNoTracking()`

### 2.4 project references: `Siri.Modules.Analytics.csproj` เพิ่ม `Siri.Modules.Payout` และ `Siri.Modules.Live` (เฉพาะ `.Contracts`) — เช็คไม่มี cycle (Live/Payout ไม่อ้าง Analytics)

## 3. API — ขยาย response เดิม (additive ต่อท้าย; field เดิมคงชื่อ/ลำดับ/ชนิดเดิม ยกเว้น §4)
`GET /api/analytics/instructor/dashboard/summary?range=7d|30d|90d&courseId=` (`InstructorOnly`; endpoint/ownership เดิม: ข้อมูลทั้งหมดมาจาก `IUserContext.UserId` → `courseIds` ของผู้สอนคนนั้นเท่านั้น; `courseId` ที่ไม่ใช่ของตนถูกเมินเหมือนเดิม) · เพิ่ม `[EnableRateLimiting("live-user")]` (per-user — query เยอะขึ้น; policy นิยามใน P11-03 §3.5)

```csharp
public sealed record InstructorAnalyticsResponse(
    /* …8 field เดิมครบ… */ int TotalViews, int TotalWatchMinutes, decimal AvgCompletionRate, decimal TotalRevenue, int TotalEnrollments,
    IReadOnlyList<InstructorDropOffStat> DropOffStats, IReadOnlyList<InstructorStudentProgress> Students, IReadOnlyList<InstructorCourseOption> Courses,
    InstructorDashboardKpis? Kpis = null,                         // ไม่ผูกกับ range/courseId — ทั้งบัญชีผู้สอน
    IReadOnlyList<InstructorCourseSummaryItem>? CourseSummaries = null,
    InstructorNextSession? NextSession = null,
    IReadOnlyList<InstructorLiveAttendanceItem>? LiveAttendance = null);

public sealed record InstructorDashboardKpis(
    string PeriodKey, string PreviousPeriodKey,                   // "2026-10" / "2026-09" (เดือน UTC ตรงกับ REVENUE_SPLITS.PERIOD_KEY)
    decimal NetRevenueThisMonth, decimal NetRevenuePreviousMonth, decimal? NetRevenueChangePercent,   // null เมื่อเดือนก่อน = 0
    int TotalStudents, int NewStudentsLast7Days,
    int PublishedCourseCount, int TotalCourseCount,
    decimal? RatingAverage, int RatingCount);                     // ถ่วงน้ำหนักด้วย RatingCount ข้ามคอร์ส; null เมื่อยังไม่มีรีวิว

public sealed record InstructorCourseSummaryItem(Guid CourseId, string Title, string Slug, string Status, string DeliveryFormat,
    decimal Price, int EnrollmentCount, decimal? RatingAverage, int RatingCount, string? ThumbnailUrl);   // ≤50 แถว, ล่าสุดก่อน

public sealed record InstructorNextSession(Guid SessionId, Guid CourseId, string CourseTitle, string Title, DateTime StartsAtUtc, DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState, int ExpectedLearners, bool MeetingUsable);                  // null เมื่อไม่มีคาบที่ยังไม่จบ

public sealed record InstructorLiveAttendanceItem(Guid SessionId, Guid CourseId, string CourseTitle, string Title, DateTime StartsAtUtc,
    int ExpectedLearners, int JoinedLearners, decimal? AttendanceRatePercent);                        // ล่าสุด 5 คาบที่จบแล้ว (ไม่รวม Cancelled); rate = Joined*100/Expected (1 ตำแหน่ง), null เมื่อ Expected = 0
```
### 3.1 วิธีคำนวณ (ทั้งหมดอ่านจากข้อมูลจริง — ไม่มี estimate/placeholder)
```
stats   = IInstructorCourseStatsReader.GetByInstructorUserIdAsync(userId)      → profileId, courses
now     = IClock.UtcNow ; periodKey = now.ToString("yyyy-MM") ; previousKey = now.AddMonths(-1).ToString("yyyy-MM")   (เดือน UTC — เหตุผลใน §0 ข้อ 5; FE แสดงชื่อเดือนจาก periodKey)
revenue = profileId is null ? (0,0) : (GetNetRevenueForPeriodAsync(profileId, periodKey), …(previousKey))
learners= ILearningAnalyticsContract.GetLearnerCountsAsync(courseIds, now.AddDays(-7))
rating  = Σ(RatingAverage × RatingCount) / Σ RatingCount  (เฉพาะ RatingCount > 0; ปัดเศษ 2 ตำแหน่ง)
next    = ILiveScheduleReader.GetInstructorSessionContextsAsync(userId, from: now, to: null, includeCancelled:false, newestFirst:false, skip:0, take:1)   // ช่วงทับซ้อน = รวมคาบที่กำลังสอน
past    = …(from: null, to: now, includeCancelled:false, newestFirst:true, skip:0, take:20) → กรอง EndsAtUtc <= now → 5 แรก
liveStats = ILiveAttendanceReader.GetSessionStatsAsync(ids ของ next + past) (batch เดียว)
DisplayState = LiveSessionDisplayStateCalculator.Compute(…) (Catalog.Contracts)
```
ไม่มีคอร์ส/ไม่มีโปรไฟล์ผู้สอน → `Kpis` เป็นศูนย์/null ที่เหมาะสม (ไม่ throw), `CourseSummaries` ว่าง, `NextSession=null`, `LiveAttendance=[]` — response เดิม `(0,0,0m,0m,0,[],[],[])` ต้องคงพฤติกรรมเดิม + ฟิลด์ใหม่เป็นค่าว่างตามนี้

## 4. แก้ข้อมูลปลอมของ endpoint เดิม (breaking เล็กน้อยทาง nullability — FE หน้า analytics ต้องรองรับ)
| เดิม | ใหม่ |
|---|---|
| `TotalWatchMinutes = totalViews * 6` | `= (int)(await GetWatchedSecondsAsync(targetCourseIds, fromDate) / 60)` |
| `InstructorStudentProgress(string Id, string Name, string Email, …)` ใช้ `?? "student@example.com"`/`"ผู้เรียน"` | `Name`/`Email` เป็น **`string?`** — ไม่พบ contact (ผู้ใช้ถูกลบ) = `null` (JSON `null`) · FE แสดงข้อความ i18n "ไม่ทราบชื่อ" |
| `courseOptions` ใช้ `?? "Unknown Course"`; students ใช้ `?? "คอร์สเรียน"` | **ตัดแถวที่หาชื่อคอร์สไม่ได้ออก** (คอร์สถูกลบ) — ไม่แต่งชื่อ |
unit test (regression ต่อข้อมูลปลอม): response ไม่มีสตริง `example.com`/`Unknown Course`/`คอร์สเรียน`/`ผู้เรียน` ที่มาจากโค้ดเมื่อไม่พบ contact/ชื่อ · `TotalWatchMinutes` = ค่าจาก contract ไม่ใช่ `views*6` · เทสต์เดิมของ handler ที่ assert ค่าประมาณการต้องแก้ตามนี้ (บันทึกใน PR)

## 5. Test checklist
**Unit (`GetInstructorAnalyticsHandlerTests` ขยาย — fake ทุก contract):** KPI revenue เดือนนี้/ก่อนหน้า (`periodKey` ตาม `FakeClock` ข้ามปี ธ.ค.→ม.ค.), `NetRevenueChangePercent` (prev=0 → null; ลดลง → ติดลบ), ถ่วงน้ำหนักรีวิว (2 คอร์ส 5.0×10 กับ 4.0×30 = 4.25; ไม่มีรีวิว → null), นับ Published/Total, ไม่มี profile → ศูนย์ · `NextSession` (คาบกำลังสอน = `Live`; ไม่มี = null), `LiveAttendance` (rate, Expected=0 → null, เรียงล่าสุดก่อน, ไม่เกิน 5, ไม่รวม Cancelled/ยังไม่จบ) · **IDOR:** ผู้สอน B ไม่เห็นคอร์ส/คาบ/รายได้ของ A (fake ส่งข้อมูลหลายผู้สอน แล้ว assert ฟิลเตอร์ด้วย userId)
**Unit (contract impl):** `InstructorCourseStatsReader` (soft-delete, ≤200, mapping string), `InstructorRevenueReader` (Reversed ไม่รวม, adjustment ติดลบรวม, คนละ instructor ไม่รวม), `LearningAnalyticsContract.GetLearnerCountsAsync` (Revoked ไม่นับ, distinct ข้ามคอร์ส), `GetWatchedSecondsAsync`
**Integration (`SiriApiFactory`):** สร้างข้อมูลผ่าน domain/handler จริง (คอร์ส Published, enrollment, split ผ่าน `IRevenueSplitContract`, คาบ+join log) → `GET /summary` ได้ตัวเลขตรง; ผู้ใช้ Learner role → 403; ไม่ login → 401 · ไม่มีค่าที่ hardcode (ตรวจด้วยการเปลี่ยนข้อมูลแล้วตัวเลขเปลี่ยนตาม)

## 6. Frontend (appendix §E) — สรุป
`InstructorDashboardPage` ต้องเลิก `searchCourses` สาธารณะ และเรียก `GET /api/analytics/instructor/dashboard/summary` ตัวเดียว (+ `GET /api/live/instructor/sessions?scope=upcoming` เมื่อมีหน้ารายการคาบ) · ทุกตัวเลข/ข้อความสถานะมาจาก response (รวม badge สถานะคอร์สจาก `status` จริง ไม่ใช่ "Published" ตายตัว) · loading skeleton / error / empty state ครบ · i18n ทั้งหมด (`P4-24/P4-27` ที่ยัง hardcode ไทยปิดพร้อมกัน) · เดือนแสดงจาก `periodKey` ผ่าน pipe วันที่ที่ตั้ง `Asia/Bangkok`

## 7. Work packages
| WP | ใคร | ทำอะไร | Acceptance |
|---|---|---|---|
| **A** | `backend-developer` | Contracts §2.1–2.4 + implementation + unit | build 0 warning · architecture เขียว · ไม่มี migration |
| **B** | `backend-developer` | handler/response ใหม่ + แก้ข้อมูลปลอม §4 + `[EnableRateLimiting]` + แก้ unit test เดิมที่ผูกกับ estimate | unit §5 เขียว |
| **C** | `backend-developer` | integration tests §5 | เขียนครบ (รายงานว่ายังไม่เคยรันถ้าไม่มี Docker) |
| **D** | `frontend-developer` (หรือ Antigravity หลัง B) | ต่อสายจอ dashboard ตาม appendix §E | ไม่มีค่า hardcode; lint/test/build เขียว |

## Changelog
(ยังไม่มี revision หลัง FROZEN)
