# Contract: P11-05 Learner/Instructor live API — **join gate (ตรวจสิทธิ์ก่อนคืนลิงก์ห้อง)**, my-sessions, calendar.ics, instructor session/roster, join logs

Status: FROZEN · วันที่: 2026-10-06 · Module: `Siri.Modules.Live` (Repository+Service, UPPERCASE) + `Siri.Modules.Learning` (1 default method ใน Contracts) + `Siri.Modules.Catalog` (1 endpoint อ่าน: `live-schedule`)

ผู้ลงมือที่แนะนำ: **Claude — `DATABASE` (WP-A) → `backend-developer` (WP-B..E)** — นี่คือ **ด่านสิทธิ์เข้าเรียนสด** (entitlement + ข้อมูลส่วนตัว + ลิงก์ที่เป็น capability) ตรงเกณฑ์ที่ห้ามส่ง Antigravity ตาม charter · ไม่ต้อง DESIGN_DATABASE (ตารางเดียว append-only)

**Dependency:** P11-03 (`SESSION_MEETINGS`, `SessionMeetingService.RevealUrl`, `LiveSessionContext`, rate-limit policies `live-join`/`live-user`) + P11-04 (`SESSION_INVITES` ใช้แสดง `inviteStatus`/`expectedLearners`) · JSON/TypeScript → `P11-FE-live-dto-appendix.md` §B

---

## 0. กฎเหล็กของ contract นี้ (ตรวจด้วยเทสต์ ไม่ใช่แค่เอกสาร)

1. **ลิงก์ห้อง (`meetUrl`) ออกจากระบบได้ 2 ทางเท่านั้น:** `POST /api/live/sessions/{sid}/join` (หลังตรวจสิทธิ์) และ `GET /api/live/instructor/sessions/{sid}` (เจ้าของคาบ) — **ไม่มีที่อื่น** (list/detail/search สาธารณะ, my-sessions, อีเมล, ICS, log, error, ProblemDetails) · เทสต์ recursive JSON walk ครอบทุก endpoint ของ Live + public course detail/search
2. **server ตัดสินสิทธิ์เสมอ:** ผู้เรียน = enrollment `Active` ที่ยังไม่หมดอายุของ **คอร์สของคาบนั้น** (`ILearningAccessContract.HasActiveEnrollmentAsync`) · ผู้สอน = เจ้าของคอร์สของคาบ (`LiveSessionContext.InstructorUserId == userId`) · **Admin ไม่ bypass** (เหมือน P1-04) — FE ซ่อนปุ่ม ≠ การป้องกัน
3. **ไม่เปิดเผยการมีอยู่:** คาบไม่มีจริง / ไม่มีสิทธิ์ / enrollment หมดอายุ-ถูก revoke → **404 ตัวเดียวกัน** ข้อความเดียวกัน (`live.not_found`) — `sessionId` ปรากฏใน public course detail อยู่แล้ว จึงไม่ใช่ความลับ แต่ "ใครมีสิทธิ์" เป็นความลับ
4. ทุก response ที่เกี่ยวกับลิงก์ห้อง: `Cache-Control: no-store, no-cache` + `Pragma: no-cache` (ทั้งสำเร็จและ error)
5. rate limit **partitioned ต่อผู้ใช้** (`live-join` = 6/นาที) — **ห้ามใช้ policy "default"** (fixed-window รวมทั้งแอป 100 req/นาที — ผู้เรียนพร้อมกันตอนคาบเริ่มจะ 429 กันเอง; เทรปเดียวกับ X-29)
6. **ความเสี่ยงที่เหลือ (ยอมรับ ไม่ซ่อน):** ผู้เรียนที่ผ่านด่านแล้วยังส่งลิงก์ Meet ต่อให้คนอื่นได้ 1 ต่อ — ด่านนี้กัน "ได้ลิงก์จากแพลตฟอร์มโดยไม่ได้จ่าย" ไม่ใช่ "คนที่จ่ายแล้วแชร์ต่อ" · mitigation: join log (รู้ว่าใครขอลิงก์ — `SESSION_JOIN_LOGS`), Google attendee sync opt-in (P11-04 §6 — คนนอกต้องขอเข้าห้อง), ผู้สอนตั้ง host admit, ไม่มี copy-button บน FE และมีข้อความเตือน "อย่าส่งต่อลิงก์" · **ไม่มี watermark/DRM สำหรับ Meet** (เทียบ Q8: ยอมรับเป็นข้อจำกัดของ Meet)

