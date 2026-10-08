# Appendix: P11 Live — FE DTO appendix + จอ/เส้นทาง/สถานะ (สร้าง FE ขนานกับ BE ได้จากไฟล์นี้)

Status: FROZEN · วันที่: 2026-10-06 · ผูกกับ contract: `P11-03` (§A) · `P11-05` (§B) · `P11-06` + Catalog (§C) · `P11-12` (§D) · `P11-10` (§E) · `P11-04` (live-settings §C.4) · ผู้ลงมือที่แนะนำ: `frontend-developer` (Claude) สำหรับจอที่แตะสิทธิ์/ลิงก์ห้อง/ข้อมูลส่วนตัว (`/live/:sid/join`, แท็บ "สอนสด", roster, หน้าตั้งค่า Google) · Antigravity ได้สำหรับ panel ใน builder / dashboard / course detail หลัง endpoint ที่เกี่ยวพร้อม

> **กติกาไฟล์นี้:** JSON = camelCase · enum = string (global `JsonStringEnumConverter`) · เวลา = ISO-8601 **UTC `…Z`** เสมอ (FE แปลงเป็น `Asia/Bangkok` ที่ pipe เดียว) · ถ้า record C# ใน contract ฝั่ง BE กับ TS ที่นี่ไม่ตรงกัน **record C# ใน contract นั้นชนะ ให้รายงาน system-architect** (ห้ามเดา/ห้ามแก้เอง) · ห้ามมี mock/ข้อมูลตัวอย่างใน component — ถ้า endpoint ยังไม่พร้อมให้ทำหน้า loading/empty/error จริงรอ ไม่ใส่ตัวเลขปลอม

## 0. ชนิดร่วม (`core/http/*` มีอยู่แล้วหลายตัว — ใช้ของเดิมถ้ามี)
```ts
type Guid = string;
type IsoUtc = string;   // "2026-10-01T03:00:00Z"
type LiveDisplayState = 'Upcoming' | 'Live' | 'Ended' | 'Cancelled';        // คำนวณที่ server (Catalog.Contracts.LiveSessionDisplayStateCalculator) — FE ห้ามคำนวณเอง
type LiveSessionStatus = 'Scheduled' | 'Cancelled';
type DeliveryFormat = 'OnDemand' | 'Live' | 'Hybrid';
type MeetingProvider = 'GoogleMeet' | 'Manual' | 'Logging';
type MeetingSyncStatus = 'Pending' | 'AwaitingLink' | 'Synced' | 'NeedsReconnect' | 'Failed' | 'PendingDelete' | 'Deleted';
type MeetingNeedsAction = 'None' | 'Waiting' | 'PasteLink' | 'ReconnectGoogle' | 'Retry';
type InviteStatus = 'Pending' | 'Invited' | 'Cancelled' | 'Skipped';

interface PagedResult<T> { items: T[]; totalCount: number; page: number; pageSize: number; totalPages: number; }

// RFC 9457 — ทุก error ของ Live
interface ApiProblem {
  status: number; title: string; traceId: string;
  errorCode: 'not_found' | 'validation' | 'forbidden' | 'conflict' | 'unavailable' | string;
  reason?: string;                                        // sub-code เสถียร ให้ FE map i18n (ตารางด้านล่าง)
  opensAtUtc?: IsoUtc; serverTimeUtc?: IsoUtc;            // live.window_not_open
  recordingEpisodeId?: Guid; courseSlug?: string;         // live.session_ended
  sessionIds?: Guid[];                                    // live.meetings_not_ready
  blockedCourseTitles?: string[]; maxRefundableAmount?: number; blockedAmount?: number;   // refund.live_attended
}
```
| `reason` | status | ความหมาย → i18n key (`live.errors.*`) |
|---|---|---|
| `live.not_found` | 404 | ไม่พบคาบ **หรือ** ไม่มีสิทธิ์ (รวมกันโดยตั้งใจ) → `notFoundOrNoAccess` |
| `live.session_cancelled` / `live.session_ended` / `live.window_not_open` | 409 | → `cancelled` / `ended` (+ปุ่มดูย้อนหลัง) / `notOpenYet` (+นับถอยหลัง) |
| `live.meeting_not_ready` | **503** (+`Retry-After`) | ห้องยังไม่พร้อม → `roomNotReady` + retry อัตโนมัติ |
| `live.meetings_not_ready` | 400 | publish ไม่ผ่าน — คาบ `sessionIds` ยังไม่มีห้อง → ชี้ไปการ์ดคาบใน builder |
| `live.google_not_configured` | 503 | ระบบยังไม่เปิดเชื่อม Google → ซ่อนปุ่มเชื่อม แสดง "วางลิงก์เอง" |
| `live.meeting_link_invalid` / `live.meeting_link_host_not_allowed` | 400 | → `linkInvalid` / `linkHostNotAllowed` (แสดงโฮสต์ที่อนุญาต: meet.google.com, zoom.us, teams.microsoft.com) |
| `live.session_not_editable` / `live.meeting_not_resyncable` | 409 | |
| `live.session_not_started` / `live.recording_asset_not_ready` / `…_asset_in_use` / `…_episode_has_no_media` / `…_episode_in_use` | 409 | แนบบันทึก (P11-06) |
| `refund.live_attended` | 409 | ขอคืนเงินไม่ได้หลังเข้าห้องสด (§D) |
429 (rate limit) → ข้อความ "ลองใหม่ในอีกสักครู่" (ไม่มี `reason`)

