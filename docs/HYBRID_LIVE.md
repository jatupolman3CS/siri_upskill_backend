# Hybrid Live Design — Google Meet + Bunny VOD + Calendar + Catch-up + AI Study

> **สถานะ:** DESIGN (2026-09-16) · ต้นทาง: `docs/external-specs/SIRI-UPSKILL-Hybrid-Live-Platform-Spec-2026-09-16.md`
> · การ reconcile กับ decision เดิมอยู่ที่ `docs/DECISIONS.md` **D-21** · task อยู่ที่ `docs/TASKS.md` **§P11 / §P12**
> · เอกสารนี้คือ "แบบ" ระดับระบบ — contract ต่อ task (FROZEN) จะออกทีละใบที่ `docs/contracts/P11-xx-*.md` เมื่อคำถาม Q10–Q13 ปิด

## 0. หลักการออกแบบ — ต่อยอด ไม่รื้อ

สเปคใหม่ ~60% มีของจริงอยู่แล้วในโค้ด (ตรวจจาก method body 2026-09-16 ไม่ใช่ doc comment):

| สเปคขอ | สถานะจริงในโค้ด | ทำอะไร |
|---|---|---|
| TUS direct upload → Bunny (`@tus/tus-client`) | ✅ `tus-js-client` + `MediaApiService.uploadVideoFile` + `BunnyVideoProvider.GetUploadUrlAsync` (`src/Siri.Integrations.Video/Bunny/BunnyVideoProvider.cs:88-118`) | **ใช้ซ้ำ** ไม่แตะ |
| Bunny webhook → update video id/status | ✅ `BunnyWebhookHandler` HMAC-SHA256 raw bytes + idempotent | ใช้ซ้ำ · เพิ่ม hook ไป AI pipeline (P12) |
| Signed URL SHA256 HMAC + domain restricted | ✅ `GenerateSignedPlaybackUrl` (dir-scoped token, TTL 5 นาที) + `PlaybackSessionService` ตรวจ enrollment | ใช้ซ้ำ · **บั๊กเดิม X-28** FE ไม่ต่ออายุ URL ก่อนหมด 5 นาที ต้องแก้ก่อน VOD ยาว ๆ |
| Resume playback + heartbeat 15 วิ | ✅ `EPISODE_PROGRESS.LAST_POSITION_SECONDS` + `PUT /api/learning/enrollments/{id}/episode-progress/{episodeId}` + `video-player.ts:213` (15000 ms) | ใช้ซ้ำ · `VideoProgress(UserId, SessionId, LastWatchedSecond)` ในสเปค = ตารางนี้ |
| Stripe checkout + webhook + auto-grant | ✅ PaymentIntent + `STRIPE_WEBHOOK_EVENTS` unique + `EnrollUserInCoursesAsync` ใน transaction เดียว | ใช้ซ้ำ · **ไม่เปลี่ยนเป็น Checkout Session** (D-21) · เปิดบัตรผ่าน PaymentIntent เดิม |
| `Enrollments.StripeSessionId` | ✅ `ENROLLMENT.ORDER_ID` → `PAYMENT.PROVIDER_PAYMENT_INTENT_ID` | ไม่เพิ่มคอลัมน์ |
| Instructor dashboard / Admin RBAC / Billing audit / Audit log | ✅ `/api/analytics/instructor/dashboard/summary`, `/api/identity/admin/*`, `/api/commerce/admin/payment-ops`, `/api/identity/admin/audit-logs` | ต่อสายที่ยัง hardcode (P4-24) + เพิ่มของที่ขาด (webhook viewer, Bunny quota) |
| **Live session / ตารางสอน / Google Meet / Calendar / ICS** | ❌ ไม่มีเลย (grep ทั้ง repo = 0) | **สร้างใหม่ — P11** |
| **AI transcript / chapter / summary / watch-plan / marketing copy** | ❌ ไม่มีเลย | **สร้างใหม่ — P12** |
| Course `Type (Live/VOD/Hybrid)` | ❌ `COURSE` ไม่มี discriminator | เพิ่ม `DeliveryFormat` — P11-01 |
| Bunny Player SDK / Clean Architecture / IHostedService | ⛔ ขัด D-09 (Shaka) / D-04 (Modular Monolith) / D-07 (Hangfire) | **ยึดของเดิม** (D-21) |

**กฎที่คุมทั้งดีไซน์นี้** (จาก `.claude/rules/*` + `SECURITY.md`): server ตัดสินสิทธิ์เท่านั้น · ลิงก์ Meet เป็นสิทธิ์เรียน ต้องผ่าน entitlement check เหมือน playback · ห้าม cascade/hard delete บน enrollment/invite · เวลาเก็บ UTC (`DateTimeKind.Utc` — Npgsql throw ถ้าไม่ใช่) แสดง Asia/Bangkok · module คุยกันผ่าน `Contracts/` เท่านั้น · ไม่มี dependency ใหม่ด้าน crypto/auth/payment โดยไม่ถาม (Google SDK + Anthropic SDK = ถาม → Q10/Q12)

---

## 1. Domain model — "session สด" กับ "บันทึก" เป็นคนละของ

```
CATALOG.COURSES                 + DeliveryFormat  (OnDemand | Live | Hybrid)
                                 + EnrollmentDeadlineUtc (nullable — Q13.1) · MaxSeats (nullable int — Q13.2)
   └─ CATALOG.COURSE_LIVE_SESSIONS   ตารางสอน (schedule) — ของ Catalog aggregate
         Id, CourseId, Title, Description, StartsAtUtc, EndsAtUtc, SortOrder,
         Status (Scheduled | Cancelled), CancelReason,
         RecordingEpisodeId  → CATALOG.COURSE_EPISODES (nullable)   ← "catch-up" คือตรงนี้
LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS  บัญชี Google ที่ผู้สอนเชื่อมต่อเอง (Q10=B) — ของ module Live
         InstructorProfileId (UQ, 1:1), GoogleEmail, RefreshTokenEncrypted (ผ่าน ISensitiveDataProtector),
         Scopes, ConnectedAtUtc, LastValidatedAtUtc, RevokedAtUtc (nullable — token ใช้ไม่ได้แล้ว)
LIVE.SESSION_MEETINGS           ห้องประชุมของ session — ของ module Live
         SESSION_ID (UQ), PROVIDER='GoogleMeet'|'Manual', PROVIDER_EVENT_ID, MEET_URL,
         INSTRUCTOR_GOOGLE_ACCOUNT_ID (FK, nullable ถ้า Manual), SYNC_STATUS (Pending|Synced|Failed|Deleted|NeedsReconnect),
         LAST_SYNC_AT_UTC, ERROR
LIVE.SESSION_INVITES            สถานะเชิญรายคน — ของ module Live
         SESSION_ID, USER_ID, ENROLLMENT_ID, STATUS (Pending|Invited|Cancelled|Skipped),
         ICS_SENT_AT_UTC, GOOGLE_ATTENDEE_SYNCED_AT_UTC, REMINDER_24H_AT_UTC, REMINDER_1H_AT_UTC, ERROR
         UQ(SESSION_ID, USER_ID)
LIVE.SESSION_JOIN_LOGS          audit ทุกครั้งที่ขอลิงก์เข้าห้อง (เหมือน PLAYBACK_SESSIONS)
-- P12 (AI Study) เลื่อนออกไปทั้งเฟส (Q12 ตัดสิน 2026-09-16) — MEDIA.MEDIA_AI_ENRICHMENTS/LEARNING.STUDY_PLANS
-- ยังไม่สร้าง ไม่ผูกกับ P11 เลย ดู §3 (คงไว้เป็นแบบร่างสำหรับตอนที่ revisit)
```