## 1. Scope
**ใน:** migration `AddLiveSessionJoinLogs` · `SessionJoinService` + `LiveLearnerQueries` + `LiveInstructorQueries` · `Live.Contracts.ILiveAttendanceReader` · Learning contract `GetActiveEnrolledCourseIdsAsync` · Catalog `GET …/live-schedule` · 2 controller (`LiveLearnerController`, `LiveInstructorController`)
**นอก:** meeting/Google/ผู้สอนวางลิงก์ (P11-03) · invite/ICS email (P11-04) · recording (P11-06) · refund (P11-12) · KPI dashboard (P11-10) · FE

---

## 2. Schema — migration `AddLiveSessionJoinLogs` — `LIVE.SESSION_JOIN_LOGS` (entity `SESSION_JOIN_LOG`, **append-only, ไม่ implement IAuditable/ISoftDelete** — เหมือน `MEDIA.PLAYBACK_SESSIONS`)

| Column / property | Type | Null | หมายเหตุ |
|---|---|---|---|
| `SESSION_JOIN_LOG_ID` | uuid PK | no | UUIDv7 |
| `SESSION_ID` | uuid | no | ไม่มี FK |
| `COURSE_ID` | uuid | no | **denormalize** เพื่อให้ refund (P11-12) ตรวจด้วย (USER_ID, COURSE_ID) ได้โดยไม่ join ข้ามโมดูล |
| `USER_ID` | uuid | no | จาก `IUserContext` เท่านั้น |
| `ROLE` | varchar(16) | no | `LiveParticipantRole` (`Learner`\|`Instructor`) — refund/KPI นับเฉพาะ `Learner` |
| `AUTH_SESSION_ID` | uuid | yes | claim `sid` ของ JWT (forensics แบบเดียวกับ playback) |
| `JOINED_AT_UTC` | timestamptz(3) | no | เวลาที่ **เปิดเผยลิงก์** |
| `IP_ADDRESS` | varchar(64) | yes | `RemoteIpAddress` (อาจเป็น IP proxy — gap เดิมของระบบ ไม่มี `UseForwardedHeaders`) |
| `USER_AGENT` | varchar(300) | yes | ตัดที่ 300 |
Index: PK `PK_SESSION_JOIN_LOGS` · `IX_SESSION_JOIN_LOGS_SESSION_USER` (`SESSION_ID`,`USER_ID`) · `IX_SESSION_JOIN_LOGS_USER_COURSE` (`USER_ID`,`COURSE_ID`) · **ห้าม UPDATE/DELETE ใดๆ** (ไม่มี method แก้บน entity; มีแต่ `static Record(...)`) · PII (IP/UA) retention เป็น follow-up (scrub หลัง 12 เดือน; `USER_ID/COURSE_ID/SESSION_ID` ต้องเก็บไว้ตามกฎ refund Q13.4)

---

## 3. Contracts ข้ามโมดูล

### 3.1 `Live.Contracts.ILiveAttendanceReader` (ใหม่ — implement ใน `Live.Infrastructure.LiveAttendanceReader`, Scoped)
```csharp
namespace Siri.Modules.Live.Contracts;
public sealed record LiveSessionStats(Guid SessionId, int ExpectedLearners, int JoinedLearners, bool MeetingUsable);
public interface ILiveAttendanceReader
{
    /// courseIds ที่ userId เคยขอลิงก์เข้าคาบสดอย่างน้อย 1 ครั้งในฐาน Learner (ใช้กับ P11-12 refund)
    Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken ct);
    /// ExpectedLearners = ผู้เรียนที่ invite STATUS=Invited · JoinedLearners = distinct USER_ID ใน join log (Learner) · MeetingUsable จาก SESSION_MEETINGS
    Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct);
}
```
ผู้บริโภค: Commerce (P11-12), Analytics (P11-10) — query แบบ batch ครั้งเดียว (`GROUP BY`) ห้าม N+1