---

## A. P11-03 — Google + ห้องประชุม (ผู้สอน) · ทุกตัว `InstructorOnly`

### A.1 `GET /api/live/instructor/google/status`
```ts
interface GoogleConnectionStatus {
  configured: boolean;            // server มี OAuth client หรือไม่ (false → ซ่อนปุ่มเชื่อม)
  connected: boolean;             // มี refresh token ใช้ได้
  needsReconnect: boolean;        // เคยเชื่อมแต่ token ใช้ไม่ได้ (revoked/หมดอายุ)
  googleEmail: string | null; connectedAtUtc: IsoUtc | null; lastValidatedAtUtc: IsoUtc | null;
  revokedReason: 'invalid_grant' | 'user_disconnected' | 'scope_missing' | 'insufficient_scope' | null;
  affectedSessionCount: number;   // คาบอนาคตของผู้สอนที่รอเชื่อมใหม่/รอวางลิงก์
}
```
C#: `GoogleConnectionStatusResponse(bool Configured, bool Connected, bool NeedsReconnect, string? GoogleEmail, DateTime? ConnectedAtUtc, DateTime? LastValidatedAtUtc, string? RevokedReason, int AffectedSessionCount)`

### A.2 `POST /api/live/instructor/google/connect`
Request `{ "returnPath": "/instructor/live-settings" }` (optional; ต้องขึ้นต้น `/instructor/`) → `200 { "authorizationUrl": "https://accounts.google.com/o/oauth2/v2/auth?…" }` → FE `window.location.assign(authorizationUrl)` · `503 live.google_not_configured`
```ts
interface GoogleConnectRequest { returnPath?: string } interface GoogleConnectResponse { authorizationUrl: string }
```
### A.3 callback (browser navigation — FE รับ query หลัง redirect)
Google → API `GET /api/live/instructor/google/callback` → **302** → `{returnPath}?google=connected` หรือ `?google=error&reason=access_denied|state_invalid|scope_missing|no_refresh_token|exchange_failed` · หน้า `/instructor/live-settings` อ่าน query นี้ → toast + `GET status` ซ้ำ + **ล้าง query ออกจาก URL** (`router.navigate` replaceUrl) · FE ไม่เรียก callback เอง

### A.4 `DELETE /api/live/instructor/google` → `204` (ใช้ซ้ำได้) — ปุ่ม "ยกเลิกการเชื่อมต่อ" (confirm dialog: คาบที่สร้างห้องไปแล้วยังใช้ได้ แต่แก้/ลบ event ผ่านระบบไม่ได้จนกว่าเชื่อมใหม่)

### A.5 `InstructorMeetingSummary` (**ไม่มี URL**) + endpoint list
```ts
interface InstructorMeetingSummary {
  sessionId: Guid;
  provider: MeetingProvider | null;     // null = ระบบยังไม่ตัดสิน (≤1 นาทีหลังสร้างคาบ)
  syncStatus: MeetingSyncStatus;
  hasMeetingLink: boolean;              // มีลิงก์เก็บไว้แล้ว (ไม่เปิดเผยค่า)
  isUsable: boolean;                    // = hasMeetingLink && ไม่ใช่ Deleted — ใช้กับ gate publish/join
  lastSyncAtUtc: IsoUtc | null;
  needsAction: MeetingNeedsAction;
  errorCode: string | null;             // code สั้น เช่น 'conference_pending','google_account_unavailable','invalid_grant' — map i18n; ห้ามแสดงดิบ
}
interface CourseMeetingsResponse { items: InstructorMeetingSummary[] }
```
C#: `InstructorMeetingSummary(Guid SessionId, MeetingProvider? Provider, MeetingSyncStatus SyncStatus, bool HasMeetingLink, bool IsUsable, DateTime? LastSyncAtUtc, MeetingNeedsAction NeedsAction, string? ErrorCode)` · `CourseMeetingsResponse(IReadOnlyList<InstructorMeetingSummary> Items)` (enum ใหม่ `MeetingNeedsAction` ใน Live.Application)
**`needsAction` derivation (BE ต้องทำตามนี้เป๊ะ):** `Pending`→`Waiting` · `AwaitingLink`→`PasteLink` · `NeedsReconnect`→`ReconnectGoogle` · `Failed`→`Retry` · `Synced`+`hasMeetingLink`→`None` · `Synced` ไม่มีลิงก์ (ไม่ควรเกิด)→`PasteLink` · `PendingDelete`/`Deleted`→`None`

