# Contract: P11-03 `Siri.Modules.Live` (meetings) + `Siri.Integrations.Google` — เชื่อม Google ของผู้สอน, ห้องประชุมต่อคาบ, ลิงก์ Manual

Status: FROZEN · วันที่: 2026-10-06 · Module: **`Siri.Modules.Live` ใหม่ — Repository+Service + UPPERCASE entity/property/DB (schema `LIVE`, D-17)** · `Siri.Integrations.Google` ใหม่ (HttpClient ล้วน ไม่มี Google SDK) · แตะ `Siri.Modules.Catalog` (vertical slice, entity UPPERCASE class + PascalCase property — ดู P11-01 contract ส่วน "แก้ข้อสมมติ") เฉพาะ Contracts + 2 handler + DI

ผู้ลงมือที่แนะนำ: **Claude — `DATABASE` (WP-A) → `backend-developer` (WP-B..F)** — เหตุผล: OAuth credential ของผู้สอน (refresh token), Contracts ข้ามโมดูลใหม่/แก้ 5 จุด, publish gate ที่กระทบสิทธิ์ขาย → ตรงเกณฑ์ charter ที่ห้ามส่ง Antigravity · **ไม่ต้องผ่าน DESIGN_DATABASE** (§2 ละเอียดระดับคอลัมน์/index/FK/ชื่อ constraint แล้ว 2 ตาราง)

> **ชุดสัญญาที่ต้องอ่านคู่กัน (ออกพร้อมกัน 2026-10-06):** `P11-04-live-invites-ics-reminders.md` · `P11-05-live-learner-instructor-api-join-gate.md` · `P11-06-live-recording-catchup.md` · `P11-12-refund-hard-block-after-live-join.md` · `P11-10-instructor-dashboard-summary.md` · **`P11-FE-live-dto-appendix.md` (JSON/TypeScript ของทุก endpoint + จอ FE)** · ลำดับ: P11-03 → 04 → 05 → 06/12/10 (ดู §10 + แต่ละไฟล์)

---

## 0. สิ่งที่ยืนยันจากโค้ดจริงแล้ว (อ่าน method body — ไม่เดา) และจุดที่ **ต่างจาก `TASKS.md`/`HYBRID_LIVE.md`**

| # | ข้อเท็จจริงจากโค้ด | ผลต่อ contract |
|---|---|---|
| F1 | `ILiveMeetingSink` มีแค่ `OnSessionScheduledAsync/ChangedAsync/CancelledAsync(sessionId, ct)` (`Catalog/Contracts/ILiveMeetingSink.cs`) และ 3 handler (`CreateLiveSession/UpdateLiveSession/CancelLiveSession`) เรียกมัน **ก่อน** `SaveChangesAsync` ครั้งเดียว (`CreateLiveSession/Handler.cs:78-81`) — session ยังไม่อยู่ใน DB ตอน sink ถูกเรียก | sink **ทำได้แค่ stage แถวด้วย sessionId** (ห้าม query หา session/instructor) — ผู้สอน/คอร์สถูกอ่านทีหลังโดย job ผ่าน contract ใหม่ (§4.1) |
| F2 | `NullLiveMeetingSink` ลงทะเบียนเป็น **Singleton** ใน `AddCatalogModule` (`CatalogModule.cs:220` ข้าง `ILiveScheduleReader` บรรทัด 221) | Live ต้องทับด้วย Scoped — ใช้ `TryAddSingleton` ฝั่ง Catalog + `AddScoped` ฝั่ง Live เพื่อ **ไม่ขึ้นกับลำดับ** `AddXxxModule` (§3.4) |
| F3 | `LiveSessionInfo` ไม่มี instructor/course title/slug (`ILiveScheduleReader.cs`) และ `Siri.SharedKernel.SeoOptions` ไม่ใช่ของ Live (`SeoOptions` อยู่ใน `Catalog.Infrastructure`) | เพิ่ม `LiveSessionContext` + 4 default method ใน `ILiveScheduleReader` (§4.1); Live bind `Live:PublicBaseUrl` เอง (fallback `Seo:PublicBaseUrl` ตอน `AddLiveModule`) |
| F4 | `SubmitCourseForReviewHandler`/`ApproveCourseHandler` มี pre-check publish invariant แล้ว (`CourseMediaReadiness`) แต่ **ไม่รู้เรื่องห้องประชุม** | เพิ่ม `ILiveMeetingReadinessReader` (§4.2) gate ทั้งสอง handler |
| F5 | `DomainError` มี code แค่ 4 ตัว (`not_found/validation/forbidden/conflict`) → `ToProblemHttpResult` map 404/400/403/409 และ **code อื่นตก 400**; ไม่มี 503 และแนบ extension ไม่ได้ (`DomainErrorHttpResults.cs`) | แก้ SharedKernel แบบ additive: `Reason` + `Extensions` + `DomainError.Unavailable` → 503 (§4.5) |
| F6 | Rate limiter มี "default" = **fixed-window ทั้งแอปรวมทุกคน 100 req/นาที** (`Program.cs:196-203`) + "heartbeat" partitioned ต่อ user (`RateLimiterConfiguration.cs`) — เทรปเดียวกับ X-29 | endpoint Live **ห้ามใช้ "default"** — เพิ่ม partitioned policy ใหม่ 3 ตัว (§3.5) |
| F7 | Redis `IConnectionMultiplexer` ใช้ร่วมกัน (`AddSharedRedis`); dev ใช้ Garnet (`dev.ps1`) — **อาจไม่รองรับ `GETDEL`** | OAuth state เก็บ Redis แล้ว consume ด้วย `StringGet` + `KeyDelete` (ตัวที่ลบสำเร็จชนะ) ไม่ใช้ `GETDEL` (§6.1) |
| F8 | Persistence สแกน entity เฉพาะ assembly ชื่อ `Siri.Modules.*` ที่โหลดแล้ว (`AppDbContext.GetModuleAssemblies`) + `DatabaseIdentifierLengthTests` มี `Assert.Equal(10, moduleCount)` | เพิ่ม Live ใน `ModuleAssemblyCatalog` **และแก้ตัวเลข 10 → 11** (§3.3) |
| F9 | FK constraint ชื่อ auto-gen ยาว (`FK_SESSION_MEETINGS_INSTRUCTOR_GOOGLE_ACCOUNTS_..._ID` = 64 ไบต์ > 63) | ตั้งชื่อ constraint/index ทุกตัวเองตาม §2 ห้ามพึ่ง auto-gen |
| F10 | Google OAuth client ที่มีแล้ว (`Identity:ExternalLogin:Google:ClientId`) เป็นแบบ **ID-token (ไม่มี secret)** สำหรับ "Sign in with Google" — ไม่ใช่ authorization-code+secret | Live ใช้ section แยก `Integrations:Google:*` (ClientId/ClientSecret/RedirectUri) — เจ้าของโปรเจ็คจะ **ใช้ client เดิมก็ได้** (เพิ่ม redirect URI + เอา secret มาใส่) หรือสร้างใหม่ |
| F11 | Google docs (เช็ค 2026-10-06): scope ที่ `events.insert` รับ = `calendar` / `calendar.events` / **`calendar.events.owned`**; refresh token ของแอปสถานะ **Testing หมดอายุใน 7 วัน**; `access_type=offline` + `prompt=consent` เพื่อให้ได้ refresh token; Meet REST `spaces.patch config.accessType=RESTRICTED` มีจริงแต่ scope/ข้อจำกัดบัญชี Gmail ธรรมดา **ยังไม่ยืนยัน** | default scope = `calendar.events.owned` (แคบสุด, config เปลี่ยนได้); Testing-mode = reconnect ทุกสัปดาห์ (§11); `RESTRICTED` เป็น follow-up ไม่อยู่ใน v1 (§9.3) |

**ต่างจาก `TASKS.md` P11-03 / `HYBRID_LIVE.md` (Contract ฉบับนี้ชนะ):**
1. **`GET .../google/connect` (redirect) → `POST .../google/connect` คืน `{ authorizationUrl }`** — bearer token ของ SPA อยู่ใน memory ส่งไปกับ top-level navigation ไม่ได้; เป็น POST เพราะสร้าง state ฝั่ง server
2. `INSTRUCTOR_GOOGLE_ACCOUNTS` key ด้วย **`INSTRUCTOR_USER_ID`** (ไม่ใช่ `INSTRUCTOR_PROFILE_ID`) — ตอน connect Live รู้แค่ `IUserContext.UserId` (ไม่มีสิทธิ์อ่าน InstructorProfile)
3. `SESSION_MEETINGS.MEET_URL` → **`MEET_URL_ENCRYPTED`** (ผ่าน `ISensitiveDataProtector`) — ลิงก์ห้อง = capability URL ที่เป็นสิทธิ์เข้าเรียน ห้าม plaintext ใน DB/dump
4. `SYNC_STATUS` เพิ่ม **`AwaitingLink`, `PendingDelete`** (HYBRID_LIVE เดิมให้ Manual = `Synced` ทั้งที่ไม่มี URL — ทำให้ gate "ต้องมีห้องใช้ได้" ตรวจไม่ได้)
5. **ไม่ใช้ `ICalendarProvider.AddAttendeeAsync/RemoveAttendeeAsync`** → `SetAttendeesAsync(ทั้งรายการ)` (Calendar REST แทนที่ `attendees` ทั้งอาร์เรย์) — รายละเอียดอยู่ P11-04
6. เพิ่ม provider mode `ManualOnly` และ `Logging` (dev-only) ผ่าน `Live:Provider` — **ไม่มี Logging เป็น default ใน production** (`ProductionConfigurationGuard` ปฏิเสธ)
7. ลิงก์ Manual: เพิ่ม endpoint `PUT /api/live/instructor/sessions/{sid}/meeting-link` + allow-list https (ผู้ใช้ขอเพิ่ม) · path อยู่ใน namespace `/api/live` (ไม่ใช่ `/api/catalog/...`) เพราะ logic เป็นของ Live