> **Q10 = ทางเลือก B (2026-09-16, แก้จาก default A เดิม)**: ไม่มี user กลาง `live@<domain>`/ไม่ซื้อ Google Workspace ให้แพลตฟอร์ม — ผู้สอนแต่ละคนเชื่อม Google Calendar ของตัวเองผ่าน OAuth ผลกระทบสถาปัตยกรรมเต็ม ๆ อยู่ที่ `DECISIONS.md` Q10 และ §2.1 ด้านล่าง (แก้ใหม่ทั้งหมดจาก draft เดิม)

**ทำไม session อยู่ใน Catalog แต่ Meet อยู่ใน module ใหม่**
- ตารางสอนเป็นส่วนของ "โครงสร้างคอร์ส" เหมือน section/episode: builder แก้, หน้า course detail (SSR, public) แสดง, `Course.Publish()` ต้องรู้ (invariant ใหม่ §1.2) → ถ้าแยก module จะเกิด Catalog→Live→Catalog วน (project reference cycle) ซึ่ง ArchitectureTest ห้าม
- Google/Meet/attendee/reminder เป็น bounded context ที่มี lifecycle ของตัวเอง (job, retry, credential) → module `Siri.Modules.Live` (Repository+Service, UPPERCASE ตาม D-17) reference `Catalog.Contracts`, `Learning.Contracts`, `Identity.Contracts`, `Notification.Contracts` ทางเดียว
- Catalog ต้องบอก Live ว่า "มี session ใหม่/แก้/ยกเลิก" โดยไม่ reference Live → ใช้แพทเทิร์นเดิมของ `IEpisodeAccessReader` (interface ประกาศใน Catalog.Contracts, implement ใน module อื่น): `ILiveMeetingSink` ประกาศใน `Catalog.Contracts`, Live implement, stage แถว `SESSION_MEETINGS` บน `AppDbContext` เดียวกัน (commit โดย handler ของ Catalog — แบบเดียวกับ `IInstructorRoleGrantor`)

**ทำไม "บันทึกการสอน" = `COURSE_EPISODE` ธรรมดา ไม่ใช่ตารางใหม่**
- playback/entitlement/watermark/progress/resume/quiz/attachment/certificate/AI ทุกอย่างผูกกับ episode อยู่แล้ว — ถ้าสร้าง "recording" เป็นของใหม่ต้องทำซ้ำทั้งสาย
- `RecordingEpisodeId` บน session ชี้ไป episode ที่มี `MediaAssetId` → "catch-up mode" = query `session.RecordingEpisodeId != null && episode.Status == Ready` ไม่มี logic พิเศษฝั่ง Learning
- `BunnyVideoId` ในสเปค = `MEDIA_ASSET.PROVIDER_ASSET_ID` ผ่าน episode (ไม่ซ้ำคอลัมน์)

### 1.1 `DeliveryFormat` กระทบอะไร

| | OnDemand (ค่าเดิมทุกคอร์ส) | Live | Hybrid |
|---|---|---|---|
| ต้องมี session ≥1 | ไม่ | ใช่ | ใช่ |
| Publish invariant | ≥1 episode มี media (เดิม) | ≥1 session `Scheduled` ที่ `StartsAtUtc > now` **หรือ** ≥1 episode มี media | อย่างใดอย่างหนึ่ง |
| syllabus | section/episode | ตารางสอน + section "บันทึก" (ถ้ามี) | ทั้งสอง |
| `AccessDurationDays` | นับจากวันซื้อ (เดิม) | **นับจาก `StartsAtUtc` ของ session แรกสุด** (Q13.3, ตัดสิน 2026-09-16) — fallback เป็นวันซื้อถ้ายังไม่มี session ตั้งไว้ตอนซื้อ (edge case ที่ยอมรับ) | เหมือน Live |
| `EnrollmentDeadlineUtc` / `MaxSeats` | ไม่มีผล (เดิมไม่มีฟิลด์นี้) | ผู้สอนตั้งเองต่อคอร์ส ทั้งสองฟิลด์ nullable = ไม่จำกัด (Q13.1/Q13.2) | เหมือน Live |
| card บนหน้า catalog | เดิม | badge "สอนสด" + วันเริ่ม | badge "Hybrid" |

Migration: `AddCourseDeliveryFormatAndLiveSessions` — `DeliveryFormat` default `'OnDemand'` (string enum ตาม convention) ไม่กระทบแถวเดิม

### 1.2 สถานะ session (derived, ไม่เก็บ)

```
Cancelled                                  ← เก็บจริง
Scheduled  ─ now < StartsAtUtc-15m ─►  Upcoming
           ─ StartsAtUtc-15m ≤ now ≤ EndsAtUtc ─►  Live      (ปุ่ม "เข้าห้อง" เปิด)
           ─ now > EndsAtUtc ─►  Ended → RecordingEpisodeId == null ? "รอบันทึก" : "ดูย้อนหลัง"
```
คำนวณที่ server ด้วย `IClock` แล้วส่ง `displayState` ออก API (FE ไม่ตัดสินเอง — กัน clock ผู้ใช้เพี้ยน) · หน้าต่างเข้าห้องเริ่มก่อน 15 นาที ปิดเมื่อจบ (config `Live:JoinWindowBeforeMinutes`)

---

## 2. Flow หลัก

### 2.1 ผู้สอนเชื่อม Google → สร้าง session → ได้ Meet link อัตโนมัติ (BR-INS-01/02) — แก้ทั้งหมดตาม Q10=B