| Endpoint | Request → Response |
|---|---|
| `GET /api/live/instructor/courses/{courseId}/meetings` | → `CourseMeetingsResponse` (404 ถ้าไม่ใช่เจ้าของ) |
| `PUT /api/live/instructor/sessions/{sessionId}/meeting-link` | `{ "meetUrl": "https://meet.google.com/abc-defg-hij" }` → `InstructorMeetingSummary` (400/403/404/409 ตาม reason) |
| `POST /api/live/instructor/sessions/{sessionId}/meeting/resync` | (no body) → `InstructorMeetingSummary` (409 `live.meeting_not_resyncable`) |
UI: ช่องวางลิงก์ = `type="url"` + validate เบื้องต้นฝั่ง FE (https) แต่ **server ตัดสินจริง**; ส่งสำเร็จแล้ว **ไม่แสดงลิงก์กลับ** (ผู้สอนเห็นลิงก์ได้ที่หน้า session detail เท่านั้น)

---

## B. P11-05 — ผู้เรียน/ผู้สอน/ด่าน join

### B.1 `GET /api/live/courses/{courseId}/my-sessions` (`[Authorize]`, ผู้เรียนที่ enrollment active)
```ts
interface MySessionItem {
  id: Guid; title: string; description: string | null;
  startsAtUtc: IsoUtc; endsAtUtc: IsoUtc;
  displayState: LiveDisplayState;       // server-computed ณ serverTimeUtc
  status: LiveSessionStatus; cancelReason: string | null;
  canJoin: boolean;                     // displayState === 'Live' (หน้าต่างเข้าห้องเปิด) — ปุ่มเข้าห้องเปิดตามนี้เท่านั้น
  joinOpensAtUtc: IsoUtc;               // startsAtUtc − Live:JoinWindowBeforeMinutes
  roomReady: boolean;                   // ห้องประชุมพร้อมหรือยัง (ไม่มี URL)
  hasRecording: boolean; recordingEpisodeId: Guid | null;
  inviteStatus: InviteStatus | null;    // สถานะคำเชิญปฏิทินของฉัน
}
interface MySessionsResponse {
  courseId: Guid; courseSlug: string; timezone: 'Asia/Bangkok';
  serverTimeUtc: IsoUtc;                // ใช้คำนวณ offset นาฬิกาเครื่องผู้ใช้ — อย่าเชื่อ Date.now() ตัดสินสถานะ
  hasAttendedAnySession: boolean;       // true = เคยขอลิงก์เข้าห้องแล้ว (ใช้แสดง/ซ่อนคำเตือน refund)
  sessions: MySessionItem[];            // เรียง startsAtUtc ASC รวม Cancelled
}
```
```json
{ "courseId":"0198…","courseSlug":"excel-pro","timezone":"Asia/Bangkok","serverTimeUtc":"2026-10-08T02:55:10Z","hasAttendedAnySession":false,
  "sessions":[{"id":"0198…","title":"คาบที่ 1 พื้นฐาน","description":null,"startsAtUtc":"2026-10-08T03:00:00Z","endsAtUtc":"2026-10-08T05:00:00Z","displayState":"Live","status":"Scheduled","cancelReason":null,"canJoin":true,"joinOpensAtUtc":"2026-10-08T02:45:00Z","roomReady":true,"hasRecording":false,"recordingEpisodeId":null,"inviteStatus":"Invited"}] }
```
C#: `MySessionsResponse(Guid CourseId, string CourseSlug, string Timezone, DateTime ServerTimeUtc, bool HasAttendedAnySession, IReadOnlyList<MySessionItem> Sessions)` · `MySessionItem(Guid Id, string Title, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc, LiveSessionDisplayState DisplayState, string Status, string? CancelReason, bool CanJoin, DateTime JoinOpensAtUtc, bool RoomReady, bool HasRecording, Guid? RecordingEpisodeId, string? InviteStatus)`
404 `live.not_found` = ไม่มี enrollment active (FE ซ่อนแท็บ ไม่ error ดัง)