### 3.2 `Learning.Contracts.ILearningAccessContract` — เพิ่ม default method
```csharp
/// course ids ที่ userId มี enrollment Active และไม่หมดอายุ ณ ตอนนี้ (rule เดียวกับ HasActiveEnrollmentAsync) — default คืนว่าง
Task<IReadOnlyList<Guid>> GetActiveEnrolledCourseIdsAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
```
`LearningAccessContract` override: query เดียว `Query().Where(USER_ID==userId && STATUS==Active && (EXPIRES_AT_UTC==null || > now)).Select(COURSE_ID)` · unit test รวม enrollment หมดอายุ/Revoked ไม่ติดมา

---

## 4. Endpoint ที่ FROZEN (JSON → appendix §B) — ทุกตัว `[Authorize]`/policy ระบุชัด, `[EnableRateLimiting(...)]`, error = RFC 9457 + `reason`

| # | Method + path | Auth | Rate limit | สำเร็จ | Error |
|---|---|---|---|---|---|
| 1 | `GET /api/live/courses/{courseId}/my-sessions` | `[Authorize]` + enrollment active (ผู้เรียน) | `live-user` | 200 `MySessionsResponse` | 404 `live.not_found` (ไม่มีสิทธิ์/ไม่พบ — รวมกัน) |
| 2 | `GET /api/live/me/sessions/upcoming?limit=10` | `[Authorize]` | `live-user` | 200 `MyUpcomingSessionsResponse {serverTimeUtc, items: MyUpcomingSessionItem[]}` (limit 1–20, default 10) | — |
| 3 | **`POST /api/live/sessions/{sessionId}/join`** | `[Authorize]` + entitlement | **`live-join`** | 200 `JoinLiveSessionResponse` + no-store | 404 `live.not_found` · 409 `live.session_cancelled` / `live.window_not_open` (`opensAtUtc`) / `live.session_ended` (`recordingEpisodeId?`) · **503** `live.meeting_not_ready` (+`Retry-After: 30`) · 429 |
| 4 | `GET /api/live/sessions/{sessionId}/calendar.ics` | `[Authorize]` + entitlement | `live-user` | 200 `text/calendar; charset=utf-8; method=PUBLISH` + `Content-Disposition: attachment; filename="live-{sessionId:N}.ics"` + no-store | 404 · 409 `live.session_cancelled`/`live.session_ended` |
| 5 | `GET /api/live/instructor/sessions?scope=upcoming\|past\|all&courseId=&page=1&pageSize=20` | `InstructorOnly` | `live-user` | 200 `PagedResult<InstructorSessionListItem>` (pageSize ≤50) | — |
| 6 | `GET /api/live/instructor/sessions/{sessionId}` | `InstructorOnly` + เจ้าของคาบ | `live-user` | 200 `InstructorSessionDetailResponse` (**มี `meetUrl`**) + no-store | 403 ไม่ใช่เจ้าของ · 404 |
| 7 | `GET /api/live/instructor/sessions/{sessionId}/roster?filter=all\|joined\|notJoined&page=1&pageSize=50` | `InstructorOnly` + เจ้าของคาบ | `live-user` | 200 `PagedResult<RosterItem>` (pageSize ≤100) | 403/404 |
| 8 | `GET /api/catalog/instructor/courses/{courseId}/live-schedule` (**Catalog**, บน `InstructorCoursesController`) | `InstructorOnly` + เจ้าของคอร์ส | `live-user` | 200 `CourseLiveScheduleResponse` | 403/404 (แยกกัน แบบ Catalog) |

กฎการแมปตอบ: ข้อ 1–4 ใช้ 404 รวม (กฎเหล็กข้อ 3) · ข้อ 5–7 เป็นของผู้สอน — คอร์ส/คาบไม่ใช่ความลับสำหรับผู้สอน จึงแยก 403/404 ได้ (แพทเทิร์นเดียวกับ handler ของ Catalog) · `userId` มาจาก `IUserContext` เสมอ