---

## 1. Scope & task IDs

**อยู่ในขอบเขต (P11-03):**
- project ใหม่ `Siri.Integrations.Google` (`IGoogleOAuthService`, `ICalendarProvider`, impl HttpClient, `Logging*` สำหรับ dev) — **ห้ามเพิ่ม NuGet `Google.Apis.*`** (Q10: ยังไม่มี OAuth client จริง; HttpClient ไม่ต้องมี package ใหม่)
- project ใหม่ `Siri.Modules.Live` — Domain/Infrastructure/Application ของ `INSTRUCTOR_GOOGLE_ACCOUNT` + `SESSION_MEETING` เท่านั้น (ตาราง invite/join log เป็นของ P11-04/05)
- Endpoint 7 ตัว: Google status/connect/callback/disconnect · list meetings ของคอร์ส · meeting-link PUT · resync (§7)
- `ILiveMeetingSink` impl จริง (`LiveMeetingSink`) + job `live-meeting-sync` + adopt-orphans
- Catalog: Contracts ใหม่ (`ILiveScheduleReader` ขยาย, `ILiveMeetingReadinessReader`) + gate ใน `SubmitCourseForReviewHandler`/`ApproveCourseHandler` + `TryAddSingleton` ของ Null default
- SharedKernel: `DomainError` additive (§4.5)
- Config/Options/ProductionConfigurationGuard/rate-limit policy/`ModuleAssemblyCatalog`/DI ใน `Siri.Api` + `Siri.Workers`
- migration `AddLiveMeetings`

**นอกขอบเขต (ห้ามทำใน P11-03):**
- `SESSION_INVITES`, ICS, outbox calendar part, reminder, attendee sync → **P11-04**
- join gate / `SESSION_JOIN_LOGS` / my-sessions / roster → **P11-05**
- attach recording → **P11-06** · refund block → **P11-12** · dashboard KPI → **P11-10**
- Meet REST API (`accessType=RESTRICTED`) → follow-up §9.3 · ดึง recording จาก Drive → ไม่ทำ (Q13.5)
- FE ทั้งหมด (ดู `P11-FE-live-dto-appendix.md`)

---

## 2. Schema delta — migration `AddLiveMeetings` (additive ล้วน, ไม่มี data migration)

> ตาราง/คอลัมน์ทั้งหมด UPPERCASE ผ่าน `ApplyUppercaseNamingConventions` · C# property **UPPER_SNAKE_CASE ยกเว้น `IAuditable` (`CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy`) ต้อง PascalCase** · เวลา `timestamptz(3)` UTC (`HasPrecision(3)`) · PK uuid UUIDv7 (`UuidV7.NewId()`) · enum เก็บ string `HasConversion<string>()` · concurrency token = `ROW_VERSION bytea` (`ConcurrencyTokenInterceptor`) · **ไม่มี FK ข้าม schema/module** (เหตุผลเดียวกับ `ENROLLMENT.USER_ID`) · **ไม่มี cascade delete** ทุกเส้น

### 2.1 `LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS` (entity `INSTRUCTOR_GOOGLE_ACCOUNT : IAuditable`)

| Column / C# property | Type | Null | หมายเหตุ |
|---|---|---|---|
| `INSTRUCTOR_GOOGLE_ACCOUNT_ID` | uuid PK | no | |
| `INSTRUCTOR_USER_ID` | uuid | no | `identity.Users.Id` (ไม่มี FK) · **UNIQUE** (1 ผู้สอน = 1 บัญชี Google) |
| `GOOGLE_SUBJECT` | varchar(64) | no | `sub` จาก userinfo |
| `GOOGLE_EMAIL` | varchar(320) | no | แสดงใน UI ว่าเชื่อมบัญชีไหน |
| `REFRESH_TOKEN_ENCRYPTED` | text | **yes** | `ISensitiveDataProtector.Encrypt` · **null หลัง disconnect/revoked** (ล้างทิ้ง — ห้ามเก็บ token ที่ใช้ไม่ได้แล้ว) |
| `SCOPES` | varchar(500) | no | scope ที่ Google ตอบกลับจริง |
| `CONNECTED_AT_UTC` | timestamptz(3) | no | |
| `LAST_VALIDATED_AT_UTC` | timestamptz(3) | yes | refresh สำเร็จล่าสุด |
| `REVOKED_AT_UTC` | timestamptz(3) | yes | |
| `REVOKED_REASON` | varchar(40) | yes | `invalid_grant` \| `user_disconnected` \| `scope_missing` \| `insufficient_scope` |
| `ROW_VERSION` | bytea | no | concurrency token |
| `CreatedAtUtc/CreatedBy/UpdatedAtUtc/UpdatedBy` | IAuditable | | PascalCase |

Index/ชื่อ: PK `PK_INSTRUCTOR_GOOGLE_ACCOUNTS` · UQ `IX_INSTR_GOOGLE_ACCT_USER_ID` (`INSTRUCTOR_USER_ID`) · ไม่มี index อื่น
Domain methods (entity ห้ามรู้จัก EF/HTTP; `private set`): `static Connect(userId, subject, email, refreshTokenEncrypted, scopes, IClock)` · `Reconnect(subject, email, refreshTokenEncrypted, scopes, IClock)` (ล้าง `REVOKED_*`, ตั้ง `CONNECTED_AT_UTC`) · `MarkValidated(IClock)` · `MarkRevoked(reason, IClock)` (**ตั้ง `REFRESH_TOKEN_ENCRYPTED = null`**) · `bool IsActive => REFRESH_TOKEN_ENCRYPTED != null && REVOKED_AT_UTC == null`

### 2.2 `LIVE.SESSION_MEETINGS` (entity `SESSION_MEETING : IAuditable`)

| Column / C# property | Type | Null | หมายเหตุ |
|---|---|---|---|
| `SESSION_MEETING_ID` | uuid PK | no | |
| `SESSION_ID` | uuid | no | `CATALOG.COURSE_LIVE_SESSIONS.Id` (ไม่มี FK — cross-schema) · **UNIQUE** |
| `INSTRUCTOR_USER_ID` | uuid | yes | denormalize ตอน job ประมวลผลครั้งแรก (จาก `LiveSessionContext`) — ใช้ reset ตอนผู้สอน reconnect |
| `PROVIDER` | varchar(20) | yes | enum `MeetingProvider`: `GoogleMeet` \| `Manual` \| `Logging` · **null = ยังไม่ตัดสิน** (sink stage ตอน provider ยังไม่รู้) |
| `INSTRUCTOR_GOOGLE_ACCOUNT_ID` | uuid | yes | FK → `INSTRUCTOR_GOOGLE_ACCOUNTS` **NoAction** (โมดูลเดียวกัน; constraint ชื่อ `FK_SESSION_MEETINGS_GOOGLE_ACCT`) |
| `PROVIDER_EVENT_ID` | varchar(200) | yes | Google Calendar event id |
| `MEET_URL_ENCRYPTED` | text | yes | `ISensitiveDataProtector.Encrypt(url)` — **อ่านผ่าน `SessionMeetingService.RevealUrl` เท่านั้น** |
| `SYNC_STATUS` | varchar(20) | no | enum `MeetingSyncStatus` (§2.3) |
| `ICS_SEQUENCE` | int | no | default 0 · เพิ่มเมื่อเวลา/ชื่อคาบเปลี่ยนหรือคาบถูกยกเลิก (P11-04 ใช้) |
| `ATTEMPTS` | int | no | default 0 |
| `NEXT_RETRY_AT_UTC` | timestamptz(3) | yes | |
| `LAST_SYNC_AT_UTC` | timestamptz(3) | yes | |
| `ERROR` | varchar(500) | yes | **เก็บเฉพาะ error code + ข้อความสั้น — ห้ามมี token/URL/อีเมล** |
| `MEETING_ALERT_SENT_AT_UTC` | timestamptz(3) | yes | กัน alert ผู้สอนซ้ำ (P11-04 reminders ใช้) |
| `ROW_VERSION` | bytea | no | |
| audit | | | PascalCase |

Index/ชื่อ: PK `PK_SESSION_MEETINGS` · UQ `IX_SESSION_MEETINGS_SESSION_ID` · `IX_SESSION_MEETINGS_SYNC_DUE` on (`SYNC_STATUS`, `NEXT_RETRY_AT_UTC`) `HasFilter("\"SYNC_STATUS\" IN ('Pending','PendingDelete')")` (job หา due) · `IX_SESSION_MEETINGS_INSTR_USER_ID` on (`INSTRUCTOR_USER_ID`) · FK ตัวที่สองไม่มี
> filtered-index ใช้ syntax PostgreSQL (`database.md`) · ตรวจ identifier ≤63 ไบต์ด้วย `DatabaseIdentifierLengthTests` — ชื่อข้างบนยาวสุด 38 ไบต์

### 2.3 Enum + state machine ของ `SESSION_MEETING`

`MeetingSyncStatus`: `Pending` (ต้องสร้าง/แก้ event — job ทำ) · `AwaitingLink` (ผู้สอนไม่ได้เชื่อม Google **และยังไม่มี URL** → รอ Manual) · `Synced` (สงบ — ถ้ามี URL = ห้องพร้อม) · `NeedsReconnect` (token Google ใช้ไม่ได้) · `Failed` (retry ครบแล้ว) · `PendingDelete` (มี Google event ที่ต้องลบ) · `Deleted` (คาบยกเลิก/ไม่มีอะไรต้องทำต่อ)