### B.2 `GET /api/live/me/sessions/upcoming?limit=10`
```ts
interface MyUpcomingSessionItem { sessionId: Guid; courseId: Guid; courseTitle: string; courseSlug: string; title: string;
  startsAtUtc: IsoUtc; endsAtUtc: IsoUtc; displayState: LiveDisplayState; canJoin: boolean; joinOpensAtUtc: IsoUtc; }
interface MyUpcomingSessionsResponse { serverTimeUtc: IsoUtc; items: MyUpcomingSessionItem[] }
```
C#: `MyUpcomingSessionsResponse(DateTime ServerTimeUtc, IReadOnlyList<MyUpcomingSessionItem> Items)` — เฉพาะคาบที่ยังไม่จบ/ไม่ถูกยกเลิก ของคอร์สที่ฉัน enrollment active; ใช้ chip "คาบถัดไป" ใน `/my-courses` (จัดกลุ่มตาม `courseId` ที่ FE: เอาคาบแรกต่อคอร์ส)

### B.3 **`POST /api/live/sessions/{sessionId}/join`** (no body)
```ts
interface JoinLiveSessionResponse { sessionId: Guid; meetUrl: string; startsAtUtc: IsoUtc; endsAtUtc: IsoUtc; serverTimeUtc: IsoUtc; }
```
C#: `JoinLiveSessionResponse(Guid SessionId, string MeetUrl, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime ServerTimeUtc)` · header `Cache-Control: no-store` · error ตามตาราง §0 (404/409/503/429) · **FE ห้ามเก็บ `meetUrl` ใน localStorage/sessionStorage/router state/URL/analytics/console** — ถือใน signal ของ component เท่านั้นแล้วล้างตอน destroy

### B.4 `GET /api/live/sessions/{sessionId}/calendar.ics` → ไฟล์ (`text/calendar`) — FE ดึงด้วย `HttpClient` `responseType:'blob'` (ต้องมี bearer) แล้วสั่งดาวน์โหลดเอง (`URL.createObjectURL` + `<a download="live-<id>.ics">` ใน `afterNextRender`/handler คลิก) · 404/409 ตามตาราง

### B.5 ผู้สอน: รายการ/รายละเอียด/roster
```ts
interface InstructorSessionListItem { sessionId: Guid; courseId: Guid; courseTitle: string; title: string; startsAtUtc: IsoUtc; endsAtUtc: IsoUtc;
  displayState: LiveDisplayState; expectedLearners: number; joinedLearners: number; meeting: InstructorMeetingSummary;
  recordingEpisodeId: Guid | null; cancelReason: string | null; }
// GET /api/live/instructor/sessions?scope=upcoming|past|all&courseId=&page=1&pageSize=20  → PagedResult<InstructorSessionListItem>

interface InstructorSessionDetail { sessionId: Guid; courseId: Guid; courseTitle: string; courseSlug: string; title: string; description: string | null;
  startsAtUtc: IsoUtc; endsAtUtc: IsoUtc; displayState: LiveDisplayState; status: LiveSessionStatus; cancelReason: string | null;
  recordingEpisodeId: Guid | null;
  meetUrl: string | null;               // **ผู้สอนเจ้าของคาบเท่านั้น** — null ถ้ายังไม่มีห้อง
  meeting: InstructorMeetingSummary;
  enrolledCount: number;                // ผู้เรียนที่มีสิทธิ์อยู่ตอนนี้
  expectedLearners: number;             // ผู้ที่ถูกเชิญ (Invited)
  joinedLearners: number;               // distinct ผู้เรียนที่ขอลิงก์เข้าห้องแล้ว
  serverTimeUtc: IsoUtc; }
// GET /api/live/instructor/sessions/{sessionId}  (Cache-Control: no-store; 403 ไม่ใช่เจ้าของ / 404)

interface RosterItem { userId: Guid; displayName: string | null; emailMasked: string | null;   // 'a***@g***.com' — ไม่มีอีเมลเต็ม
  inviteStatus: InviteStatus | null; joined: boolean; firstJoinedAtUtc: IsoUtc | null; joinCount: number; }
// GET /api/live/instructor/sessions/{sessionId}/roster?filter=all|joined|notJoined&page=1&pageSize=50 → PagedResult<RosterItem>
```
C#: `InstructorSessionListItem(Guid SessionId, Guid CourseId, string CourseTitle, string Title, DateTime StartsAtUtc, DateTime EndsAtUtc, LiveSessionDisplayState DisplayState, int ExpectedLearners, int JoinedLearners, InstructorMeetingSummary Meeting, Guid? RecordingEpisodeId, string? CancelReason)` · `InstructorSessionDetailResponse(Guid SessionId, Guid CourseId, string CourseTitle, string CourseSlug, string Title, string? Description, DateTime StartsAtUtc, DateTime EndsAtUtc, LiveSessionDisplayState DisplayState, string Status, string? CancelReason, Guid? RecordingEpisodeId, string? MeetUrl, InstructorMeetingSummary Meeting, int EnrolledCount, int ExpectedLearners, int JoinedLearners, DateTime ServerTimeUtc)` · `RosterItem(Guid UserId, string? DisplayName, string? EmailMasked, string? InviteStatus, bool Joined, DateTime? FirstJoinedAtUtc, int JoinCount)`