### 4.1 `POST …/join` — ลำดับตรวจ **บังคับตามนี้** (`SessionJoinService.JoinAsync(userId, sessionId, authSessionId, ip, userAgent, ct)`)
```
1. ctx = ILiveScheduleReader.GetSessionContextsAsync([sessionId]).FirstOrDefault()
        null                                                         → NotFound("live.not_found")            [log live.join.denied reason=no_session]
2. isOwner  = ctx.InstructorUserId == userId
   entitled = isOwner || await ILearningAccessContract.HasActiveEnrollmentAsync(userId, ctx.CourseId)
        !entitled                                                    → NotFound("live.not_found")  (ข้อความ/สถานะเดียวกับข้อ 1 ทุกไบต์ ยกเว้น traceId)   [log reason=not_entitled]
3. ctx.Status == Cancelled                                           → Conflict reason "live.session_cancelled"
4. now > ctx.EndsAtUtc                                               → Conflict reason "live.session_ended"   ext { recordingEpisodeId, courseSlug }
5. !isOwner && now < ctx.StartsAtUtc − Live:JoinWindowBeforeMinutes  → Conflict reason "live.window_not_open" ext { opensAtUtc, serverTimeUtc }   (ผู้สอน = เจ้าของ เข้าห้องเตรียมได้ก่อนเวลา)
6. meeting = ISessionMeetingRepository.GetBySessionIdAsync(sessionId)
        meeting == null || !meeting.IsUsable                         → Unavailable reason "live.meeting_not_ready"   (controller ใส่ Retry-After: 30)
7. url = SessionMeetingService.RevealUrl(meeting)   (decrypt ล้มเหลว → log Error(sessionId) + Unavailable เดียวกัน; ห้ามคืน 500 ที่หลุดข้อมูล)
8. ISessionJoinLogRepository.Add(SESSION_JOIN_LOG.Record(sessionId, ctx.CourseId, userId, isOwner ? Instructor : Learner, authSessionId, now, ip, truncate(userAgent,300)))
   SaveChangesAsync   — **ต้อง commit log สำเร็จก่อนคืน URL** (log ล้ม = ไม่ให้ URL)
9. return JoinLiveSessionResponse(sessionId, url, ctx.StartsAtUtc, ctx.EndsAtUtc, serverTimeUtc: now)
```
- เวลา = `IClock` เท่านั้น · `LiveSessionDisplayStateCalculator.Compute(...)` (Catalog.Contracts) ใช้ในข้อ 1–2 ของ my-sessions เพื่อให้ `displayState` ตรงกับ gate (ส่ง `joinWindowBeforeMinutes` จาก `LiveOptions`)
- **ไม่เขียน join log ตอนถูกปฏิเสธ** (log เฉพาะการเปิดเผยลิงก์จริง — ตารางนี้คือหลักฐานของ refund rule) แต่เขียน structured log `live.join.denied` (userId, sessionId, reason code — **ไม่ใส่ URL/อีเมล**) ไว้สืบสวนการพยายามเดา id
- controller: `sid` = `HttpContext.User.FindFirstValue("sid")` (parse Guid ไม่ได้ → `null`) · `ip = HttpContext.Connection.RemoteIpAddress?.ToString()` · `User-Agent` จาก header · ตั้ง `Cache-Control`/`Pragma` ผ่าน `Response.Headers` **ก่อน** คืน `IResult` (ทั้งสำเร็จและ error) · 503 ใส่ `Retry-After: 30`