**ขั้นก่อนหน้า (ครั้งเดียวต่อผู้สอน) — เชื่อมต่อ Google Calendar:**
```
Instructor Studio → "เชื่อมต่อ Google Calendar"
  → GET /api/live/instructor/google/connect  → redirect ไป Google OAuth consent (scope calendar.events ขั้นต่ำ — ยืนยันตอนเขียน contract)
  → GET /api/live/instructor/google/callback?code=...
       แลก code เป็น access+refresh token → เข้ารหัส refresh token ด้วย ISensitiveDataProtector
       → upsert LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS (1 แถวต่อผู้สอน)
  → DELETE /api/live/instructor/google  (ถอนการเชื่อมต่อเอง) · GET .../google/status (เชื่อมอยู่ไหม, อีเมลไหน, ต้องเชื่อมใหม่ไหม)
```
ยังไม่เชื่อม/token ใช้ไม่ได้แล้ว = สร้าง session ได้ปกติแต่ Meet จะเป็น `PROVIDER=Manual` (ผู้สอนวางลิงก์เอง) จนกว่าจะเชื่อม/เชื่อมใหม่

**สร้าง/แก้/ยกเลิก session:**
```
Builder (tab "ตารางสอนสด")
  → POST /api/catalog/instructor/courses/{id}/live-sessions  {title, startsAt (ISO, tz Asia/Bangkok), endsAt}
  → CreateLiveSessionHandler (Catalog):
       ownership (IsInstructorOwnerOfCourseAsync) · DeliveryFormat ≠ OnDemand · ไม่ทับกันในคอร์สเดียว · ระยะ 15 นาที–8 ชม.
       course.AddLiveSession(...) → ILiveMeetingSink.OnSessionScheduled(sessionId, instructorProfileId)   (stage SESSION_MEETINGS: Pending)
       SaveChangesAsync ครั้งเดียว
  → Hangfire job  live-meeting-sync  (ทุก 1 นาที, Live module):
       SESSION_MEETINGS.Pending → ตรวจ INSTRUCTOR_GOOGLE_ACCOUNTS ของผู้สอนเจ้าของคอร์ส:
          ไม่เชื่อม/RevokedAtUtc ไม่ null → PROVIDER=Manual, SYNC_STATUS=Synced ทันที (ไม่มี MeetUrl ให้ระบบ ผู้สอนกรอกเอง)
          เชื่อมอยู่ → decrypt refresh token → ขอ access token ใหม่ → ICalendarProvider.CreateEventWithMeetAsync(...)
             (Google Calendar API v3, conferenceData.createRequest, เรียกในนามผู้สอนคนนั้นโดยตรง ไม่ impersonate ใคร)
             → 401/token invalid → SYNC_STATUS=NeedsReconnect + แจ้งผู้สอนผ่าน USER_NOTIFICATION "เชื่อม Google ใหม่"
             → สำเร็จ → เก็บ PROVIDER_EVENT_ID + MEET_URL → Synced
          fail อื่น (เครือข่าย/Google 5xx) → retry backoff 1,5,15,60 นาที แล้ว Failed + แจ้ง instructor
  → แก้เวลา/ชื่อ → OnSessionChanged → job patch event (attendee เดิมได้ update จาก Google เอง)
  → ยกเลิก  → OnSessionCancelled → job ลบ event + SESSION_INVITES → Cancelled → อีเมล CANCEL (ICS METHOD:CANCEL)
```
- Meet URL **ไม่เคยอยู่ใน response สาธารณะ** (course detail / search) — อยู่ใน `GET /api/live/instructor/sessions/{id}` (ownership) และ `POST /api/live/sessions/{id}/join` (entitlement) เท่านั้น
- **ไม่มีบัญชี host กลางของแพลตฟอร์มอีกต่อไป** (ทาง A เดิมตกไป) — event ทุกอันอยู่บนปฏิทินส่วนตัวของผู้สอนเจ้าของคอร์ส ผู้สอนเป็น organizer เอง ไม่ใช่ attendee/co-host
- **เพดานผู้เข้าร่วม/ระยะเวลาประชุมเป็นไปตามแผน Google ของผู้สอนแต่ละคนเอง** ไม่ใช่ของแพลตฟอร์ม — builder ต้องแสดงคำเตือนนี้ตอนตั้งตารางสอน (ผู้สอนใช้ Gmail ฟรี ~100 คน/60 นาทีสำหรับ group call)
- **Google OAuth app (scope calendar) ต้องผ่าน verification ก่อนใช้กับผู้ใช้ทั่วไป** — ใช้เวลาเป็นสัปดาห์ เป็นงานที่เจ้าของโปรเจ็คต้องเริ่มทันที (ไม่ใช่รอถึงตอนจะ deploy) ระหว่างรอใช้โหมด Testing ได้กับ test user ≤100 คน

### 2.2 ซื้อ → enroll → เชิญเข้าปฏิทิน (BR-STU-02/03) + latecomer (BR-STU-04)

**ไม่แตะ webhook ของ Stripe เลย** — `StripeWebhookHandler` เรียก `EnrollUserInCoursesAsync` อยู่แล้ว (ทั้ง PromptPay/บัตร/ฟรี/admin/ops เข้าทางเดียวกันหมด) การเชิญเป็น **reconciliation job แบบ diff** ไม่ใช่ event เพราะต้องรองรับ 4 กรณีเท่ากัน: ซื้อหลังตารางออก, ผู้สอนเพิ่ม session หลังมีคนซื้อ, enrollment ถูก revoke/expire, session ถูกยกเลิก

**ก่อน enroll ได้ — 3 กฎใหม่จาก Q13 (2026-09-16) ที่ต้องเช็คตอนสร้าง Order/Enrollment สำหรับคอร์ส Live/Hybrid:**
1. **ปิดรับสมัคร (Q13.1)**: `OrderService.CreateAsync` ปฏิเสธ (409) ถ้า `now > course.EnrollmentDeadlineUtc` (null = ไม่จำกัด) — เช็คต่อคอร์สในตะกร้า ไม่ใช่ทั้งออร์เดอร์
2. **เพดานที่นั่ง (Q13.2)**: นับที่นั่งที่ใช้ไปแบบ **atomic ที่ระดับ SQL** ตอนสร้าง enrollment (ห้ามอ่านนับใน memory — `database.md`) รูปแบบ `UPDATE COURSES SET SEATS_USED = SEATS_USED + 1 WHERE COURSE_ID=@id AND (MAX_SEATS IS NULL OR SEATS_USED < MAX_SEATS)` แล้วเช็ค affected rows ก่อนเรียก `EnrollUserInCoursesAsync` — เต็มแล้ว = ปฏิเสธ order ตั้งแต่ต้น (409) ไม่ปล่อยให้จ่ายเงินแล้วเข้าไม่ได้
3. **`AccessDurationDays` นับจาก session แรก (Q13.3)**: `StripeWebhookHandler`/`OrderService`'s enroll step อ่าน `ILiveScheduleReader.GetEarliestScheduledSessionAsync(courseId)` — มี → `ExpiresAtUtc = earliestSession.StartsAtUtc + AccessDurationDays` · ไม่มี (ยังไม่ตั้งตารางเลยตอนซื้อ) → fallback `ExpiresAtUtc = now + AccessDurationDays` (edge case ที่ยอมรับ) — คอร์ส `OnDemand` ไม่เปลี่ยนพฤติกรรมเดิมเลย (ยังนับจากวันซื้อ)