---

## C. Catalog (ผู้สอน) — ที่มีแล้ว (P11-02) + ที่เพิ่มใหม่

### C.1 อ่านตารางสอนของคอร์ส (ใหม่ — P11-05 §4.5) `GET /api/catalog/instructor/courses/{courseId}/live-schedule`
```ts
interface LiveSession { id: Guid; courseId: Guid; title: string; description: string | null; startsAtUtc: IsoUtc; endsAtUtc: IsoUtc;
  sortOrder: number; status: LiveSessionStatus; cancelReason: string | null; recordingEpisodeId: Guid | null; }
interface CourseLiveSchedule { courseId: Guid; deliveryFormat: DeliveryFormat; status: 'Draft'|'InReview'|'Published'|'Rejected'|'Archived';
  enrollmentDeadlineUtc: IsoUtc | null; maxSeats: number | null; seatsUsed: number; googleAttendeeSyncEnabled: boolean; sessions: LiveSession[]; }
```
C#: `CourseLiveScheduleResponse(Guid CourseId, DeliveryFormat DeliveryFormat, CourseStatus Status, DateTime? EnrollmentDeadlineUtc, int? MaxSeats, int SeatsUsed, bool GoogleAttendeeSyncEnabled, IReadOnlyList<LiveSessionResponse> Sessions)` (`LiveSessionResponse` = record เดิม P11-02)

### C.2 เขียน (P11-02 — มีแล้ว ใช้ตามเดิม): เวลาต้องเป็น UTC `Z`
`POST /api/catalog/instructor/courses/{courseId}/live-sessions` `{ title, description?, startsAtUtc, endsAtUtc }` → 201 `LiveSession` · `PUT …/{sessionId}` (body เดียวกัน) → 200 · `POST …/{sessionId}/cancel` `{ reason }` → 200 `LiveSession` · `DELETE …/{sessionId}` → 204 · `PUT /api/catalog/instructor/courses/{id}/delivery-format` `{ deliveryFormat }` → `{ id, deliveryFormat }` · `PUT …/enrollment-policy` `{ enrollmentDeadlineUtc?, maxSeats? }` → `{ id, enrollmentDeadlineUtc, maxSeats, seatsUsed }` · FE แปลงวัน-เวลา Asia/Bangkok ที่ผู้สอนกรอก → UTC ISO `Z` ก่อนส่ง (ห้ามส่ง offset `+07:00`) · error: 409 ทับเวลา/ไม่ใช่ Scheduled/จบแล้ว, 400 ระยะ 15 นาที–8 ชม./อดีต

### C.3 แนบบันทึก (P11-06) `POST /api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}/recording`
```ts
interface AttachSessionRecordingRequest { mediaAssetId?: Guid; episodeId?: Guid; sectionId?: Guid; episodeTitle?: string }   // mediaAssetId XOR episodeId
interface LiveSessionRecording { sessionId: Guid; recordingEpisodeId: Guid; sectionId: Guid; episodeTitle: string; durationSeconds: number; mediaAssetId: Guid; replacedExisting: boolean }
```
C#: `LiveSessionRecordingResponse(Guid SessionId, Guid RecordingEpisodeId, Guid SectionId, string EpisodeTitle, int DurationSeconds, Guid MediaAssetId, bool ReplacedExisting)` · flow: อัปโหลดด้วย `MediaApiService.uploadVideoFile` + `watchAsset` เดิม → asset `Ready` → เรียก endpoint นี้ → refetch C.1