`IsUsable` (derived, ใช้ทั้ง publish gate และ join gate) = **`MEET_URL_ENCRYPTED != null && SYNC_STATUS != Deleted`** — `Pending` (กำลัง patch event หลังเลื่อนเวลา) และ `NeedsReconnect`/`Failed` ที่ยังมี URL เดิม **ยังใช้ได้**; `PendingDelete` + Manual URL ใช้ได้ (ดู `SetManualLink`); คาบที่ถูกยกเลิกถูกตัดที่ join gate ข้อ 3 และไม่อยู่ใน publish gate (นับเฉพาะคาบ Scheduled อนาคต) อยู่แล้ว

Domain methods (ทั้งหมด `public`/`internal` ตาม repo pattern — **ไม่มี setter ภายนอก**): 

| method | ผล |
|---|---|
| `static Stage(Guid sessionId)` | `SYNC_STATUS=Pending`, `PROVIDER=null`, `ICS_SEQUENCE=0`, `ATTEMPTS=0` |
| `MarkSessionChanged()` | `ICS_SEQUENCE++` · ถ้า `PROVIDER==GoogleMeet && PROVIDER_EVENT_ID!=null` → `Pending`, `ATTEMPTS=0`, `NEXT_RETRY=null` · Manual/อื่น → ไม่เปลี่ยน status |
| `MarkSessionCancelled()` | `ICS_SEQUENCE++` · มี `PROVIDER_EVENT_ID` → `PendingDelete` · ไม่มี → `Deleted` |
| `SetManualLink(string urlEncrypted)` | `PROVIDER=Manual`, `MEET_URL_ENCRYPTED=…`, `ERROR=null` · มี `PROVIDER_EVENT_ID` → `PendingDelete` (ให้ job ลบ event Google ทิ้ง) ไม่งั้น → `Synced` |
| `RecordGoogleSynced(provider, eventId, urlEncrypted, accountId, clock)` | `PROVIDER=provider` (`GoogleMeet` หรือ `Logging`), ตั้ง event/URL/account, `Synced`, `ATTEMPTS=0`, `NEXT_RETRY=null`, `LAST_SYNC_AT_UTC=now`, `ERROR=null` |
| `AssignProvider(MeetingProvider provider, Guid? accountId)` | ตั้ง `PROVIDER` (+`INSTRUCTOR_GOOGLE_ACCOUNT_ID` เมื่อ `GoogleMeet`) **ก่อนเรียก Google ครั้งแรก** — ทำให้ `PROVIDER != null` ตั้งแต่รอบแรกของ job ไม่ว่าผลจะสำเร็จหรือล้ม (P11-04 รอเงื่อนไขนี้ก่อนส่งอีเมลผู้สอน) |
| `ResolveAsAwaitingLink()` | `PROVIDER=Manual`, `AwaitingLink` (ใช้เมื่อผู้สอนไม่ได้เชื่อม Google และไม่มี URL) |
| `RecordNeedsReconnect(code)` | `NeedsReconnect`, `ERROR=code` (URL เดิมถ้ามีคงอยู่ = ยังใช้ได้) |
| `RecordAttemptFailed(code, clock)` | `ATTEMPTS++`; backoff หลังครั้งที่ 1/2/3/4 = **1/5/15/60 นาที** → ตั้ง `NEXT_RETRY_AT_UTC`; ครั้งที่ 5 → `Failed` (`NEXT_RETRY=null`) |
| `RequestResync()` | ได้เฉพาะ `Failed/NeedsReconnect/AwaitingLink` และ **ไม่มี URL** (หรือ `Failed` มี URL) → `Pending`, `PROVIDER=null`, `ATTEMPTS=0`, `NEXT_RETRY=null` · `Synced` → throw `InvalidOperationException` (service แปลงเป็น 409) |
| `FinishDelete(bool sessionStillScheduled)` | `PROVIDER_EVENT_ID=null` · (`PROVIDER==Manual && URL!=null && sessionStillScheduled`) → `Synced` ไม่งั้น `Deleted` |

---

## 3. โครงสร้างโปรเจ็ค, DI, config

### 3.1 `Siri.Integrations.Google`
```
src/Siri.Integrations.Google/
  Siri.Integrations.Google.csproj      ← ProjectReference: Siri.SharedKernel เท่านั้น · PackageReference: Microsoft.Extensions.Http,
                                          Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.Extensions.Logging.Abstractions
                                          (ทั้งสามมีใน Directory.Packages.props แล้ว — ไม่เพิ่ม package) · InternalsVisibleTo Siri.UnitTests · ไม่อ้าง EF Core
  GoogleOAuthOptions.cs · IGoogleOAuthService.cs · GoogleOAuthService.cs
  ICalendarProvider.cs · GoogleCalendarProvider.cs · GoogleApiDtos.cs (internal records, System.Text.Json)
  Logging/LoggingGoogleOAuthService.cs · Logging/LoggingCalendarProvider.cs      ← dev-only (§3.4)
  GoogleIntegrationServiceCollectionExtensions.cs   ← AddGoogleIntegration(IConfiguration, bool useLogging)
```
`ModuleAssemblyCatalog`: `assemblies["Siri.Integrations.Google"] = typeof(Siri.Integrations.Google.IGoogleOAuthService).Assembly;` (ต้องผ่านเทสต์ `NonModuleAssemblies_DoNotReferenceEntityFrameworkCore`)

### 3.2 `Siri.Modules.Live`
```
src/Siri.Modules.Live/
  Siri.Modules.Live.csproj   ← ProjectReference: SharedKernel, Persistence, Integrations.Google, Modules.Catalog, Modules.Learning, Modules.Identity, Modules.Notification
                                PackageReference: Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational (provider-agnostic เหมือน Analytics/Catalog), Hangfire.Core
                                FrameworkReference Microsoft.AspNetCore.App · StackExchange.Redis มาทาง Persistence (เช็ค restore) · ห้ามอ้าง Commerce/Analytics/Payout/Media
  LiveModule.cs              ← AddLiveModule(IServiceCollection, IConfiguration) (ไม่มี MapLiveEndpoints — endpoint เป็น MVC controller ที่ Siri.Api เท่านั้น, D-19)
  Contracts/                 ← ว่างใน P11-03 (P11-05 เพิ่ม ILiveAttendanceReader)
  Domain/    INSTRUCTOR_GOOGLE_ACCOUNT.cs · SESSION_MEETING.cs · MeetingProvider.cs · MeetingSyncStatus.cs
  Infrastructure/  InstructorGoogleAccountConfiguration.cs · SessionMeetingConfiguration.cs · AppDbContextLiveExtensions.cs
                   InstructorGoogleAccountRepository.cs · SessionMeetingRepository.cs · RedisGoogleOAuthStateStore.cs
                   LiveMeetingSink.cs · LiveMeetingReadinessReader.cs · LiveMeetingSyncJob.cs
  Application/     IInstructorGoogleAccountRepository.cs · ISessionMeetingRepository.cs · IGoogleOAuthStateStore.cs
                   InstructorGoogleAccountService.cs · SessionMeetingService.cs · MeetingLinkValidator.cs · LiveOptions.cs
                   IInstructorAlertSender.cs + InstructorAlertSender.cs   ← ส่งอีเมลแจ้งผู้สอน (IEmailOutbox + IUserContactReader); P11-04 WP-D เติม in-app ผ่าน IUserNotificationOutbox
                   ISessionMeetingRepository: GetBySessionIdAsync · GetBySessionIdsAsync · GetDueAsync(now,take) · GetResettableByInstructorAsync(userId) · Add · SaveChangesAsync
                   DTO/command records + FluentValidation validators (ทุก class PascalCase; entity เท่านั้นที่ UPPERCASE)
```
Controller (ใน `Siri.Api`): `Controllers/Live/LiveGoogleController.cs`, `Controllers/Live/LiveMeetingsController.cs` — `[ApiController]` `[Tags("Live")]` · ทุก action ใส่ `[Authorize]`/`[AllowAnonymous]` **ชัดเจนเสมอ** (ไม่มี fallback policy — `backend.md`) · คืน `IResult` · error → `Error.ToProblemHttpResult(HttpContext)`

### 3.3 การลงทะเบียน (ต้องทำครบ ไม่งั้นเทสต์/โหลด entity พัง)
1. `SiriUpSkill.sln` + `Siri.Api.csproj` + `Siri.Workers.csproj` + `tests/Siri.ArchitectureTests.csproj` + `tests/Siri.UnitTests.csproj` + `tests/Siri.IntegrationTests.csproj`: เพิ่ม ProjectReference `Siri.Modules.Live` (+ `Siri.Integrations.Google` ที่ Architecture/Unit)
2. `Siri.Api/Program.cs` และ `Siri.Workers/Program.cs`: `.AddLiveModule(builder.Configuration)` **หลัง** `.AddCatalogModule(...)`/`.AddLearningModule()`/`.AddNotificationModule(...)` (ลำดับไม่บังคับเพราะ `TryAdd` — แต่ให้เป็นแบบนี้เพื่ออ่านง่าย)
3. `tests/Siri.ArchitectureTests/ModuleAssemblyCatalog.cs`: เพิ่ม `new("Siri.Modules.Live", typeof(Siri.Modules.Live.LiveModule).Assembly)` และ `assemblies["Siri.Integrations.Google"]` · **`DatabaseIdentifierLengthTests`: `Assert.Equal(10, moduleCount)` → `11`** (ห้ามลบ assert ทิ้ง)
4. `Siri.Workers/RecurringJobsRegistration.cs`: `live-meeting-sync` → `Cron.Minutely()` (เพิ่มอีก 2 job ใน P11-04)