### 4.2 `GET …/my-sessions` (`LiveLearnerQueries.GetMySessionsAsync`)
entitlement (HasActiveEnrollment) → ไม่ผ่าน = 404 · `ILiveScheduleReader.GetSessionContextsForCoursesAsync([courseId], null, null, includeCancelled:true)` · batch: meetings (`roomReady = IsUsable`), invites ของ (userId, sessionIds) → `inviteStatus`, `hasAttendedAnySession` จาก `ILiveAttendanceReader.GetCourseIdsAttendedAsync` · `displayState`/`canJoin` ที่ server เท่านั้น (`canJoin = displayState==Live`) · `joinOpensAtUtc = StartsAtUtc − window` · เรียง `StartsAtUtc` ASC · response มี `timezone`=`Asia/Bangkok`, `serverTimeUtc` (= `IClock.UtcNow` ตอนคำนวณ — ให้ FE ใช้คำนวณ offset นาฬิกาและตั้งเวลา refetch) · **ไม่มี `meetUrl`**
### 4.3 `GET …/calendar.ics`
entitlement เหมือน join (เจ้าของคาบก็ได้) · คาบ Cancelled → 409 · `EndsAtUtc <= now` → 409 · `IcsCalendarBuilder.Build(Publish, [event seq=meeting.ICS_SEQUENCE], …)` (UID/SEQUENCE ตรงกับที่ส่งทางอีเมล) · `JoinUrl` = `…/live/{sid}/join` · **ไม่มี meetUrl**
### 4.4 instructor endpoints
- `sessions` list: `scope=upcoming` → overlap window `[now, ∞)` ไม่รวม Cancelled, เรียงใกล้สุดก่อน · `past` → `(-∞, now)` รวม Cancelled, ล่าสุดก่อน · `all` → ทั้งหมด · แนบ `meeting` summary (ไม่มี URL) + `ILiveAttendanceReader.GetSessionStatsAsync` (expected/joined) — batch เดียว
- `sessions/{sid}` detail: เจ้าของคาบเท่านั้น · `meetUrl` = `RevealUrl` (null ถ้ายังไม่มี) · `Cache-Control: no-store` · เพิ่ม `courseSlug`, `enrolledCount` (= `GetActiveEnrolledUserIdsAsync().Count`), `expectedLearners`, `joinedLearners`, `serverTimeUtc`; `needsAction` อยู่ใน `meeting` (`InstructorMeetingSummary` — นิยามที่ appendix §A.5)
- `roster`: ประกอบจาก (enrolled ∪ invite learners ∪ ผู้มี join log) → ติดต่อ `IUserContactReader.GetUsersContactInfoAsync` แบบ batch → **`displayName` + `emailMasked`** (ไม่คืนอีเมลเต็ม — least privilege; รูปแบบ `a***@g***.com` ผ่าน `EmailMasker.Mask` ที่ unit test) · กรอง `joined`/`notJoined` · เรียง `displayName` · แบ่งหน้า (ตัดหน้าก่อนแล้วค่อยแปลงผลลัพธ์) · `firstJoinedAtUtc`/`joinCount` จาก join log

### 4.5 Catalog addendum — `GET /api/catalog/instructor/courses/{courseId}/live-schedule`
vertical slice `Features/GetCourseLiveSchedule/{Handler,Response}.cs` · ownership แบบ Catalog เดิม (404/403 แยก) · อ่าน `COURSE` (+ `LiveSessions` ทุกสถานะ) → `{ courseId, deliveryFormat, status, enrollmentDeadlineUtc, maxSeats, seatsUsed, googleAttendeeSyncEnabled, sessions: LiveSessionResponse[] }` (`LiveSessionResponse` = record เดิมของ P11-02: id, courseId, title, description, startsAtUtc, endsAtUtc, sortOrder, status, cancelReason, recordingEpisodeId) เรียง `StartsAtUtc` · เหตุผลที่มี: builder (P11-20) ยังไม่มีทางโหลดตารางสอน/รูปแบบ/นโยบายปัจจุบัน (`CourseBuilderResponse` ไม่มี field พวกนี้ และ P11-01 §3.6 ฝากรายการไว้ที่ P11-05) — **ไม่มี meetUrl** (สถานะห้องอยู่ที่ `GET /api/live/instructor/courses/{id}/meetings` ของ P11-03)

---

## 5. Test checklist