### C.4 ตั้งค่า Google attendee sync (P11-04) `PUT /api/catalog/instructor/courses/{courseId}/live-settings`
`{ "googleAttendeeSyncEnabled": true }` → `{ "courseId": "…", "googleAttendeeSyncEnabled": true }` · UI toggle + คำอธิบาย: "อีเมลผู้เรียนจะถูกส่งให้ Google เพื่อเชิญเข้า event (ผู้เรียนไม่เห็นอีเมลกัน) — ปิดไว้ได้ ระบบยังส่งอีเมลเชิญเองตามปกติ; เกิน 150 คนระบบปิดให้อัตโนมัติ"

### C.5 อ่านฝั่งสาธารณะ (P11-07 — **DONE แล้ว** — ใช้ต่อจอ P11-21) `GET /api/catalog/courses/{slug}`
`deliveryFormat` + `liveSchedule: { timezone:'Asia/Bangkok', upcomingCount, pastCount, nextStartsAtUtc, sessions:[{ id, title, startsAtUtc, endsAtUtc, displayState, hasRecording }] } | null` (**ไม่มี meetUrl และไม่ต้องเรียก API ของ Live**) · `GET /api/catalog/courses/search` มี `format` filter + `nextStartsAtUtc`/`deliveryFormat` ต่อการ์ด + facet `formats`

---

## D. P11-12 — ขอคืนเงิน
`POST /api/commerce/refunds` (เดิม) ตอบ **409** ได้ใหม่: `{ status:409, errorCode:'conflict', reason:'refund.live_attended', title, blockedCourseTitles:string[], maxRefundableAmount:number, blockedAmount:number }` → FE (หน้า `/account/orders`) แสดง i18n `refund.liveAttended` + รายชื่อคอร์ส; ถ้า `maxRefundableAmount > 0` เสนอปุ่ม "ขอคืนเท่าที่ทำได้" (ใส่ยอดนั้นให้) · ก่อนเข้าห้องครั้งแรก (`hasAttendedAnySession=false`) แสดงข้อความแจ้งใน `/live/:sid/join` และแท็บ "สอนสด" (ข้อมูลเท่านั้น ไม่บล็อก)

---

## E. P11-10 — dashboard ผู้สอน `GET /api/analytics/instructor/dashboard/summary?range=&courseId=` (ฟิลด์เดิมคงเดิม + ต่อท้าย)
```ts
interface InstructorDashboardKpis { periodKey: string; previousPeriodKey: string;                       // 'YYYY-MM' (เดือน UTC) → แสดงชื่อเดือนผ่าน pipe
  netRevenueThisMonth: number; netRevenuePreviousMonth: number; netRevenueChangePercent: number | null;
  totalStudents: number; newStudentsLast7Days: number; publishedCourseCount: number; totalCourseCount: number;
  ratingAverage: number | null; ratingCount: number; }
interface InstructorCourseSummaryItem { courseId: Guid; title: string; slug: string; status: string; deliveryFormat: string; price: number;
  enrollmentCount: number; ratingAverage: number | null; ratingCount: number; thumbnailUrl: string | null; }
interface InstructorNextSession { sessionId: Guid; courseId: Guid; courseTitle: string; title: string; startsAtUtc: IsoUtc; endsAtUtc: IsoUtc;
  displayState: LiveDisplayState; expectedLearners: number; meetingUsable: boolean; }
interface InstructorLiveAttendanceItem { sessionId: Guid; courseId: Guid; courseTitle: string; title: string; startsAtUtc: IsoUtc;
  expectedLearners: number; joinedLearners: number; attendanceRatePercent: number | null; }
interface InstructorAnalyticsSummary { /* …ฟิลด์เดิมของหน้า analytics… */ kpis: InstructorDashboardKpis | null; courseSummaries: InstructorCourseSummaryItem[] | null;
  nextSession: InstructorNextSession | null; liveAttendance: InstructorLiveAttendanceItem[] | null;
  students: { id: string; name: string | null; email: string | null; courseTitle: string; progressPercent: number; completedEpisodes: number; totalEpisodes: number; lastActiveAtUtc: IsoUtc }[]; }  // name/email เป็น nullable ตาม P11-10 §4
```
การ์ด 4 ใบบน dashboard: รายได้สุทธิเดือนนี้ (+% เทียบเดือนก่อน; `null` → "—") · นักเรียนทั้งหมด (+n ใน 7 วัน) · คอร์สที่เผยแพร่ (x จาก y) · คะแนนรีวิว (`null` → "ยังไม่มีรีวิว") · การ์ด "คาบถัดไป" (`nextSession`: ชื่อคอร์ส/คาบ, เวลา, ผู้เรียนที่ถูกเชิญ, เตือนถ้า `meetingUsable=false` + ลิงก์ไป `/instructor/sessions/:id`) · การ์ด "การเข้าร่วมล่าสุด" (`liveAttendance`) · ตารางคอร์ส = `courseSummaries` (badge จาก `status`)