### 3.4 `AddLiveModule` — สิ่งที่ลงทะเบียน
- Options (`AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`): `LiveOptions` (section `Live`) — `PostConfigure`: ถ้า `PublicBaseUrl` ว่าง ใช้ `configuration["Seo:PublicBaseUrl"]` (อ่านที่จุดลงทะเบียนครั้งเดียว) · `GoogleOAuthOptions` (section `Integrations:Google`)
- `services.AddGoogleIntegration(configuration, useLogging: Live:Provider == "Logging")` — อ่าน `Live:Provider` ที่จุดลงทะเบียนแบบเดียวกับ `AddEmailIntegration` (precedent `Email:Provider`)
- Scoped: repositories, `InstructorGoogleAccountService`, `SessionMeetingService`, `LiveMeetingSyncJob`, `IValidator<>` ของ command
- **Cross-module impl (Scoped):** `services.AddScoped<ILiveMeetingSink, LiveMeetingSink>()` · `services.AddScoped<ILiveMeetingReadinessReader, LiveMeetingReadinessReader>()`
- Singleton: `IGoogleOAuthStateStore` (Redis)
- ฝั่ง Catalog: `AddSingleton<ILiveMeetingSink, NullLiveMeetingSink>()` → **`TryAddSingleton`** (`Microsoft.Extensions.DependencyInjection.Extensions`) + `TryAddScoped<ILiveMeetingReadinessReader, NullLiveMeetingReadinessReader>()` — Null ทั้งคู่ยังจำเป็นสำหรับ host/เทสต์ที่ไม่โหลด Live

### 3.5 Config keys (ค่า default ใน `appsettings.json` เป็น placeholder ที่ไม่ใช่ secret; ค่าจริงผ่าน user-secrets/env)

| Key (env: `A__B`) | Default | หมายเหตุ |
|---|---|---|
| `Live:Provider` | `GoogleMeet` | `GoogleMeet` (ใช้ Google เมื่อผู้สอนเชื่อมแล้ว ไม่งั้น Manual) · `ManualOnly` (ไม่เรียก Google เลย) · `Logging` (**dev เท่านั้น** — fake OAuth+Meet URL `https://meet.invalid/dev/{id}`) |
| `Live:PublicBaseUrl` | = `Seo:PublicBaseUrl` | origin ของ FE: ใช้สร้าง `…/live/{sid}/join`, redirect หลัง OAuth, host ของ ICS UID · absolute https (http เฉพาะ loopback) |
| `Live:JoinWindowBeforeMinutes` | `15` | 5–120 · ใช้ใน join gate + displayState ของ Live module (หน้า public ของ Catalog ใช้ constant 15 — เฉพาะ badge, ไม่ใช่ gate) |
| `Live:InviteLookaheadDays` | `180` | P11-04 |
| `Live:GoogleAttendeeCap` | `150` | P11-04 (≤190) |
| `Live:AllowedMeetingHosts` | `["meet.google.com","zoom.us","teams.microsoft.com","teams.live.com"]` | ตรงชื่อโฮสต์ **หรือ subdomain ของมัน** (`us02web.zoom.us` ผ่าน) |
| `Live:OrganizerEmail` | `no-reply@<host ของ PublicBaseUrl>` | P11-04 (ICS ORGANIZER) |
| `Integrations:Google:ClientId` | `""` | ว่าง = ปิดฟีเจอร์เชื่อม Google (`configured:false`) **ระบบทั้งหมดยังทำงานด้วย Manual** |
| `Integrations:Google:ClientSecret` | `""` | **SECRET** — `dotnet user-secrets` (dev) / env (prod) · ตั้งทั้ง `Siri.Api` **และ `Siri.Workers`** (UserSecretsId คนละตัว; dev ใช้ `.env`) |
| `Integrations:Google:RedirectUri` | `""` | ต้องตรง Google Cloud เป๊ะ: `{API origin}/api/live/instructor/google/callback` |
| `Integrations:Google:Scopes` | `["openid","email","https://www.googleapis.com/auth/calendar.events.owned"]` | callback ยอมรับถ้า scope ที่ได้คืนมามีอย่างใดอย่างหนึ่งของ `calendar.events.owned`/`calendar.events`/`calendar` |
| `Integrations:Google:PostConnectRedirectPath` | `/instructor/live-settings` | path ใน FE ที่ callback redirect ไป (ต้องขึ้นต้น `/instructor/`) |
| `Integrations:Google:HttpTimeoutSeconds` | `15` | |

`GoogleOAuthOptions` validate (`IValidateOptions`): `ClientId` ว่าง → ผ่าน (ปิดฟีเจอร์) · `ClientId` มีค่า → `ClientSecret` ต้องไม่ว่าง, `RedirectUri` absolute (https หรือ http+loopback), `Scopes` ต้องมี `openid`+`email`+calendar scope อย่างน้อย 1 ตัว · `ProductionConfigurationGuard.ValidateProductionConfiguration` เพิ่ม: `Live:Provider` **ห้ามเป็น `Logging`**; ถ้า `Integrations:Google:ClientId` มีค่า → `ClientSecret` ไม่ว่าง/ไม่มี `CHANGE_ME`, `RedirectUri` ขึ้นต้น `https://`

**Rate-limit policies ใหม่** ใน `Siri.Api/Configuration/RateLimiterConfiguration.cs` (partition key = claim `NameIdentifier`/`sub` ตามตัวอย่าง heartbeat; fallback IP) และลงทะเบียนใน `Program.cs` `AddRateLimiter`:

| ชื่อ | โควตา | ใช้กับ |
|---|---|---|
| `live-join` | 6 req / 60 วิ / ผู้ใช้ | `POST …/join` (P11-05) |
| `live-user` | 60 req / 60 วิ / ผู้ใช้ | endpoint Live อื่นทั้งหมดที่ login แล้ว (status, connect, disconnect, meeting-link, resync, list, my-sessions, ics, roster) |
| `live-google-callback` | 60 req / 60 วิ / IP | `GET …/google/callback` (anonymous — ปริมาณต่ำ; IP อาจเป็น IP ของ proxy จนกว่าจะตั้ง forwarded headers — ยอมรับ) |
เทสต์ policy แบบเดียวกับ `HeartbeatRateLimitIntegrationTests` (ผู้ใช้ A ครบโควตา ผู้ใช้ B ยังผ่าน) — **ห้ามใช้ "default"/"auth"/"webhook"**

**dev stack:** `dev.ps1` `.env` มี `Seo__PublicBaseUrl=http://localhost:4202` แล้ว (Live ใช้ต่อได้) · เจ้าของใส่ `Integrations__Google__ClientId/ClientSecret/RedirectUri` ลง `.env` เอง (ไม่ commit) · dev redirect URI = `http://localhost:5190/api/live/instructor/google/callback` หรือ `http://localhost:4202/api/...` (ถ้าผ่าน proxy ของ UI — ต้องตรงกับที่ลงทะเบียน) · อีเมล dev ออกจริงไป Mailpit อยู่แล้ว (`Email__Provider=Smtp`) — ไม่ต้องแก้

---

## 4. Contracts ข้ามโมดูล (งานของ system-architect — implement ตามนี้เท่านั้น)

### 4.1 `Catalog.Contracts.ILiveScheduleReader` — เพิ่ม default method + record (additive; implementer เดิม/fake ไม่พัง)

```csharp
public sealed record LiveSessionContext(
    Guid SessionId, Guid CourseId, string CourseTitle, string CourseSlug,
    string Title, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc,
    LiveSessionStatus Status, string? CancelReason, Guid? RecordingEpisodeId,
    Guid InstructorProfileId, Guid InstructorUserId, string InstructorDisplayName,
    bool GoogleAttendeeSyncEnabled);   // P11-03: คืน false ตายตัว · P11-04 ผูกคอลัมน์จริง

public sealed record LiveSessionContextPage(IReadOnlyList<LiveSessionContext> Items, int TotalCount);

// เพิ่มใน ILiveScheduleReader (ทุกตัวมี default body คืนค่าว่าง — Catalog `LiveScheduleReader` override จริง):
Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct);
Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsInWindowAsync(DateTime fromUtc, DateTime toUtc, bool includeCancelled, CancellationToken ct);   // StartsAtUtc ∈ [from, to)
Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsForCoursesAsync(IReadOnlyCollection<Guid> courseIds, DateTime? fromUtc, DateTime? toUtc, bool includeCancelled, CancellationToken ct);   // window = overlap: EndsAtUtc > fromUtc && StartsAtUtc < toUtc (null = ไม่จำกัดด้านนั้น)
Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(Guid instructorUserId, DateTime? fromUtc, DateTime? toUtc, bool includeCancelled, bool newestFirst, int skip, int take, CancellationToken ct);   // window = overlap เช่นเดียวกัน
```
**Window semantics:** `GetSessionContextsInWindowAsync` = `StartsAtUtc ∈ [from, to)` (ใช้กับ job invite/reminder) · `…ForCoursesAsync` และ `…InstructorSessionContextsAsync` = **ช่วงทับซ้อน** `EndsAtUtc > fromUtc && StartsAtUtc < toUtc` (คาบที่กำลังสอนอยู่ต้องติดมาด้วย) · sort/`take` clamp ≤200
Implementation (`LiveScheduleReader`): join `CourseLiveSessions()` → `Courses()` (global soft-delete filter ตัดคอร์สที่ลบแล้วเอง) → `InstructorProfiles()`; `.AsNoTracking()`; **projection เดียวต่อ query (ห้าม N+1)**; sort `StartsAtUtc` (+`Id` tiebreak); `take` ≤ 200 บังคับที่ reader (clamp); ไม่ expose `Domain.*` ออกนอกโมดูล