**Unit:** `SessionJoinService` ทุก branch ลำดับ §4.1 (ด้วย fake reader/learning/meeting repo + `FakeClock`; ขอบเวลาพอดี `StartsAt−15m` = ผ่าน, `−15m−1tick` = 409, `EndsAt` พอดี = ผ่าน, `EndsAt+1tick` = 409) · **404 ของ "ไม่พบ" กับ "ไม่มีสิทธิ์" เท่ากันทุกฟิลด์** (assert ProblemDetails เท่ากันหลังตัด traceId) · เจ้าของข้าม window ได้ · join log ล้ม (repo throw) → **ไม่คืน URL** · `EmailMasker` · `LiveAttendanceReader` aggregation · roster pagination/กรอง
**Integration (`SiriApiFactory`, Testcontainers):** IDOR ครบ: ผู้เรียนไม่ซื้อ / enrollment `Expired` / `Revoked` / ผู้สอนอีกคนของคอร์สอื่น / Admin ไม่ใช่เจ้าของ → 404 (join, ics, my-sessions) และ 403/404 ตามตาราง (instructor endpoints) · window/ended/cancelled → 409 พร้อม `reason`/ext · meeting ไม่พร้อม → 503 + `Retry-After` · สำเร็จ → 200 + `Cache-Control: no-store` + แถว join log (userId/courseId/role/sid/ip/ua) · เรียกซ้ำ → log เพิ่ม · **rate limit:** ครั้งที่ 7 ใน 60 วิ → 429, ผู้ใช้อื่นยังผ่าน · **ความลับ:** JSON-walk ทุก response ของ Live + public detail/search ไม่มีคำว่า `meetUrl`/ค่าลิงก์ที่ seed ไว้ ยกเว้น 2 endpoint ที่อนุญาตตามกฎเหล็กข้อ 1 · ลิงก์ที่ seed ไม่ปรากฏใน log ของ test sink · `calendar.ics` UID/SEQUENCE ตรงกับอีเมล P11-04 · refund-attended: `GetCourseIdsAttendedAsync` เห็นเฉพาะ Learner ไม่ใช่ Instructor
**Learning:** `GetActiveEnrolledCourseIdsAsync` (active/expired/revoked)
**Catalog:** `live-schedule` ownership 403/404, ครบทุกสถานะคาบ, ไม่มี meetUrl

---

## 6. Work packages

| WP | ใคร | ทำอะไร | Acceptance |
|---|---|---|---|
| **A** | `DATABASE` | entity `SESSION_JOIN_LOG` (+ `LiveParticipantRole` ถ้ายังไม่มีจาก P11-04) + configuration + migration `AddLiveSessionJoinLogs` (อ่านทั้งไฟล์; additive; ชื่อ ≤63) + `docs/DATABASE.md` | architecture เขียว · ไม่ apply DB จริง |
| **B** | `backend-developer` | Learning `GetActiveEnrolledCourseIdsAsync` · Catalog `GetCourseLiveSchedule` handler + action บน `InstructorCoursesController` · `Live.Contracts.ILiveAttendanceReader` + `LiveAttendanceReader` | unit ตาม §5 |
| **C** | `backend-developer` | `ISessionJoinLogRepository`, `SessionJoinService`, `LiveLearnerQueries`, `LiveInstructorQueries`, `EmailMasker`, DTO/record ตาม appendix §B | unit §5 |
| **D** | `backend-developer` | `LiveLearnerController` (1–4) + `LiveInstructorController` (5–7): `[Authorize]`/policy ทุก action, `[EnableRateLimiting]`, header no-store/Retry-After, `[ProducesResponseType]` ครบ | ไม่มี action ใดไร้ attribute auth |
| **E** | `backend-developer` | integration tests §5 | เขียนครบ (รายงานว่ายังไม่เคยรันถ้าไม่มี Docker) |

ขนานได้: A กับ B (ส่วน Learning/Catalog) · D หลัง C · `DONE` ตั้งโดย integrator-qa · **commit ตามที่ user สั่งเท่านั้น**

## 7. FE
ดู `P11-FE-live-dto-appendix.md` — หน้า `/live/:sessionId/join`, แท็บ "สอนสด" ใน `/learn/:slug`, chip "คาบถัดไป" ใน `/my-courses`, หน้า `/instructor/sessions/:id`, แผงใน builder · **FE ต้องไม่เก็บ `meetUrl` ใน state ที่คงอยู่ (localStorage/router state/URL/console)** และไม่ทำปุ่ม copy ลิงก์

## Changelog
(ยังไม่มี revision หลัง FROZEN)