---

## F. จอ/เส้นทาง/สถานะ (FE work packages)

| WP | จอ/Route | Data | สถานะที่ต้องมี (loading/error/empty) | หมายเหตุ |
|---|---|---|---|---|
| **FE-1** (frontend-developer) | **`/live/:sessionId/join`** (ใต้ `PublicLayout`, `canActivate:[authGuard]` พร้อม returnUrl, **CSR + `robots: noindex`, ไม่ SSR**) | `POST …/join` ใน `afterNextRender()` **ครั้งเดียว** (ไม่เรียกใน resolver/ctor — กัน prefetch/crawler) | `checking` (spinner + `aria-live=polite`) · `redirecting` (200: `window.location.replace(meetUrl)` หลัง ~600ms + ปุ่มสำรอง `<a target=_blank rel="noopener noreferrer">`) · `notOpen` (นับถอยหลังถึง `opensAtUtc` โดยใช้ offset จาก `serverTimeUtc`; ถึงเวลาแล้ว auto-retry) · `ended` (+ปุ่ม "ดูย้อนหลัง" ถ้ามี `recordingEpisodeId` → `/learn/{courseSlug}/{episodeId}`) · `cancelled` · `roomNotReady` (503: retry ทุก 30 วิ สูงสุด 10 ครั้ง) · `notFoundOrNoAccess` (404: ปุ่ม "ไปที่คอร์สของฉัน/ดูคอร์ส") · `rateLimited` (429) · `error` | ข้อความเตือน "อย่าส่งต่อลิงก์ — ใช้ได้เฉพาะบัญชีของคุณ" + (ถ้า `hasAttendedAnySession=false` จาก my-sessions ที่เรียกมาก่อนหน้า หรือแสดงเสมอ) ข้อความนโยบายคืนเงิน (§D) · **ห้ามเก็บ meetUrl นอก signal** · ห้ามปุ่ม copy |
| **FE-2** (frontend-developer) | แท็บ **"สอนสด"** ใน `/learn/:slug` (`?tab=live`) — แสดงเฉพาะเมื่อคอร์ส `deliveryFormat !== 'OnDemand'` และมี enrollment | `GET my-sessions` | skeleton · empty ("ยังไม่มีตารางสอน") · error + retry | การ์ดคาบ: ชื่อ, วัน-เวลาไทย (pipe), badge `displayState` (**ข้อความ+ไอคอน ไม่ใช้สีอย่างเดียว**), ปุ่ม "เข้าห้องเรียน" **enabled เฉพาะ `canJoin`** → `router.navigate(['/live', id, 'join'])` · Upcoming: "เปิดห้อง {joinOpensAtUtc}" + นับถอยหลัง; **refetch เมื่อถึง `joinOpensAtUtc`/`startsAtUtc`/`endsAtUtc` (setTimeout ตาม offset จาก `serverTimeUtc`) — ห้ามคำนวณ displayState เอง** · "เพิ่มลงปฏิทิน" (B.4) · Ended: `hasRecording` → "ดูย้อนหลัง" `/learn/{slug}/{recordingEpisodeId}` มิฉะนั้น "รอบันทึก" · Cancelled: ขีดฆ่า + `cancelReason` · `roomReady=false` → "ห้องยังไม่พร้อม" (ปุ่มยัง enabled ตาม canJoin; server ตอบ 503 ได้) |
| **FE-3** (frontend-developer) | `/my-courses` + การ์ดเรียนต่อ (P2-26/P11-23) | `GET me/sessions/upcoming?limit=10` | ไม่มีคาบ → ไม่แสดง chip | chip "คาบถัดไป: {วันสั้น} {เวลา}" ต่อคอร์ส + ปุ่มเข้าห้องเมื่อ `canJoin` |
| **FE-4** (frontend-developer / Antigravity) | component ใหม่ `live-schedule-panel` ใน builder (`course-builder-page` เกิน 200 บรรทัดอยู่แล้ว — **ห้ามยัดเพิ่มในไฟล์เดิม**) | `GET live-schedule` + `GET …/meetings` (poll ทุก 10 วิ **เฉพาะเมื่อมี `needsAction==='Waiting'`**) + `GET google/status` | ครบ 3 state | เลือก format · ตารางคาบ (เพิ่ม/แก้/ยกเลิก — กรอก Asia/Bangkok → UTC `Z`) · คอลัมน์สถานะห้อง (badge ข้อความ) + ปุ่มตาม `needsAction`: `PasteLink`→ช่องวางลิงก์ · `ReconnectGoogle`→ปุ่มเชื่อมใหม่ · `Retry`→resync · แบนเนอร์ "เชื่อม Google Calendar" (`configured && !connected`) · **คำเตือนคงที่:** เพดานผู้เข้าร่วม/เวลา Meet ขึ้นกับแผน Google ของบัญชีตัวเอง (Gmail ฟรี ~100 คน/60 นาที) + แนะนำตั้ง "ต้อง admit ก่อนเข้า" · toggle attendee sync (C.4) · ช่อง deadline/seat (มีแล้ว P11-11) · การ์ดคาบที่จบ → "อัปโหลดบันทึก" (C.3) · แสดง `live.meetings_not_ready` ตอน submit review โดยชี้คาบใน `sessionIds` |
| **FE-5** (frontend-developer) | **`/instructor/live-settings`** (ใต้ instructor layout) | `GET google/status` + query จาก callback (A.3) | ครบ | สถานะการเชื่อม (อีเมล/วันที่), ปุ่มเชื่อม/เชื่อมใหม่/ยกเลิก (confirm), คำอธิบายสิทธิ์ที่ขอ (ปฏิทินของคุณ — แพลตฟอร์มสร้าง/แก้/ลบเฉพาะ event ของคาบสอน), ข้อความเมื่อ `configured=false` ("ผู้ดูแลยังไม่เปิดฟีเจอร์ — วางลิงก์เองได้") |
| **FE-6** (frontend-developer) | **`/instructor/sessions`** (รายการ) และ **`/instructor/sessions/:id`** | B.5 | ครบ + roster paginate | detail: ลิงก์ห้อง (`meetUrl` — ผู้สอน **มีปุ่ม copy และ "เปิดห้อง"** ได้ เพราะเป็นเจ้าของ), สถานะห้อง+ปุ่มตาม `needsAction`, ตัวเลข enrolled/expected/joined, roster (กรอง joined/notJoined, `emailMasked`), ปุ่มยกเลิกคาบ (C.2) และอัปโหลดบันทึก (C.3) |
| **FE-7** (Antigravity ได้) | `instructor-dashboard-page` | §E | ครบ | เลิก `searchCourses` สาธารณะและค่า hardcode ทั้งหมด (ดู P11-10 §0) · i18n ครบ |
| **FE-8** (Antigravity ได้) | course detail + catalog (P11-21) | C.5 | — | ตาม `P11-01` §4 + `HYBRID_LIVE.md` §6 |
| **FE-9** (frontend-developer) | order history refund (§D) | — | — | จัดการ 409 `refund.live_attended` |