### 4.2 `Catalog.Contracts.ILiveMeetingReadinessReader` (ใหม่)
```csharp
public interface ILiveMeetingReadinessReader
{
    /// <summary>คืนเฉพาะ sessionId ที่ **ยังไม่มีห้องใช้ได้** (SESSION_MEETINGS ไม่มีแถว หรือ IsUsable=false). ว่าง = พร้อมทั้งหมด</summary>
    Task<IReadOnlyCollection<Guid>> GetSessionsWithoutUsableMeetingAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct);
}
```
- `Catalog.Infrastructure.NullLiveMeetingReadinessReader` คืน **ว่างเสมอ** (= ไม่ gate; ใช้เฉพาะ host ที่ไม่มี Live — production โหลด Live เสมอ)
- `Live.Infrastructure.LiveMeetingReadinessReader` — query `SESSION_MEETINGS` ด้วย id list ครั้งเดียว: ไม่มีแถว → ไม่พร้อม; มีแถว → `!IsUsable` ไม่พร้อม

### 4.3 gate ใน Catalog handler (กฎ publish "ทุกคาบอนาคตต้องมีห้องใช้ได้")
`SubmitCourseForReviewHandler` และ `ApproveCourseHandler` (inject `ILiveMeetingReadinessReader`): **หลัง** `CourseMediaReadiness.ValidateAsync` ผ่าน และ **ก่อน** `course.SubmitForReview(clock)`/`course.Publish(clock)`:
```
if course.DeliveryFormat != OnDemand:
   futureIds = course.LiveSessions.Where(Status==Scheduled && StartsAtUtc > clock.UtcNow).Select(Id)
   notReady  = await readiness.GetSessionsWithoutUsableMeetingAsync(futureIds)
   if notReady.Count > 0 → Result.Failure(DomainError.Validation($"คาบสอนสด {notReady.Count} คาบยังไม่มีลิงก์ห้องประชุมที่ใช้งานได้ …")
                                           .WithReason("live.meetings_not_ready", { sessionIds = notReady }))   // 400
```
- **ทำไมอยู่ที่ handler ไม่ใช่ `COURSE.Publish`:** สถานะห้องเป็นข้อมูลของ Live (คนละ aggregate/คนละโมดูล) — domain ห้ามรู้; handler เป็นจุดเดียวที่คอร์สกลายเป็น "ขายได้" (Draft→InReview→Published) · domain invariant เดิมของ P11-01 **ไม่แตะ**
- **ไม่ gate ที่ checkout/`OrderService`** (ตัดสินใจ): คอร์สที่ Published แล้วแต่ผู้สอนเพิ่งเพิ่มคาบใหม่/token Google หมดอายุ ไม่ควรหยุดขายทั้งคอร์ส — ปล่อยให้ join คืน 503 `live.meeting_not_ready` + alert ผู้สอน (P11-04 reminders: T−24h ถ้าห้องไม่พร้อม) + แสดงเตือนใน builder · **ถ้าเจ้าของต้องการ hard-block ที่ checkout ให้บอก — ออก revision**
- ข้อความเตือนเมื่อ gate ล้ม: FE ใช้ `reason`+`sessionIds` ชี้ไปการ์ดคาบใน builder
- unit test handler: Live ไม่มีห้อง → 400 พร้อม reason · มีครบ → ผ่าน · OnDemand → ไม่เรียก reader เลย · คาบอดีต/Cancelled ไม่นับ

### 4.4 `Catalog.Contracts.ILiveMeetingSink` — **ไม่เปลี่ยน signature**
semantics ที่ `LiveMeetingSink` ต้องทำ (ห้าม `SaveChangesAsync` เด็ดขาด — คนเรียกเป็นเจ้าของ transaction; ห้าม query หา session/instructor):
- `OnSessionScheduledAsync(id)`: ถ้ายังไม่มีแถว (`GetBySessionIdAsync`) → `repository.Add(SESSION_MEETING.Stage(id))`
- `OnSessionChangedAsync(id)`: โหลดแถว (tracked) → `MarkSessionChanged()`; **ไม่มีแถว → Stage ใหม่** (คาบที่สร้างก่อนมี Live)
- `OnSessionCancelledAsync(id)`: โหลดแถว → `MarkSessionCancelled()`; ไม่มีแถว → ไม่ทำอะไร
- unit test: ไม่เรียก `SaveChanges` · แถวซ้ำ (Scheduled สองครั้ง) ไม่เกิด · integration: สร้างคาบผ่าน `CreateLiveSessionHandler` จริง → มี `SESSION_MEETINGS` Pending ใน SaveChanges เดียว; คาบชนกัน (domain throw) → **ไม่มี** meeting row

### 4.5 SharedKernel — `DomainError` (additive)
```csharp
public sealed record DomainError(string Code, string Message)
{
    public string? Reason { get; init; }                                  // sub-code เสถียรให้ FE ("live.window_not_open")
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; } // ข้อมูลประกอบ (opensAtUtc, sessionIds…)
    public static DomainError Unavailable(string message) => new("unavailable", message);   // → 503
    public DomainError WithReason(string reason, IReadOnlyDictionary<string, object?>? extensions = null) => this with { Reason = reason, Extensions = extensions };
}
```
`DomainErrorHttpResults.ToProblemHttpResult`: เพิ่ม `"unavailable" => 503`; ใส่ extension `reason` และกระจาย `Extensions` (key เป็น camelCase ตามที่ผู้เรียกส่งมา) ลง ProblemDetails; `errorCode` เดิมคงไว้ · unit test: map 503, extension ออกครบ, error เดิมที่ไม่มี Reason ผลลัพธ์ไม่เปลี่ยน (regression ทุก `ToProblemHttpResult` เดิม)

---

## 5. `Siri.Integrations.Google` — interface ที่ FROZEN

```csharp
namespace Siri.Integrations.Google;

public sealed record GoogleTokenSet(string AccessToken, DateTime ExpiresAtUtc, string? RefreshToken, string GrantedScopes);
public sealed record GoogleUserInfo(string Subject, string Email, bool EmailVerified);

public interface IGoogleOAuthService
{
    bool IsConfigured { get; }                                              // ClientId != "" (หรือ Logging mode = true)
    string BuildAuthorizationUrl(string state, string codeChallenge);       // PKCE S256; access_type=offline; prompt=consent; include_granted_scopes=false
    Task<Result<GoogleTokenSet>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);
    Task<Result<GoogleTokenSet>> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct);
    Task<Result<GoogleUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken ct);
    Task<Result> RevokeAsync(string token, CancellationToken ct);           // best-effort — คืน Success เสมอถ้า Google ตอบ 200/400
}

public sealed record CalendarEventRequest(
    string Summary, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc,
    string RequestId, string PrivateSessionId);                              // RequestId = meetingId:N (idempotent conferenceData)
public sealed record CalendarEventResult(string EventId, string? MeetUrl, bool ConferencePending);

public interface ICalendarProvider
{
    Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(string accessToken, CalendarEventRequest request, CancellationToken ct);
    Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(string accessToken, string privateSessionId, CancellationToken ct); // กัน event ซ้ำเมื่อ retry
    Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct);
    Task<Result<CalendarEventResult>> UpdateEventAsync(string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct);
    Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);                    // 404/410 = Success
    Task<Result> SetAttendeesAsync(string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct); // P11-04
}
```
**ทุกเมธอดรับ `accessToken` ต่อ call** (Q10=B — ไม่มี credential กลาง) · `Result` + `DomainError.Code`:

| code | เงื่อนไข | Live ตอบสนอง |
|---|---|---|
| `google.unauthorized` | token endpoint `invalid_grant`/`invalid_client`; Calendar `401`; `403` reason `insufficientPermissions`/`forbidden` ที่เป็นปัญหา scope | account `MarkRevoked(invalid_grant\|insufficient_scope)` → meeting `NeedsReconnect` + แจ้งผู้สอน |
| `google.not_found` | Calendar `404`/`410` | patch → สร้าง event ใหม่ (URL ใหม่; join URL ของแพลตฟอร์มคงเดิม) · delete → ถือว่าสำเร็จ |
| `google.rate_limited` | `429`, `403` reason `rateLimitExceeded`/`userRateLimitExceeded` | transient → `RecordAttemptFailed` |
| `google.transient` | `5xx`, timeout, `HttpRequestException` | transient |
| `google.bad_request` | `400` อื่น ๆ (ข้อมูลผิดรูป) | `RecordAttemptFailed` (ไม่หายเองแต่ให้ครบ 5 ครั้งแล้ว Failed — ผู้สอนวางลิงก์เองได้) |
| `google.not_configured` | `IsConfigured=false` | connect → 503 `live.google_not_configured` |