```
Hangfire job  live-invite-reconcile  (ทุก 2 นาที, [DisableConcurrentExecution])
  for session in ILiveScheduleReader.GetUpcomingSessionsAsync(now, now+90d) where meeting Synced:
     enrolled  = ILearningAccessContract.GetActiveEnrolledUserIdsAsync(courseId)      ← contract ใหม่ (default method)
     invited   = SESSION_INVITES where SESSION_ID
     + (enrolled − invited)  → insert Pending
     + (invited − enrolled)  → Cancelled (ส่ง ICS CANCEL, ถอด attendee)
  for invite Pending (batch 100):
     contact = IUserContactReader.GetUsersContactInfoAsync(...)
     (1) IEmailOutbox.Enqueue(to, subject, html, templateKey:"live-session-invite",
                             calendar: ICS METHOD:REQUEST, UID=session id, SEQUENCE=n, TZID=Asia/Bangkok, LOCATION=meetUrl)
     (2) ถ้า course.GoogleAttendeeSyncEnabled && invitedCount < Live:GoogleAttendeeCap (default 150):
            ICalendarProvider.AddAttendeeAsync(eventId, email, sendUpdates: none)   ← ไม่ให้ Google ส่งอีเมลซ้ำ
     → Invited
```
- **Latecomer:** job มองแค่ session ที่ `StartsAtUtc > now` → session ที่ผ่านไปแล้วไม่ถูกเชิญ (ตรงสเปค "Past sessions → skip calendar") ส่วน VOD ย้อนหลังเปิดทันทีเพราะ entitlement ของ episode ไม่รู้จักเวลา session เลย
- **ทำไมส่ง ICS เอง ไม่พึ่ง Google attendee อย่างเดียว:** Google จำกัด ~200 guest/event และมี quota อีเมลเชิญ; attendee list ทำให้ผู้เรียนเห็นอีเมลกันเอง (PDPA); ICS ผ่าน outbox ของเราไม่มีเพดาน, brand เรา, ใช้ได้ทั้ง Gmail/Outlook/Apple (UID เดียวกัน + SEQUENCE เพิ่ม = อัปเดตในปฏิทินผู้รับ) → **ICS = baseline, Google attendee = ตัวเลือกต่อคอร์ส** (ยืนยันที่ Q11)
- Reminder: job `live-session-reminders` (ทุก 5 นาที) ส่ง 24 ชม./1 ชม. ก่อนเริ่ม ผ่าน outbox + `USER_NOTIFICATION` (in-app มีอยู่แล้ว) ติด flag บน invite กันซ้ำ

### 2.3 ผู้เรียนเข้าห้องสด (BR-STU-03)

```
POST /api/live/sessions/{sessionId}/join     [Authorize] rate limit "default"
  → ILearningAccessContract.HasActiveEnrollmentAsync(userId, courseId)   (ไม่ผ่าน → 403 ไม่บอกว่ามี session)
  → session.Status == Scheduled && now ∈ [StartsAt-15m, EndsAt]           (นอกหน้าต่าง → 409 + displayState)
  → meeting.SYNC_STATUS == Synced                                          (ยัง Pending/Failed → 503 "ห้องยังไม่พร้อม")
  → insert SESSION_JOIN_LOGS(user, session, sid claim, ip)                 (forensics เหมือน PLAYBACK_SESSIONS)
  → 200 { meetUrl, startsAtUtc, endsAtUtc }
```
ผู้ที่ไม่ได้ enroll แต่ได้ลิงก์ Meet ต่อจากคนอื่น: การตั้งค่าห้อง (admit, access type) เป็นของบัญชี Google ส่วนตัวของผู้สอนแต่ละคนเอง (Q10=B) แพลตฟอร์มสั่งบังคับจากส่วนกลางไม่ได้ — บันทึกเป็นข้อจำกัดที่ยอมรับ (คล้าย Q8: กันไม่ได้ 100% แต่มี log ว่าใครขอลิงก์ผ่าน `SESSION_JOIN_LOGS`) แนะนำผู้สอนตั้ง "ต้อง admit ก่อนเข้า" เองในหน้าตั้งค่า Meet ของตัวเอง (ข้อความแนะนำใน builder ไม่ใช่ enforcement)

### 2.6 Refund หลังเข้าเรียนสดแล้ว — hard block อัตโนมัติ (Q13.4, 2026-09-16)

```
RefundService.RequestAsync(orderId, ...)
  for each course ในออร์เดอร์ที่ DeliveryFormat != OnDemand:
     ถ้ามี row ใน LIVE.SESSION_JOIN_LOGS ของ enrollment นี้ (เข้าคาบสดไปแล้วอย่างน้อย 1 ครั้ง)
        → ปฏิเสธคำขอ refund ทันทีที่ระบบ (409) ไม่เข้าคิว admin เลย
```
- ต่างจาก policy เดิม (P3-05: admin approve/reject ทุกกรณี) เฉพาะกรณีนี้กรณีเดียว — เหตุผล refund อื่นที่ไม่เกี่ยวกับการเข้าเรียนสด (เนื้อหาไม่ตรง, ปัญหาทางเทคนิคฝั่งแพลตฟอร์ม) ยังเป็นกระบวนการเดิมทุกอย่าง ไม่เปลี่ยน
- ออร์เดอร์ที่มีหลายคอร์ส: ปฏิเสธเฉพาะ refund ของคอร์สที่เข้าเรียนสดไปแล้ว ส่วนคอร์สอื่นในออร์เดอร์เดียวกันที่ยังไม่ได้เข้าเรียนขอ refund ได้ตามปกติ (ต้องยืนยันกับโครงสร้าง `REFUND`/`ORDER_ITEM` จริงตอนเขียน contract ว่า refund แบบ per-item ทำได้อยู่แล้วหรือไม่)

### 2.4 จบสอนสด → อัปโหลดบันทึก → VOD (BR-INS-03, BR-STU-04)