**ร่วมทุกจอ:** standalone + `OnPush` + signals · `*ApiService` ใน `features/live/data/` (component ห้ามเรียก `HttpClient` ตรง) + model TS ตามไฟล์นี้ · i18n namespace **`live.*`** ครบทั้ง `th.json` + `en.json` (กลุ่ม: `live.tab.*`, `live.join.*`, `live.errors.*` (ตาราง §0), `live.status.*` (displayState/syncStatus/needsAction), `live.instructor.*`, `live.google.*`, `live.roster.*`, `live.refundNotice`) · วัน-เวลาแสดงผ่าน pipe `Asia/Bangkok` เท่านั้น · a11y: badge สถานะใช้ข้อความ+ไอคอน, ปุ่ม icon-only มี `aria-label`, นับถอยหลังไม่ประกาศทุกวินาที (ใช้ `aria-live=off` + ประกาศที่จุดเปลี่ยนสถานะ), focus ไปหัวเรื่องหลังเปลี่ยนสถานะหน้า join · ทุกหน้าใหม่ CSR+noindex (ยกเว้น course detail/catalog) · unit test: `JoinPage` ทุก state (รวม **ไม่เรียก API ใน ctor**), live tab (ปุ่มเข้าห้อง disabled เมื่อ `canJoin=false`), `needsAction`→UI

**ตัวอย่างลำดับ error ของ `/live/:sid/join`:** 401 → interceptor/authGuard พาไป `/login?returnUrl=/live/<id>/join` · หลัง login กลับมาเรียก POST ใหม่เอง

## Changelog
(ยังไม่มี revision หลัง FROZEN)