HTTP จริง (ยืนยันจากเอกสาร Google 2026-10-06): auth `https://accounts.google.com/o/oauth2/v2/auth` · token+refresh `POST https://oauth2.googleapis.com/token` (form: `code|refresh_token`, `client_id`, `client_secret`, `redirect_uri`, `grant_type`, `code_verifier`) · revoke `POST https://oauth2.googleapis.com/revoke` (`token=`) · userinfo `GET https://openidconnect.googleapis.com/v1/userinfo` · Calendar `https://www.googleapis.com/calendar/v3/calendars/primary/events` — insert: `POST …?conferenceDataVersion=1&sendUpdates=none` body `{summary, description, start:{dateTime,timeZone:"Asia/Bangkok"}, end:{…}, conferenceData:{createRequest:{requestId, conferenceSolutionKey:{type:"hangoutsMeet"}}}, guestsCanModify:false, guestsCanInviteOthers:false, guestsCanSeeOtherGuests:false, anyoneCanAddSelf:false, extendedProperties:{private:{siriSessionId}}}` · ผลอาจ `conferenceData.createRequest.status.statusCode = "pending"` ทันที (ยังไม่มี `hangoutLink`) → `ConferencePending=true`; พร้อมแล้วอ่านจาก `hangoutLink` (หรือ `conferenceData.entryPoints[type=video].uri`) · find: `GET …?privateExtendedProperty=siriSessionId={id}&maxResults=1&showDeleted=false` · update: `PATCH …/{eventId}?conferenceDataVersion=1&sendUpdates=none` · delete: `DELETE …/{eventId}?sendUpdates=none` · attendees: `GET` event → `PATCH` ด้วย `attendees:[{email}]` ทั้งชุด `sendUpdates=none`
- **`MeetUrl` ที่ได้จาก Google ต้องผ่าน allow-list host เดียวกับ Manual** ก่อนเก็บ (defense in depth)
- DTO ภายใน `internal`, System.Text.Json, `JsonSerializerOptions` camelCase, timeout จาก `HttpTimeoutSeconds`, `IHttpClientFactory` named client `google-oauth` / `google-calendar` · **ห้าม log**: token, refresh token, code, อีเมลผู้เรียน, URL ห้องประชุม (log แค่ sessionId/eventId/status code)
- `LoggingGoogleOAuthService`/`LoggingCalendarProvider` (เฉพาะ `Live:Provider=Logging`): `BuildAuthorizationUrl` คืน `{RedirectUri}?code=dev&state=…` (callback ถูกยิงทันที), `ExchangeCode/Refresh` คืน token ปลอมที่ระบุตัวชัด (`dev-…`) และ `userinfo` = `dev-instructor@example.test`, `CreateEvent` คืน `https://meet.invalid/dev/{requestId}` — **ปิดด้วย ProductionConfigurationGuard** และห้ามเป็น default

---

## 6. Services & Job (พฤติกรรมที่ต้องตรง)