```
Builder: การ์ด session (Ended, ยังไม่มีบันทึก) → ปุ่ม "อัปโหลดบันทึก"
  → ใช้ flow เดิม 100%: POST /api/media/assets → upload-sessions → TUS → complete → Bunny webhook → Ready
  → POST /api/catalog/instructor/courses/{id}/live-sessions/{sid}/recording  {mediaAssetId, sectionId?, episodeTitle?}
  → AttachSessionRecordingHandler (Catalog):
       ownership · asset เป็นของผู้สอนคนนี้ + Ready (IMediaAssetContract.GetAssetSummaryAsync)
       สร้าง COURSE_EPISODE ใหม่ใน section ที่เลือก (default: section "บันทึกการสอนสด" สร้างอัตโนมัติครั้งแรก)
       episode.AttachMedia(mediaAssetId, duration) → session.RecordingEpisodeId = episode.Id
       (หรือเลือก episode ที่มีอยู่แล้วแทน — ส่ง episodeId แทน mediaAssetId)
  → ผู้เรียนทุกคน (รวม latecomer) เห็น "ดูย้อนหลัง" ทันที · P12 job เริ่ม transcribe อัตโนมัติถ้า opt-in
```
Google Meet มี recording อัตโนมัติ (Workspace บางแผน) ลง Google Drive — **v1 ไม่ดึงอัตโนมัติ** (ต้องมี Drive scope + ดาวน์โหลดผ่าน VPS = bandwidth ที่ D-15 ตั้งใจเลี่ยง) ให้ผู้สอนดาวน์โหลดแล้วอัปผ่าน TUS · เก็บเป็น P11 backlog ถ้ามีคนขอ

### 2.5 บัตรเครดิต (BR-STU-02) — ดึง `P10-01` มาเป็น `P11-09`

```
POST /api/commerce/payments {orderId, method:"Card"}
  → StripePaymentMethod.CreatePaymentIntentAsync: PaymentMethodTypes=["card"], Confirm=false (ไม่ใส่ PaymentMethodData)
  → คืน clientSecret (มีใน PaymentResponse อยู่แล้ว แต่ FE ไม่เคยอ่าน)
FE checkout: @stripe/stripe-js + Payment Element → stripe.confirmPayment({clientSecret, return_url:/checkout/{id}})
  → 3DS ถ้าธนาคารบังคับ → กลับหน้า checkout → poll GET /api/commerce/payments/{id} เดิม
Webhook: payment_intent.succeeded เดิม (ไม่เปลี่ยน) → enroll → split → receipt
  + อ่าน charge.balance_transaction.fee → ส่ง OrderItemSplitInfo.PaymentFee (param มีอยู่แล้ว ตอนนี้ส่ง null)   ← Q4 "หักค่าธรรมเนียมก่อนแบ่ง"
```
- **ไม่ใช้ Stripe Checkout Session** (redirect ไปหน้า Stripe): ต้องเพิ่ม webhook type ใหม่ (`checkout.session.completed`), แยก state machine ที่สอง, และหน้า checkout/QR/promo ที่ทำแล้วจะไร้ประโยชน์ — PaymentIntent ทำได้ครบเท่ากัน (บัตร+PromptPay+3DS) ในสถาปัตยกรรมเดียว
- เจ้าของโปรเจ็คต้องเปิด card ใน Stripe Dashboard เอง · เรตค่าธรรมเนียมบัตร ≠ PromptPay → ต้องเข้าสูตร Q4 ตามที่ `PAYMENT.md` เตือนไว้แล้ว
- ผ่อนชำระยังไม่มีเหมือนเดิม (Stripe ไทยไม่รองรับ — `PAYMENT.md`)

---

## 3. AI Study (P12) — **เลื่อนออกไปทั้งเฟส (Q12 ตัดสิน 2026-09-16 — ไม่เปิดใช้ AI ในรอบนี้)**

> ทั้งหมดใน §3 เป็น**แบบร่างที่ยังไม่เริ่มสร้าง** ไม่ผูกกับ P11 เลย ไม่บล็อกอะไรใน P11 — เก็บไว้ให้ revisit ได้ทันทีเมื่อเจ้าของโปรเจ็คสั่งเปิด (ไม่ต้องออกแบบใหม่) ดู `DECISIONS.md` Q12

ตรวจจากเอกสาร Bunny 2026-09-16: **Bunny Stream Transcribe AI** ทำ transcription (Whisper, รองรับ **ไทย** `th`), แปล, และ **generate chapters/moments/title/description ให้เอง** ผ่าน `POST /library/{libraryId}/videos/{videoId}/transcribe` (`sourceLanguage`, `generateChapters`, `generateMoments`, `targetLanguages`) ผลอยู่บน video object (`chapters[{title,start,end}]`, `moments[{label,timestamp}]`, caption VTT) ราคา ~$0.10/นาที/ภาษา → **chapter ไม่ต้องเรียก LLM เลย** ลดทั้งต้นทุนและงาน

```
recording Ready (BunnyWebhookHandler / BunnyTranscodePollJob)
  → ถ้า course.AiEnrichmentEnabled (instructor opt-in, Q12): MEDIA_AI_ENRICHMENTS upsert Status=Pending
Hangfire  ai-enrichment  (ทุก 2 นาที)
  Pending     → IVideoProvider.RequestTranscriptionAsync(videoId, "th", chapters:true, moments:true) → Transcribing
  Transcribing→ IVideoProvider.GetTranscriptionAsync → มี chapters + VTT? → เก็บ TRANSCRIPT_TEXT/CHAPTERS_JSON/MOMENTS_JSON → Summarizing
  Summarizing → ILlmClient.GenerateStructuredAsync(SummaryPrompt(transcript, chapters))   (Anthropic SDK, model Ai:Model default claude-opus-5,
                 structured output: {summaryTh, keyPoints[≤7], reviewQuestions[≤5], suggestedTitle}) → Ready
  ทุกขั้นเขียน AI_GENERATION_LOG (purpose, model, tokens, usd) + เช็ค Ai:MonthlyBudgetUsd ก่อนเรียก (เกิน → Skipped + แจ้ง admin)
```
- **Chapters ในผู้เล่น (BR-STU-05):** `GET /api/media/playback/{episodeId}` เพิ่ม `chapters[]` (ผ่าน entitlement เดิม) → Shaka player วาด marker บน seek bar + รายการคลิก seek (UI ใหม่บนผู้เล่นเดิม ไม่เปลี่ยน player)
- **Summary (BR-STU-06):** `GET /api/learning/episodes/{episodeId}/study` → summary/keyPoints/reviewQuestions (entitlement ผ่าน `ILearningAccessContract.CanUserAccessEpisodeAsync`)
- **Watch-plan (BR-STU-06):** `GET /api/learning/enrollments/{id}/study-plan` — input = episode ที่ยังไม่จบ (EPISODE_PROGRESS) + session ถัดไป (ILiveScheduleReader) + summary ที่มี → LLM จัดลำดับ "ดูอะไรก่อนคาบหน้า กี่นาที" → cache `LEARNING.STUDY_PLANS` (VALID_UNTIL = min(next session start, +24h)) · ถ้าไม่มี LLM budget → fallback แบบ rule-based (เรียงตาม sort order + เวลาที่เหลือ) ไม่ error
- **Marketing copy (BR-INS-04):** `POST /api/catalog/instructor/courses/{id}/ai/marketing-copy {channel: Facebook|Line|Instagram, tone}` → stateless, ใช้ title/subtitle/outcomes/summary ของ session ล่าสุด → คืน 3 draft · rate limit ต่อผู้สอน 20/วัน · **ไม่บันทึกอัตโนมัติ** ผู้สอน copy เอง (กัน AI เขียนทับ description จริง)
- **Session summary สำหรับโปรโมท (BR-INS-04):** ใช้ summary จาก enrichment ของ recording episode ตรง ๆ (`GET .../live-sessions/{sid}/summary`)
- Privacy: transcript เป็นเนื้อหาของผู้สอน ส่งไป Bunny (ที่เก็บวิดีโออยู่แล้ว) และ Anthropic (ใหม่) → ต้อง opt-in ต่อคอร์ส + ระบุใน privacy policy/ข้อตกลงผู้สอน (Q12) · ไม่ส่งข้อมูลผู้เรียนเข้า LLM (watch-plan ส่งแค่ชื่อ episode/นาที ไม่ส่งชื่อ/อีเมล)