### 6.1 `InstructorGoogleAccountService` (aggregate: INSTRUCTOR_GOOGLE_ACCOUNT)
- `GetStatusAsync(userId)` → `GoogleConnectionStatusResponse` (§7 แถว 1) · `configured = oauth.IsConfigured` · `affectedSessionCount` = จำนวนคาบอนาคตของผู้สอน (`ILiveScheduleReader.GetInstructorSessionContextsAsync(userId, from: now, …)`) ที่ meeting `NeedsReconnect|AwaitingLink` และไม่มี URL
- `BeginConnectAsync(userId, returnPath)`: `!IsConfigured` → `Unavailable.WithReason("live.google_not_configured")` · validate `returnPath` (`^/instructor/[A-Za-z0-9/_\-]*$`, ไม่มี `//`/`\`/`..`, ≤200) ไม่ผ่าน → `PostConnectRedirectPath` default · สร้าง `state` (32 ไบต์ random base64url) + `codeVerifier` (64 ตัว unreserved) → store `{userId, codeVerifier, returnPath}` Redis **TTL 10 นาที** key `live:google:oauth:{sha256hex(state)}` · Redis ล่ม → **fail-closed** `Unavailable` · คืน `authorizationUrl`
- `CompleteConnectAsync(code, state)` (callback): consume state (§6.1) → ไม่พบ/หมดอายุ → `state_invalid` · `ExchangeCodeAsync` → ตรวจ `GrantedScopes` มี calendar scope ไม่งั้น `scope_missing` (ไม่เก็บอะไร + best-effort revoke) · ไม่มี `RefreshToken` → `no_refresh_token` · `GetUserInfoAsync` → email/sub · upsert (ถ้ามีแถวเดิม: revoke token เก่า best-effort แล้ว `Reconnect`) ด้วย `Encrypt(refreshToken)` · **หลังสำเร็จ:** reset meeting ของผู้สอนคนนี้ที่ `SYNC_STATUS IN (AwaitingLink, NeedsReconnect, Failed) AND MEET_URL_ENCRYPTED IS NULL` → `RequestResync()` (repository query ตาม `INSTRUCTOR_USER_ID`) · ผลลัพธ์คืน `(redirectPath, outcome)` ให้ controller redirect
- `DisconnectAsync(userId)`: ไม่มีแถว/ไม่ active → 204 (idempotent) · มี → `oauth.RevokeAsync(decrypt(token))` (ผลไม่กระทบ) → `MarkRevoked("user_disconnected")` (ล้าง token) · meeting ที่ `Synced` มี URL **คงไว้ใช้ได้** (แก้/ลบ event ผ่านระบบไม่ได้จนกว่าเชื่อมใหม่ → job จะตั้ง `NeedsReconnect`)
- `TryGetAccessTokenAsync(Guid instructorUserId, CancellationToken)` (**public** — P11-04 ใช้ด้วย): คืน `Result<string accessToken>`; ไม่มี account active → failure `live.google_not_connected`; `Decrypt` → `RefreshAccessTokenAsync` → สำเร็จ `MarkValidated`; `google.unauthorized` → `MarkRevoked("invalid_grant")` + `IInstructorAlertSender.GoogleReconnectNeeded(...)` **ครั้งเดียวต่อการ revoke** (ส่งเฉพาะตอนเปลี่ยนจาก active → revoked) · cache access token ใน dictionary ของ **run เดียว** (ไม่เก็บถาวร ไม่ใส่ Redis)

### 6.2 `SessionMeetingService` (aggregate: SESSION_MEETING)
- `GetCourseMeetingSummariesAsync(courseId, userId)`: ownership ผ่าน `ICatalogPriceContract.IsInstructorOwnerOfCourseAsync` (ไม่ผ่าน → `NotFound`) → session ids ของคอร์ส (`ILiveScheduleReader.GetSessionsForCourseAsync`) → meeting rows → `InstructorMeetingSummary[]` (**ไม่มี URL**)
- `SetManualLinkAsync(userId, sessionId, url)`: context ผ่าน `ILiveScheduleReader.GetSessionContextsAsync([sessionId])` → null → 404; `InstructorUserId != userId` → 403 · คาบต้อง `Scheduled` และ `EndsAtUtc > now` ไม่งั้น 409 `live.session_not_editable` · `MeetingLinkValidator.Validate(url)` (§6.4) · `SetManualLink(Encrypt(normalizedUrl))` · ไม่มีแถว → Stage ก่อนแล้วตั้ง · SaveChanges · log `sessionId`+`host` เท่านั้น
- `ResyncAsync(userId, sessionId)`: ownership เหมือนกัน · `Synced`/`PendingDelete`/`Deleted` → 409 `live.meeting_not_resyncable` · อื่น ๆ → `RequestResync()`
- `RevealUrl(SESSION_MEETING)` (ใช้โดย P11-05 เท่านั้น): `Decrypt(MEET_URL_ENCRYPTED)` — **ห้าม log ค่า**

### 6.3 `LiveMeetingSyncJob` (Hangfire recurring `live-meeting-sync`, ทุก 1 นาที, `[DisableConcurrentExecution(timeoutInSeconds: 50)]`, ลงทะเบียนเฉพาะ `Siri.Workers`)
```
RunAsync(ct):
  now = clock.UtcNow
  0. AdoptOrphans: sessions = ILiveScheduleReader.GetSessionContextsInWindowAsync(now, now+365d, includeCancelled:false)
       missing = ids ที่ไม่มีแถว SESSION_MEETINGS → Add(Stage(id)) (ไม่เกิน 200/run) → SaveChanges
  1. due = SESSION_MEETINGS WHERE SYNC_STATUS IN (Pending,PendingDelete) AND (NEXT_RETRY_AT_UTC IS NULL OR <= now)
           ORDER BY (UpdatedAtUtc/CreatedAtUtc) TAKE 50
     ctx = ILiveScheduleReader.GetSessionContextsAsync(due.SessionId)            // รวม Cancelled
     tokens = new Dictionary<instructorUserId, AccessToken?>()                    // cache ภายใน run
  2. foreach m in due (แต่ละตัว try/catch + SaveChanges แยก + ChangeTracker.Clear() ถ้า exception; DbUpdateConcurrencyException → ข้าม รอบหน้า):
       Process(m, ctx[m.SessionId])
```
`Process(m, c)` (ตาราง — ลำดับสำคัญ):

| เงื่อนไข | การกระทำ |
|---|---|
| `c == null` (คาบถูกลบ/หาไม่เจอ) | `Deleted` |
| `m.INSTRUCTOR_USER_ID == null` | ตั้งจาก `c.InstructorUserId` |
| `c.Status == Cancelled` และ `m` ยังไม่ `PendingDelete/Deleted` | `m.MarkSessionCancelled()` แล้วทำต่อตามสถานะใหม่ |
| **`PendingDelete`** | มี event: ถ้าผู้สอนมี account active → refresh → `DeleteEventAsync` (404/410 = สำเร็จ) ไม่มี account/`unauthorized` → ข้ามการลบ + `ERROR="orphan_event"` · แล้ว `FinishDelete(c.Status==Scheduled && c.EndsAtUtc>now)` |
| **`Pending`** และ `c.EndsAtUtc <= now` | ไม่สร้างอะไรให้คาบที่จบแล้ว: มี URL → `Synced` ไม่มี → `Deleted` |
| **`Pending`**, `Live:Provider=Logging` | `CreateEvent` ผ่าน Logging provider → `RecordGoogleSynced`(provider=`Logging`) |
| **`Pending`**, ไม่มี account active (หรือ `ManualOnly`) | มี event เดิม → `RecordNeedsReconnect("google_account_unavailable")` · มี URL (manual) → `Synced` · ไม่มีทั้งคู่ → `ResolveAsAwaitingLink()` + `IInstructorAlertSender.MeetingNeedsLink(...)` ครั้งเดียว (กันซ้ำด้วย `MEETING_ALERT_SENT_AT_UTC`) |
| **`Pending`**, มี account active | `AssignProvider(GoogleMeet, account.Id)` · `TryGetAccessToken` → `google.unauthorized` → `RecordNeedsReconnect("invalid_grant")` (+แจ้งเตือนตาม §6.1) · transient → `RecordAttemptFailed(code)` · สำเร็จแล้ว: ถ้า `PROVIDER_EVENT_ID==null` → (`ATTEMPTS>0` ⇒ `FindEventByPrivateSessionIdAsync` ก่อน; พบ ⇒ adopt) ไม่งั้น `CreateEventWithMeetAsync` · ถ้ามี event → `UpdateEventAsync` (404/410 ⇒ ล้าง `PROVIDER_EVENT_ID` แล้วสร้างใหม่ใน run เดียวกัน) · ถ้า `ConferencePending` ⇒ `GetEventAsync` ซ้ำสูงสุด **3 ครั้ง ห่างกัน 1 วิ** แล้วถ้ายังไม่พร้อม `RecordAttemptFailed("conference_pending")` · ได้ `MeetUrl` ที่ผ่าน allow-list ⇒ `RecordGoogleSynced` |
| เมื่อ `Failed` ครบ 5 ครั้ง | `IInstructorAlertSender.MeetingFailed(...)` ครั้งเดียว |

event ที่ส่งไป Google: `Summary = "{CourseTitle} — {SessionTitle}"` · `Description = "เข้าห้องเรียนผ่านแพลตฟอร์ม: {PublicBaseUrl}/live/{sessionId}/join" + "\n\n" + plain(Description)` (**ไม่มีรายชื่อ/อีเมลผู้เรียน**; guest เห็นลิงก์แพลตฟอร์มเท่านั้น) · เวลา UTC พร้อม `timeZone:"Asia/Bangkok"` · `PrivateSessionId = sessionId:N`

### 6.3b `IInstructorAlertSender` (อีเมลแจ้งผู้สอน — P11-03 ส่งอีเมลอย่างเดียว; in-app เติมใน P11-04)
3 เมธอด `GoogleReconnectNeeded(instructorUserId, affectedCount)` · `MeetingNeedsLink(instructorUserId, sessionId, sessionTitle, courseTitle)` · `MeetingFailed(instructorUserId, sessionId, sessionTitle, courseTitle)` — ดึงอีเมลผู้สอนจาก `IUserContactReader.GetEmailAsync`, สร้างเนื้อหาด้วย `EmailTemplateRenderer.RenderLayout` (ไทย, **HtmlEncode ทุกค่าที่มาจาก DB**) เรียก `IEmailOutbox.Enqueue` (stage ไม่ SaveChanges — job เป็นคน SaveChanges) · subject: `เชื่อม Google Calendar ใหม่เพื่อให้ห้อง Meet ทำงานต่อ` / `วางลิงก์ห้องประชุมสำหรับคาบ {คาบ}` / `สร้างห้อง Google Meet ไม่สำเร็จ — {คาบ}` · templateKey `live-google-reconnect` / `live-meeting-needs-link` / `live-meeting-failed` · ลิงก์ในเนื้อหา = `{PublicBaseUrl}/instructor/live-settings` หรือ `/instructor/sessions/{sid}` เท่านั้น (ไม่มี meetUrl)

### 6.4 `MeetingLinkValidator` (กัน SSRF/XSS/open-redirect — เป็นแหล่งเดียวของกฎ)
`Validate(string input) → Result<string normalizedUrl>`: trim · ความยาว 1–500 · ไม่มี whitespace/control char/ช่องว่างตรงกลาง · `Uri.TryCreate(Absolute)` · **scheme == `https` เท่านั้น** (ปฏิเสธ `http/javascript/data/file/…`) · **ไม่มี userinfo** (`user:pass@`) · port ต้องเป็น default (443) · host: IDN→ASCII lowercase แล้วตรง `Live:AllowedMeetingHosts` (exact **หรือ** `.{entry}` suffix; ไม่ใช่ substring/endsWith เปล่า → `evilzoom.us` ต้องไม่ผ่าน) · ตัด fragment · คืน `uri.GetComponents(AbsoluteUri, UriFormat.UriEscaped)` · error → `DomainError.Validation(...).WithReason("live.meeting_link_invalid" | "live.meeting_link_host_not_allowed")`
unit test: allow-list ครบ/ไม่ครบ, `https://meet.google.com.evil.com/x`, `https://evil.com/?https://meet.google.com`, `https://meet.google.com@evil.com/`, `javascript:alert(1)`, `HTTPS://MEET.GOOGLE.COM/abc`, IDN/punycode, port 8443, ยาว 501, มีช่องว่าง

---

## 7. Endpoint ที่ FROZEN (รายละเอียด JSON/TypeScript → `P11-FE-live-dto-appendix.md` §A)

`InstructorMeetingSummary`/`CourseMeetingsResponse`/`GoogleConnectionStatusResponse` และ **การ derive `needsAction` อยู่ที่ `P11-FE-live-dto-appendix.md` §A.1/§A.5 — BE ต้องทำตามเป๊ะ**

ทุก endpoint: **`Cache-Control: no-store`** ตอบกลับ · `[ProducesResponseType]` + `[EndpointName]` + `[EndpointSummary]` ครบ · error = RFC 9457 + `traceId` + `errorCode` + `reason`

| # | Method + path | Auth | Rate limit | สำเร็จ | Error |
|---|---|---|---|---|---|
| 1 | `GET /api/live/instructor/google/status` | `InstructorOnly` | `live-user` | 200 `GoogleConnectionStatusResponse` | 401 |
| 2 | `POST /api/live/instructor/google/connect` body `{returnPath?}` | `InstructorOnly` | `live-user` | 200 `{authorizationUrl}` | 503 `live.google_not_configured` / Redis ล่ม |
| 3 | `GET /api/live/instructor/google/callback?code&state&error` | **`[AllowAnonymous]`** (ตรวจ `state` แทน) | `live-google-callback` | **302** → `{PublicBaseUrl}{returnPath}?google=connected` | ผิดทุกกรณี **302** → `?google=error&reason=<access_denied\|state_invalid\|scope_missing\|no_refresh_token\|exchange_failed>` (ห้าม 4xx/JSON — เป็น navigation ของ browser; ห้ามใส่ code/token/อีเมลใน URL) |
| 4 | `DELETE /api/live/instructor/google` | `InstructorOnly` | `live-user` | 204 (idempotent) | 401 |
| 5 | `GET /api/live/instructor/courses/{courseId}/meetings` | `InstructorOnly` + เจ้าของคอร์ส | `live-user` | 200 `{items: InstructorMeetingSummary[]}` | 404 (ไม่ใช่เจ้าของ/ไม่พบ — รวมเป็น 404 เพราะ contract คอร์สไม่แยก 2 กรณี) |
| 6 | `PUT /api/live/instructor/sessions/{sessionId}/meeting-link` body `{meetUrl}` | `InstructorOnly` + เจ้าของคาบ | `live-user` | 200 `InstructorMeetingSummary` | 400 `live.meeting_link_invalid`/`…host_not_allowed` · 403 ไม่ใช่เจ้าของ · 404 · 409 `live.session_not_editable` |
| 7 | `POST /api/live/instructor/sessions/{sessionId}/meeting/resync` | `InstructorOnly` + เจ้าของคาบ | `live-user` | 200 `InstructorMeetingSummary` | 403/404 · 409 `live.meeting_not_resyncable` |

กฎร่วม: ownership ทุกตัวตัดสินจาก `IUserContext.UserId` เท่านั้น (ห้ามรับ userId จาก body/query) · Admin **ไม่** bypass ownership (เหมือน P1-04) · ไม่มี endpoint ไหนคืน `meetUrl` ใน P11-03 (มีแค่ `hasMeetingLink`) — URL อยู่ที่ P11-05 `join` และ `instructor/sessions/{sid}` เท่านั้น

---

## 8. Test checklist (Unit + Integration — Testcontainers PostgreSQL+Redis, ห้าม InMemory)

**Unit:** entity state machine ทุก transition (§2.3 รวม backoff 1/5/15/60 → Failed ครั้งที่ 5) · `MeetingLinkValidator` (§6.4) · `LiveMeetingSyncJob` ทุกแถวของตาราง §6.3 ด้วย `FakeCalendarProvider`/`FakeGoogleOAuthService` + `FakeClock` (Manual→AwaitingLink · Google สำเร็จ · conference pending→3 poll→Failed ครั้งที่ n · `unauthorized`→NeedsReconnect+แจ้งครั้งเดียว · 404 patch→สร้างใหม่ · PendingDelete 404 ok · คาบจบแล้ว) · `GoogleOAuthService`/`GoogleCalendarProvider` ด้วย `HttpMessageHandler` stub (body/query ที่ส่งตรง §5, mapping error code ทุกแถว, **ไม่มี token ใน log**) · `GoogleOAuthOptions` validator · `DomainError` mapper · readiness reader · sink (ไม่ SaveChanges)
**Integration:** (ใช้ `SiriApiFactory` — controller เป็น MVC; **ไม่ต้องเขียน minimal mapper คู่** สำหรับ Live — ตัดสินใจ P11-30: Live ใช้ `WebApplicationFactory` เท่านั้น) · สร้างคาบ→meeting Pending ใน SaveChanges เดียว · publish gate: Live course + คาบอนาคตไม่มี URL → 400 `live.meetings_not_ready`; หลัง `PUT meeting-link` ที่ถูกต้อง → Submit ผ่าน; Approve gate เช่นกัน · ownership: ผู้สอน B เรียก `PUT meeting-link`/`resync`/`meetings` ของผู้สอน A → 403/404 · OAuth: `connect` สร้าง state; `callback` ด้วย state ผิด/หมดอายุ/ใช้ซ้ำ → 302 `state_invalid` (state ใช้ได้ครั้งเดียวจริง); connect→callback ด้วย `FakeGoogleOAuthService` → แถวบัญชีมี `REFRESH_TOKEN_ENCRYPTED` ที่ **ไม่เท่า** plaintext และ decrypt ได้ · `disconnect` ล้าง token · `callback` ที่ได้ scope ไม่ครบ → `scope_missing` ไม่เก็บ · rate-limit `live-user`/`live-google-callback` · **ไม่มี `meetUrl` ใน response ของ endpoint ใดใน P11-03 + public course detail/search** (recursive JSON walk แบบ `LiveSessionReadModelSecurityTests`)
**Security-critical:** `MEET_URL_ENCRYPTED`/`REFRESH_TOKEN_ENCRYPTED` ใน DB ไม่ใช่ plaintext (assert ด้วย raw SQL) · log ไม่มี token/URL (capture logger sink แล้วค้น)

---

## 9. Residual risk & follow-up (บันทึกอย่างตรงไปตรงมา)

1. **ลิงก์ Meet ที่ถูกเปิดเผยแล้วส่งต่อได้** — join gate (P11-05) ป้องกันเฉพาะ "ใครได้ลิงก์จากแพลตฟอร์ม" ผู้เรียนที่ผ่านด่านแล้วยังส่งลิงก์ต่อได้ 1 ต่อ · mitigation ที่ทำใน v1: (ก) ไม่มี raw URL ในอีเมล/ICS/response สาธารณะเลย (ข) join log รู้ว่าใครขอลิงก์ (ค) **Google attendee sync เป็น opt-in ต่อคอร์ส (Q11, P11-04)** — ผู้เรียนที่ถูกเชิญเข้าห้องได้ทันที คนที่ได้ลิงก์ต่อต้อง "ขอเข้า" ให้ผู้สอนตัดสิน (ง) คำแนะนำผู้สอนให้ตั้ง host admit ในหน้า builder
2. **Testing-mode ของ OAuth app → refresh token หมดอายุใน 7 วัน** (เอกสาร Google) — ผู้สอนต้องเชื่อมใหม่ทุกสัปดาห์จนกว่าเจ้าของจะ publish แอปเป็น In production (+verification เพราะ scope sensitive) — ระบบรับมือด้วย `NeedsReconnect` + แจ้งเตือน + Manual fallback แต่เป็นความรำคาญจริง
3. **Follow-up ที่ไม่อยู่ใน v1:** Meet REST API `spaces.patch config.accessType=RESTRICTED` (ต้องตรวจ scope `meetings.space.*` และว่าบัญชี Gmail ธรรมดารองรับ) → เปิดเป็น task ใหม่ (`P11-14`) หลังเจ้าของมี OAuth client จริงให้ทดสอบ
4. IP ใน join log = `RemoteIpAddress` (อาจเป็น IP proxy เพราะไม่มี `UseForwardedHeaders` ใน `Program.cs` — gap เดิมของ `PLAYBACK_SESSIONS`)

---

## 10. Work packages (ลำดับบังคับ)

| WP | ใคร | ทำอะไร | ไฟล์หลัก | Acceptance |
|---|---|---|---|---|
| **A** | `DATABASE` | สร้าง project `Siri.Modules.Live` (csproj+`LiveModule.cs` ว่าง) + entity/enum/configuration ของ 2 ตาราง ตาม §2 + `AppDbContextLiveExtensions` + migration **`AddLiveMeetings`** (อ่านทั้งไฟล์ก่อนส่ง — ต้อง additive ล้วน, ไม่มี shadow column/FK, FK `NoAction`, ชื่อ ≤63) + อัปเดต `docs/DATABASE.md` (แทน sketch เดิมของ LIVE.*; ระบุ deviation §0) + `ModuleAssemblyCatalog` + แก้ `Assert.Equal(10→11)` + sln/csproj refs (§3.3 ข้อ 1,3) | `src/Siri.Modules.Live/Domain/*`, `Infrastructure/*Configuration.cs`, `src/Siri.Persistence/Migrations/*AddLiveMeetings*` | build 0 warning · architecture tests เขียว (รวม identifier length) · migration ไม่ถูก apply ขึ้น DB จริง |
| **B** | `backend-developer` | `Siri.Integrations.Google` ทั้งโปรเจ็ค (§5) + `GoogleOAuthOptions` + unit test ด้วย HttpMessageHandler stub + Logging providers | `src/Siri.Integrations.Google/**`, `tests/Siri.UnitTests/Google/**` | unit เขียว · ไม่มี NuGet ใหม่ (`git diff Directory.Packages.props` ว่าง) · ไม่ log secret |
| **C** | `backend-developer` | SharedKernel `DomainError` (§4.5) · Catalog: contracts §4.1–4.2, `LiveScheduleReader` ขยาย, `NullLiveMeetingReadinessReader`, `TryAdd*`, gate ใน 2 handler (+ แก้ ctor ที่เทสต์เดิมเรียก) | `Siri.SharedKernel/DomainError*.cs`, `Siri.Modules.Catalog/Contracts/*`, `Infrastructure/LiveScheduleReader.cs`, `Features/{SubmitCourseForReview,ApproveCourse}/Handler.cs`, `CatalogModule.cs` | unit+เทสต์เดิมของ handler/DomainError ผ่านโดยไม่แก้ assertion เดิม |
| **D** | `backend-developer` | Live: repositories, services (§6.1–6.2, 6.4), `LiveMeetingSink`, `LiveMeetingReadinessReader`, `RedisGoogleOAuthStateStore`, `LiveOptions`+validator, `AddLiveModule` | `src/Siri.Modules.Live/{Application,Infrastructure}/**` | unit ครบ §8 ส่วน service/validator/sink |
| **E** | `backend-developer` | `LiveMeetingSyncJob` (§6.3) + `RecurringJobsRegistration` + Workers/Api `AddLiveModule` + rate-limit policies (§3.5) + `ProductionConfigurationGuard` + `appsettings.json` placeholders | `Infrastructure/LiveMeetingSyncJob.cs`, `Siri.Workers/*`, `Siri.Api/{Program.cs,Configuration/*}` | unit job state machine ครบทุกแถว |
| **F** | `backend-developer` | 2 controller + validators ลงทะเบียน + integration tests §8 | `Siri.Api/Controllers/Live/*`, `tests/Siri.IntegrationTests/Live*` | integration เขียนครบ (เครื่อง dev ไม่มี Docker → รายงานว่ายังไม่เคยรัน) |

WP A ต้องเสร็จก่อน D/E; B ขนานกับ A ได้; C ขนานกับ A/B ได้; F หลัง D+E · **commit แยกตาม WP ตามที่ user สั่งเท่านั้น (ห้าม commit เอง)** · `DONE` ตั้งโดย integrator-qa

---

## 11. สิ่งที่เจ้าของโปรเจ็คต้องทำเอง vs สิ่งที่ใช้ได้โดยไม่ต้องทำ

**ใช้ได้ทันทีโดยไม่ต้องมี Google เลย:** ผู้สอนสร้างคาบ → meeting `AwaitingLink` → วางลิงก์ Meet/Zoom/Teams เอง (`PUT meeting-link`) → ผ่าน publish gate → ขาย → invite/ICS/reminder/join gate ทำงานครบ (ทั้งหมดใช้ลิงก์แพลตฟอร์ม) — ตั้ง `Live:Provider=ManualOnly` หรือเว้น `Integrations:Google:ClientId` ว่าง
**ต้องทำเองเพื่อให้ "สร้าง Meet อัตโนมัติ" ใช้งานได้:**
1. Google Cloud Console → OAuth consent screen (External) + เปิด **Google Calendar API** · scope: `openid`, `email`, `…/auth/calendar.events.owned` (ถ้า Google ไม่รับ ให้เปลี่ยนเป็น `calendar.events` ใน config `Integrations:Google:Scopes` — ไม่ต้องแก้โค้ด) · เพิ่ม **test users** (≤100) ระหว่างโหมด Testing
2. สร้าง/ใช้ **OAuth client แบบ Web application** (ใช้ client เดิมของ Sign-in ได้ — ต้องเอา **client secret** มาด้วย) → **Authorized redirect URIs**: prod `https://<API origin>/api/live/instructor/google/callback`, dev `http://localhost:5190/api/live/instructor/google/callback` (และ `http://localhost:4202/api/...` ถ้าผ่าน proxy)
3. ตั้งค่า (ทั้ง API และ Workers): `Integrations__Google__ClientId`, `…__ClientSecret`, `…__RedirectUri` ผ่าน user-secrets/`.env`/env ของ container
4. **submit verification** (scope sensitive) และ **publish เป็น In production** — ไม่ทำ = refresh token หมดอายุทุก 7 วัน และใช้ได้เฉพาะ test user
5. apply migration `AddLiveMeetings` (และตัวถัดไปของ P11-04/05) ขึ้น DB จริง — **agent ห้าม apply เอง**
6. แจ้งผู้สอนว่าเพดานผู้เข้าร่วม/ระยะเวลา Meet ขึ้นกับแผน Google ของบัญชีตัวเอง (Gmail ฟรี ~100 คน/60 นาที) — builder แสดงคำเตือนคงที่

---

## Changelog
(ยังไม่มี revision หลัง FROZEN)