---

## 4. Contracts ใหม่/แก้ (งานของ system-architect + Claude เท่านั้น — ตาม charter)

| Contract | อยู่ที่ | implement โดย | ใช้โดย |
|---|---|---|---|
| `ILiveScheduleReader` — `GetSessionsForCourseAsync(courseId)`, `GetUpcomingSessionsAsync(fromUtc, toUtc)`, `GetSessionAsync(id)`, `GetEarliestScheduledSessionAsync(courseId)` (ใหม่ — สำหรับ Q13.3 access-duration) → `LiveSessionInfo(SessionId, CourseId, Title, StartsAtUtc, EndsAtUtc, Status, RecordingEpisodeId)` | `Catalog.Contracts` | Catalog | Live, Commerce (enroll), Learning (study-plan, deferred) |
| `ILiveMeetingSink` — `OnSessionScheduledAsync(sessionId, instructorProfileId)`, `OnSessionChangedAsync`, `OnSessionCancelledAsync` (stage บน DbContext เดิม ไม่ save) | `Catalog.Contracts` | **Live** | Catalog handlers |
| `ILearningAccessContract.GetActiveEnrolledUserIdsAsync(courseId)` (default method) | `Learning.Contracts` | Learning | Live (invite diff) |
| `IEmailOutbox.Enqueue(..., EmailCalendarPart? calendar)` overload + คอลัมน์ `CALENDAR_ICS text`, `CALENDAR_METHOD` บน `NOTIFY.EMAIL_OUTBOX` + `EmailMessage.CalendarPart` ใน `Siri.Integrations.Email` (MimeKit `text/calendar; method=`) | `Notification.Contracts` / Integrations.Email | Notification | Live |
| `ICalendarProvider` — **แก้จาก draft เดิม (Q10=B, 2026-09-16)**: `CreateEventWithMeetAsync`/`UpdateEventAsync`/`DeleteEventAsync`/`AddAttendeeAsync`/`RemoveAttendeeAsync` ทุกเมธอดรับ **credential ต่อ call** (resolve จาก `InstructorGoogleAccount`'s decrypted refresh token) **ไม่ใช่** instance เดียวที่ตั้งค่า service-account impersonation ตอน DI — ไม่มี `GoogleCalendarOptions.ServiceAccountJson`/`ImpersonateUser` อีกต่อไป เหลือแค่ `ClientId`/`ClientSecret` ของ OAuth app (public info ระดับหนึ่ง แต่ยังเก็บผ่าน user-secrets/env) · เพิ่ม `IGoogleOAuthService` ใหม่ (`GetAuthorizationUrl`, `ExchangeCodeAsync`, `RefreshAccessTokenAsync`) | **`Siri.Integrations.Google` (project ใหม่)** | Google adapter + `LoggingCalendarProvider` (dev/Manual fallback) | Live |
| `IMediaAssetContract.GetEnrichmentAsync(mediaAssetId)` (default) — **เลื่อนพร้อม P12**, ไม่ต้อง implement ตอนนี้ | `SharedKernel.Contracts` | Media (ยังไม่ทำ) | Learning, Catalog (ยังไม่ทำ) |

**ตัดออกจาก wave นี้ (เลื่อนพร้อม P12 — Q12 ตัดสิน 2026-09-16):** `IVideoProvider.RequestTranscriptionAsync`/`GetTranscriptionAsync`, `ILlmClient`/`Siri.Integrations.Ai`, `ILearningAccessContract.GetIncompleteEpisodesAsync` — ไม่ต้องออกแบบ/สร้างจนกว่าจะ revisit P12

`Siri.ArchitectureTests/ModuleAssemblyCatalog.cs` ต้องเพิ่ม `Siri.Modules.Live` และ `Siri.Integrations.Google` ด้วยมือ (ไม่ใช่ reflection — ลืมแล้ว boundary test จะข้ามเงียบ ๆ ตาม `ARCHITECTURE.md`)

---

## 5. API surface (สรุป — รายละเอียด request/response/error ไปอยู่ใน contract ต่อ task)

| Method + path | Policy | Task |
|---|---|---|
| `GET /api/catalog/courses/{slug}` **+ `deliveryFormat`, `liveSchedule{timezone, upcomingCount, pastCount, nextStartsAtUtc, sessions[{id,title,startsAtUtc,endsAtUtc,displayState,hasRecording}]}`** (ไม่มี meetUrl) | Anonymous (เดิม) | P11-07 |
| `GET /api/catalog/courses/search` **+ `format` filter + facet** | Anonymous | P11-07 |
| `POST/PUT/DELETE /api/catalog/instructor/courses/{id}/live-sessions[/{sid}]`, `POST .../{sid}/cancel`, `POST .../{sid}/recording`, `PUT .../delivery-format`, `PUT .../enrollment-policy {enrollmentDeadlineUtc?, maxSeats?}` (ใหม่ — Q13) | InstructorOnly + ownership | P11-02, P11-11 |
| `GET /api/live/instructor/google/connect` (redirect ไป Google OAuth) · `GET .../google/callback` · `DELETE .../google` · `GET .../google/status` | InstructorOnly | P11-03 |
| `GET /api/live/instructor/sessions/{sid}` (meetUrl, sync status, invited/joined count, roster) | InstructorOnly + ownership | P11-05 |
| `GET /api/live/courses/{courseId}/my-sessions` (sessions + invite status + displayState + recordingEpisodeId) | Authorize + enrollment | P11-05 |
| `POST /api/live/sessions/{sid}/join` | Authorize + enrollment + window, rate limit `default` | P11-05 |
| `GET /api/live/sessions/{sid}/calendar.ics` (ดาวน์โหลด ICS ซ้ำ) | Authorize + enrollment | P11-05 |
| `POST /api/commerce/payments {method:"Card"}` (ปลดล็อก) | Authorize (เดิม) | P11-09 |
| `GET /api/commerce/admin/webhook-events?type=&from=&result=` (อ่าน `STRIPE_WEBHOOK_EVENTS`) | AdminOnly | P11-08 |
| `GET /api/analytics/admin/media-usage` (Bunny statistics API: bandwidth/storage เดือนนี้ + เพดาน) | AdminOnly | P11-08 |
| `POST /api/commerce/refunds` **+ hard-block เมื่อมี `SESSION_JOIN_LOGS`** (Q13.4) | Authorize + ownership (เดิม) | P11-12 |

**ตัดออกจาก wave นี้ (เลื่อนพร้อม P12):** `GET /api/media/playback/{episodeId} + chapters[]`, `GET /api/learning/episodes/{episodeId}/study`, `GET /api/learning/enrollments/{id}/study-plan`, `POST .../ai/marketing-copy`, `PUT .../ai-settings` — ดู §3

ทุก endpoint ใหม่ต้องมี **ทั้ง MVC controller (production, D-19) และ minimal-API mapper** ถ้าจะให้ integration test harness เดิม (`_app.Map*Endpoints()`) ครอบ — หรือย้าย harness ไปใช้ `WebApplicationFactory` (บันทึกใน P11-30 ให้ตัดสินตอนเขียนเทสต์)

---

## 6. Frontend (สรุปหน้า/จุดแทรก — ตามรายงานสำรวจ 2026-09-16)

| หน้า | มีอยู่ | เพิ่ม | Task |
|---|---|---|---|
| Course detail `features/catalog/course-detail-page` | ✅ | section "ตารางสอนสด" ระหว่าง description (`:114`) กับ syllabus (`:115`), badge format, บรรทัด "คาบถัดไป … / ดูย้อนหลังได้" ใน `<aside>` ราคา (`:299`), JSON-LD `Course` เพิ่ม `courseMode: online-live` + `Event` ต่อ session | P11-21 |
| Catalog `/courses` | ✅ | filter format + badge บนการ์ด (`CoursesPage` + `HomeCourseCard`) | P11-21 |
| Builder `course-builder-page.ts` (1268 บรรทัด — เกิน rule 200 อยู่แล้ว) | ✅ | **แยก component ใหม่** `live-schedule-panel` (ไม่ยัดเพิ่มในไฟล์เดิม): เลือก format, ตาราง session (เพิ่ม/แก้/ยกเลิก, เวลา Asia/Bangkok), ช่องกรอก "วันปิดรับสมัคร"/"เพดานที่นั่ง" (Q13.1/13.2), การ์ด Ended → อัปโหลดบันทึก (reuse `MediaApiService.uploadVideoFile`), แสดง Meet link + สถานะ sync + ปุ่ม "เชื่อมต่อ Google Calendar"/สถานะการเชื่อมต่อ (Q10=B) + คำเตือนเพดาน Meet ตามบัญชี Google ของตัวเอง | P11-20 |
| Classroom `/learn/:slug` | ✅ | แท็บ "สอนสด": upcoming (countdown + ปุ่มเข้าห้อง + เพิ่มลงปฏิทิน) / past (ดูย้อนหลัง → episode / รอบันทึก) | P11-22 |
| My courses + การ์ดเรียนต่อ (P2-26) | ✅ | "คาบถัดไป: ส. 10:00" + ปุ่มเข้าห้องเมื่อถึงเวลา | P11-23 |
| Checkout `checkout-page.ts` | ✅ PromptPay | tab "บัตรเครดิต/เดบิต" + `@stripe/stripe-js` Payment Element (โหลดแบบ `@defer`, publishable key จาก `GET /api/commerce/payments/config`) | P11-24 |
| Instructor dashboard `P4-24` | PART (KPI hardcode `฿45,200`) | ต่อ `GET /api/analytics/instructor/dashboard/summary` จริง + การ์ด "session ถัดไป" + roster | P11-25 |
| Admin | ✅ users/payments/audit | หน้า webhook events, tile Bunny bandwidth บน dashboard, **เพิ่ม role guard** ที่ `/admin/**` (ตอนนี้ `authGuard` อย่างเดียว) | P11-26 |
| Player `video-player.ts` | ✅ Shaka | chapter marker บน seek bar + รายการ chapter (คลิก → seek) + `aria` | P12-20 |
| Classroom | ✅ | แผง "สรุปบทเรียน (AI)" + "แผนดูก่อนคาบหน้า" | P12-21/22 |
| Instructor | ✅ | แผง AI: toggle opt-in, marketing copy generator, summary ของ session | P12-23 |

i18n: namespace ใหม่ `live.*`, `ai.*` ทั้ง `th.json`/`en.json` · ทุกหน้าใหม่ CSR+noindex ยกเว้น course detail/catalog (SSR) · เวลาแสดงผ่าน pipe `Asia/Bangkok` เท่านั้น

---

## 7. งานที่กระทบของเดิม (พบระหว่างสำรวจ — บันทึกเป็น X-ใหม่ใน `TASKS.md`)

> **สถานะจริง ณ ตอนนี้อยู่ที่ `TASKS.md` เท่านั้น (ไฟล์นี้ไม่อัปเดตซ้ำ)** — X-28/X-29/X-31 ถูกแก้และผ่าน review แล้ว (2026-09-16), X-30 ถูกแก้และผ่าน review แล้วเช่นกัน (Antigravity Phase M) รายละเอียดเต็มอยู่ที่ตาราง "ปัญหาข้ามเฟส" ของ `TASKS.md`

| ID | เรื่อง | ทำไมสำคัญกับ hybrid |
|---|---|---|
| **X-28** | FE ไม่ต่ออายุ signed URL: `getPlaybackSession` เรียกครั้งเดียว/episode ทั้งที่ token TTL 5 นาที (`learning-api.service.ts:36`, `PlaybackSessionService.PlaybackUrlLifetime`) — Bunny token auth ตรวจ `expires` ทุก request → บันทึกสอนสดยาว 1–3 ชม. จะหยุดโหลด segment หลัง 5 นาที | บล็อก catch-up mode จริง — **แก้แล้ว** |
| **X-29** | heartbeat 15 วิ = 1 DB write/beat/user (`EpisodeProgressService.UpsertProgressAsync`) ไม่มี rate limit, ไม่เขียน `WATCH_EVENT` (ตารางมีแต่ไม่มีใครเขียน → drop-off analytics ว่างเปล่า) | เมื่อผู้เรียน 5k คนดูพร้อมกัน (D-10) = 333 write/วินาที — **แก้แล้ว** (partitioned rate limit ต่อผู้ใช้ + append `WATCH_EVENT` ทุกครั้ง) |
| **X-30** | `/admin/**` มีแค่ `authGuard` ไม่มี role guard ฝั่ง FE (backend policy คุมอยู่ แต่ผู้เรียนเปิด `/admin` ได้เห็น shell ว่าง) | BR-ADM-01 — **แก้แล้ว** |
| **X-31** | ระบบประกาศไม่เคยส่งอีเมล/แจ้งเตือนถึงใครเลยจริง ๆ (ร้ายแรงกว่าที่บันทึกไว้ตอนแรกว่าแค่ "scheduled ไม่ส่ง") | ไม่เกี่ยวกับ P11-04 โดยตรงแล้ว (แก้แยกเสร็จก่อน) — **แก้แล้ว** |

---

## 8. ลำดับส่งมอบ (เรียงตาม dependency จริง)

**อัปเดต 2026-09-16 — Q10–Q13 ตอบครบแล้ว, Q12 = เลื่อน P12 ทั้งเฟส:**

```
ก่อนเริ่ม P11-03 (เจ้าของโปรเจ็คต้องทำเอง — critical path ยาวกว่างานเขียนโค้ด):
  สมัคร/ตั้ง OAuth consent screen ใน Google Cloud Console (scope calendar.events) + submit verification
  (ใช้เวลาเป็นสัปดาห์ — เริ่มทันที ระหว่างรอใช้โหมด Testing ได้กับ test user ≤100 คน)
  · เปิด card ใน Stripe Dashboard (สำหรับ P11-09/24)
  · P12 (AI) ไม่ต้องทำอะไรตอนนี้ — เลื่อนทั้งเฟสตาม Q12

Wave 1 (ไม่ติดอะไร — เริ่มได้ทันที, กำลังทำ/เสร็จแล้วบางส่วน):
  P11-01 (Catalog schema — DONE) → P11-02/P11-07 (Antigravity, ANTIGRAVITY_HANDOFF.md §7 Phase N)
  + X-28/X-29/X-30/X-31 (DONE) + P11-09/24 (บัตรเครดิต — DONE) + P11-11 (seat cap + enrollment deadline, ใหม่จาก Q13.1/13.2)

Wave 2 (พร้อมเริ่มได้เลย — ไม่ติด Q แล้ว แต่ติด OAuth app verification ที่เป็น external lead time):
  P11-03 (OAuth connect + Siri.Modules.Live + Siri.Integrations.Google — ออกแบบใหม่ตาม Q10=B)
    → P11-04 (invite reconcile + ICS, ไม่เปลี่ยนจาก draft เดิม) → P11-05 (join API) → P11-06 (recording attach)
    → P11-12 (refund hard-block, Q13.4) → P11-13 (AccessDurationDays จาก session แรก, Q13.3)
    → P11-22/23/25 (FE: classroom live tab, my-courses, instructor dashboard)

Wave 3: P11-08/26 (admin) → P11-30/31 (tests)

Wave 4 — เลื่อนไม่มีกำหนด (P12 ทั้งเฟส, รอสั่งเปิดใหม่): P12-01..30 ทั้งหมดตาม §3
```
ผู้ลงมือ (ตาม charter `system-architect`): งานที่แตะ entitlement/เงิน/Contracts ข้ามโมดูล/OAuth-credential (P11-01/03/04/05/06/09/11/12/13, X-28/29) = **Claude** (backend-developer/frontend-developer) · UI ที่ไม่แตะสิทธิ์ (P11-20/21/23/25/26) = Antigravity ได้เมื่อ contract FROZEN

---

## 9. ความเสี่ยงเฉพาะของ hybrid (เพิ่มจาก `ROADMAP.md`)

| ความเสี่ยง | การรับมือ |
|---|---|
| **Google OAuth app (scope calendar) ต้องผ่าน verification ก่อนใช้กับผู้ใช้ทั่วไป** — ใช้เวลาเป็นสัปดาห์ (Q10=B, 2026-09-16) | เริ่มขั้นตอนนี้ทันทีไม่รอถึงตอน deploy · ระหว่างรอใช้โหมด Testing (≤100 test user) สำหรับ dev/beta · fallback `PROVIDER=Manual` ถ้าผู้สอนยังไม่เชื่อม/token หมดอายุ |
| **ผู้สอนถอนสิทธิ์ Google เมื่อไหร่ก็ได้ = session ที่ sync ไว้แล้วอัปเดตไม่ได้อีก** | job ตรวจ token invalid (401) → `SYNC_STATUS=NeedsReconnect` + แจ้งเตือนผู้สอนทันที + session เดิมยังมี Meet URL ที่ออกไปแล้วใช้ได้ (แค่แก้ไข/ยกเลิกผ่านระบบไม่ได้จนกว่าจะเชื่อมใหม่) |
| **เพดานผู้เข้าร่วม/ระยะเวลา Meet ขึ้นกับบัญชี Google ของผู้สอนแต่ละคนเอง** (ไม่ใช่แผน Workspace ส่วนกลางอีกต่อไป) | เตือนผู้สอนในหน้า builder ตอนตั้งตารางสอน (ข้อความคงที่ ไม่ต้อง query แผนจริงของผู้สอน — ทำไม่ได้ผ่าน Calendar API) · ไม่ hard block การขาย |
| Calendar attendee cap ~200/event + quota อีเมลเชิญ | ICS ผ่าน outbox เป็น baseline (§2.2) |
| Clock skew / timezone ผิดคาบ | เก็บ UTC + `IClock`, FE รับ ISO UTC แปลงเดียวที่ pipe, ทดสอบ DST-free (ไทยไม่มี DST แต่ผู้เรียนต่างประเทศมี) |
| ลิงก์ Meet หลุดให้คนไม่ได้ซื้อ | join log + ผู้สอนตั้ง "ต้อง admit ก่อนเข้า" เองในบัญชี Google ของตัวเอง + (Q8 เดิม) ยอมรับว่ากันไม่ได้ 100% |
| PDPA: อีเมลผู้เรียนไป Google | attendee sync เป็น opt-in ต่อคอร์ส + ระบุ processor ใน privacy policy (P7 PDPA งานเดิม) — **เพิ่มใหม่**: refresh token ของผู้สอนเองก็เป็นข้อมูลอ่อนไหวที่ต้องเข้ารหัส (`ISensitiveDataProtector`) และระบุใน "ข้อตกลงผู้สอน" ว่าแพลตฟอร์มเข้าถึงปฏิทิน Google ของตนได้ |
| ที่นั่งเต็มพอดีตอนมีคนซื้อพร้อมกันหลายคน (race) — Q13.2 | นับที่นั่งแบบ atomic ที่ SQL (UPDATE...WHERE affected-rows check) ไม่อ่าน-แล้ว-เขียนใน memory ตาม `database.md` |
| ~~ค่า AI บานปลาย~~ | **ไม่เกี่ยวข้องแล้วในรอบนี้** — P12 เลื่อนทั้งเฟส (Q12) |
