# Contract: P11-04 Invite reconcile + ICS ผ่าน EmailOutbox + reminder 24 ชม./1 ชม. (ผู้เรียนและผู้สอน) + Google attendee sync (opt-in)

Status: FROZEN · วันที่: 2026-10-06 · Module: `Siri.Modules.Live` (Repository+Service, UPPERCASE) + `Siri.Modules.Notification` (**ตามแพทเทิร์นเดิมของโมดูล:** class `EMAIL_OUTBOX_MESSAGE` UPPERCASE แต่ property PascalCase, vertical-slice/Infrastructure) + `Siri.Integrations.Email` + `Siri.Modules.Catalog` (1 คอลัมน์ + 1 endpoint)

ผู้ลงมือที่แนะนำ: **Claude — `DATABASE` (WP-A) → `backend-developer` (WP-B..G)** — แตะ Contracts ข้ามโมดูล (Notification), อีเมลถึงผู้เรียนจริง, ข้อมูลส่วนบุคคล (อีเมลผู้เรียนส่งไป Google เมื่อเปิด attendee sync) → ตรงเกณฑ์ charter · ไม่ต้อง DESIGN_DATABASE (3 คอลัมน์/ตาราง รายละเอียดครบใน §2)

**Dependency:** P11-03 ต้องเสร็จก่อน (Live module, `LiveSessionContext`, `SESSION_MEETINGS.ICS_SEQUENCE`, `ISessionMeetingRepository`, `InstructorGoogleAccountService.TryGetAccessToken`) · อ่านคู่กับ `P11-03-live-module-google-meetings.md` (§3.5 config, §4 contracts, §5 Google interface)

---

## 0. ข้อเท็จจริงจากโค้ด + สิ่งที่ **เปลี่ยนจาก `TASKS.md` P11-04** (contract นี้ชนะ)

| # | ข้อเท็จจริง | ผล |
|---|---|---|
| F1 | `IEmailOutbox` มีเมธอดเดียว `Enqueue(to, subject, bodyHtml, templateKey)` (`Notification/Contracts/IEmailOutbox.cs`), `EMAIL_OUTBOX_MESSAGE` ไม่มีที่เก็บ calendar, `EmailMessage(ToAddress, Subject, HtmlBody)` (`Integrations.Email/IEmailSender.cs`), `SmtpEmailSender` สร้าง `BodyBuilder{HtmlBody}` เท่านั้น | เพิ่ม calendar part ครบ 4 ชั้น (§3) — ทุกอย่าง additive |
| F2 | `EmailOutboxSenderJob` ส่งทีละ **100 ฉบับ/นาที** (`BatchSize=100`) และเป็นคอขวดจริงของ "ภายใน ~2 นาที" | staging ของ invite ≤ 300 ฉบับ/run; การส่งจริงของคนซื้อพร้อมกัน >200 คนอาจเลยเวลา — **ไม่แก้ BatchSize ใน task นี้** (บันทึกเป็น config follow-up) |
| F3 | `ILearningAccessContract.GetActiveEnrolledUserIdsAsync(courseId)` มีแล้วและใช้ rule เดียวกับ `HasActiveEnrollmentAsync` (`LearningAccessContract.cs`) · `IUserContactReader.GetUsersContactInfoAsync` (batch) มีแล้ว | ใช้ของเดิมทั้งคู่ ไม่แตะ Learning/Identity |
| F4 | `USER_NOTIFICATION` (`NOTIFY.USER_NOTIFICATIONS`) มี แต่ **ไม่มี contract ให้โมดูลอื่น stage** (`AnnouncementDispatchJob` ทำภายในโมดูลเอง) | เพิ่ม `IUserNotificationOutbox` (§3.2) |
| F5 | `AnnouncementDispatchJob` มีแพทเทิร์น try/catch ต่อก้อนงาน + detach entity ที่ค้าง tracker เมื่อล้ม | job ของ Live ใช้แพทเทิร์นเดียวกัน (`ChangeTracker.Clear()` ต่อ course) |
| F6 | ไม่มี Ical.Net/ไลบรารี ICS ในโปรเจ็ค | **เขียน `IcsCalendarBuilder` เอง ห้ามเพิ่ม NuGet** (ทดสอบด้วย unfold+assert สตริง) |

**เปลี่ยนจาก TASKS.md:**
1. **อีเมลวันซื้อ = 1 ฉบับต่อ (ผู้เรียน, คอร์ส)** รวมทุกคาบอนาคตเป็นตาราง + `.ics` ไฟล์เดียว `METHOD:PUBLISH` หลาย `VEVENT` (เดิมวางแบบ 1 ฉบับ/คาบ ⇒ คอร์ส 20 คาบ = 20 อีเมลต่อคนซื้อ และกินโควตา outbox) · การ **เปลี่ยนเวลา/ยกเลิกรายคาบ** ยังเป็น `METHOD:REQUEST`/`CANCEL` รายคาบตาม Q11 · reminder 24 ชม. แนบ `REQUEST` รายคาบ
2. **DTSTART/DTEND เป็น UTC (`…Z`) ไม่ใช้ `TZID=Asia/Bangkok`** — instant เดียวกัน ไม่ต้องมี `VTIMEZONE` (ไคลเอนต์เข้มอย่าง Outlook ปฏิเสธ TZID ที่ไม่มี VTIMEZONE); ใส่ `X-WR-TIMEZONE:Asia/Bangkok` เป็น hint; ข้อความอีเมลแสดงเวลาไทยเสมอ
3. **ไม่มี raw meetUrl ในอีเมล/ICS ใด ๆ (รวมของผู้สอน)** — `LOCATION`/`URL`/ลิงก์ในเนื้อหา = `{Live:PublicBaseUrl}/live/{sessionId}/join` (ผู้สอนเป็นเจ้าของคอร์สเข้าห้องผ่านลิงก์เดียวกันได้) — เทสต์ยืนยัน
4. **invite ไม่รอห้องประชุมพร้อม** (HYBRID_LIVE เดิมให้เชิญเฉพาะ meeting `Synced`) — เพราะลิงก์แพลตฟอร์มคงที่ ผู้เรียนได้ตารางทันที; ห้องไม่พร้อม → join คืน 503 + แจ้งผู้สอน (§5.4)
5. **ผู้สอนเป็นผู้รับด้วย** (`ROLE=Instructor`): batch ตอนสร้างคาบ · ICS ให้เฉพาะเมื่อห้องเป็น Manual/Logging (ถ้า `GoogleMeet` event อยู่บนปฏิทิน Google ของผู้สอนเองแล้ว — ส่ง ICS ซ้ำ = event ซ้ำ) · reminder 24 ชม./1 ชม. เหมือนผู้เรียน
6. `SESSION_INVITES` **ตัด `ENROLLMENT_ID`** (Live ไม่เห็น enrollment id ผ่าน contract และไม่จำเป็น) เพิ่ม `ROLE`, `CANCEL_SENT_AT_UTC`, `ICS_SEQUENCE_SENT` เป็น nullable

---

## 1. Scope

**อยู่ในขอบเขต:** migration 3 ตัว (§2) · Notification: `EmailCalendarPart`, `IEmailOutbox` overload, `IUserNotificationOutbox`, outbox column, sender MIME (§3) · Live: `SESSION_INVITE`, `SessionInviteService`, `IcsCalendarBuilder`, `ThaiDateText`, `LiveEmailTemplates`, jobs `live-invite-reconcile` + `live-session-reminders` (§4–§5) · Google attendee sync (§6) · Catalog `GOOGLE_ATTENDEE_SYNC_ENABLED` + `PUT …/live-settings` (§6.1)
**นอกขอบเขต:** join/my-sessions/`calendar.ics` download (P11-05) · recording (P11-06) · เพิ่ม BatchSize ของ outbox · template i18n อังกฤษ (อีเมลเป็นภาษาไทยอย่างเดียว เหมือนอีเมลเดิมทั้งระบบ) · ผู้สอนปิดการแจ้งเตือน (ไม่มี preference ใน v1)

---

## 2. Schema delta

### 2.1 Migration `AddEmailOutboxCalendarPart` — `NOTIFY.EMAIL_OUTBOX` (Notification: class UPPERCASE `EMAIL_OUTBOX_MESSAGE`, **property PascalCase**)
| C# property | column | Type | Null |
|---|---|---|---|
| `CalendarIcs` | `CALENDAR_ICS` | text | yes |
| `CalendarMethod` | `CALENDAR_METHOD` | varchar(10) | yes (`REQUEST`\|`CANCEL`\|`PUBLISH`) |
additive 2 `AddColumn` · แถวเดิมไม่กระทบ · domain: `EMAIL_OUTBOX_MESSAGE.Enqueue(…, calendarMethod, calendarIcs)` overload — validate method ∈ 3 ค่า, ICS ไม่ว่าง ≤ 200,000 ตัวอักษร, `BEGIN:VCALENDAR` นำหน้า · มี method/ics ต้องมาคู่กัน

### 2.2 Migration `AddLiveSessionInvites` — `LIVE.SESSION_INVITES` (entity `SESSION_INVITE : IAuditable`)
| Column / property | Type | Null | หมายเหตุ |
|---|---|---|---|
| `SESSION_INVITE_ID` | uuid PK | no | UUIDv7 |
| `SESSION_ID` | uuid | no | ไม่มี FK (cross-schema) |
| `USER_ID` | uuid | no | ไม่มี FK |
| `ROLE` | varchar(16) | no | enum `LiveParticipantRole` (ใช้ร่วมกับ `SESSION_JOIN_LOGS.ROLE` ของ P11-05): `Learner`\|`Instructor` |
| `STATUS` | varchar(16) | no | enum `InviteStatus`: `Pending`\|`Invited`\|`Cancelled`\|`Skipped` |
| `ICS_SEQUENCE_SENT` | int | yes | SEQUENCE ล่าสุดที่ส่ง (รวม CANCEL) — **monotonic ต่อ invite** |
| `INVITE_SENT_AT_UTC` | timestamptz(3) | yes | เวลาส่ง batch/invite แรก |
| `CANCEL_SENT_AT_UTC` | timestamptz(3) | yes | |
| `REMINDER_24H_SENT_AT_UTC` | timestamptz(3) | yes | |
| `REMINDER_1H_SENT_AT_UTC` | timestamptz(3) | yes | |
| `GOOGLE_ATTENDEE_SYNCED_AT_UTC` | timestamptz(3) | yes | §6 |
| `ERROR` | varchar(300) | yes | code สั้น (`no_contact`) — ห้ามมีอีเมล |
| `ROW_VERSION` | bytea | no | |
| audit (PascalCase) | | | |
Index: UQ `IX_SESSION_INVITES_SESSION_USER` (`SESSION_ID`,`USER_ID`) · `IX_SESSION_INVITES_USER_ID` (`USER_ID`) · `IX_SESSION_INVITES_PENDING` (`SESSION_ID`) `HasFilter("\"STATUS\" = 'Pending'")` · **ไม่มี cascade/ไม่ hard delete** (เป็นหลักฐานว่าใครถูกเชิญ/ถูกยกเลิก)
Domain: `static Create(sessionId, userId, role, clock)` (`Pending`) · `MarkInvited(seq, clock)` (`Invited`, `ICS_SEQUENCE_SENT=seq`, `INVITE_SENT_AT_UTC ??= now`, `ERROR=null`) · `MarkCancelled(seq, clock)` (`Cancelled`, `CANCEL_SENT_AT_UTC=now`, `ICS_SEQUENCE_SENT=seq`) · `Reinvite()` (`Cancelled`→`Pending`, ล้าง reminder/cancel/attendee stamps) · `MarkSkipped(code)` · `MarkReminder24h(clock)`/`MarkReminder1h(clock)` · `ResetReminders()` · `MarkAttendeeSynced(clock)`/`ClearAttendeeSynced()` · `NextSequence(int meetingSequence) => Math.Max(meetingSequence, (ICS_SEQUENCE_SENT ?? -1) + 1)` (client ปฏิเสธ SEQUENCE ที่ต่ำกว่าเดิม — ต้อง monotonic แม้หลัง CANCEL→Reinvite)

### 2.3 Migration `AddCourseGoogleAttendeeSync` — `CATALOG.COURSES`
`GOOGLE_ATTENDEE_SYNC_ENABLED bool NOT NULL DEFAULT false` (Catalog: property PascalCase `GoogleAttendeeSyncEnabled`, `private set`) · `COURSE.SetGoogleAttendeeSync(bool)` (ปฏิเสธเมื่อ `Archived`; ไม่บังคับ DeliveryFormat) · `CourseConfiguration`: `builder.Property(c => c.GoogleAttendeeSyncEnabled).HasDefaultValue(false)` · อ่านค่านี้ผ่าน `LiveScheduleReader` → `LiveSessionContext.GoogleAttendeeSyncEnabled` (แทนค่า `false` ตายตัวของ P11-03)
> ลำดับ migration: `AddEmailOutboxCalendarPart` → `AddLiveSessionInvites` → `AddCourseGoogleAttendeeSync` (ให้ DATABASE generate ตามลำดับนี้ในรอบเดียวได้) — **ห้าม apply ขึ้น DB จริง**

---

## 3. Notification + Email (additive)

### 3.1 `Notification.Contracts.IEmailOutbox`
```csharp
public sealed record EmailCalendarPart(string Method, string IcsContent);   // Method: "REQUEST" | "CANCEL" | "PUBLISH"

public interface IEmailOutbox
{
    void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey);                       // เดิม

    /// default body กัน fake/implementer เดิมพัง: calendar == null → เรียกตัว 4 พารามิเตอร์; != null → throw NotSupportedException
    void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey, EmailCalendarPart? calendar)
        => calendar is null ? Enqueue(toEmail, subject, bodyHtml, templateKey)
                            : throw new NotSupportedException("This IEmailOutbox implementation does not support calendar parts.");
}
```
`Notification.Infrastructure.EmailOutbox` override ตัว 5 พารามิเตอร์ → `EMAIL_OUTBOX_MESSAGE.Enqueue(…, calendar.Method, calendar.IcsContent)` (**ไม่ SaveChanges** — เหมือนเดิม) · unit test: ผ่าน/ปฏิเสธ method ผิด/ICS ว่าง

### 3.2 `Notification.Contracts.IUserNotificationOutbox` (ใหม่)
```csharp
public interface IUserNotificationOutbox
{
    /// stage USER_NOTIFICATION บน AppDbContext ที่ใช้อยู่ — **ไม่ SaveChanges** (แพทเทิร์นเดียวกับ IEmailOutbox). title ≤200, body ≤2000 (ตัดให้พอดี), type ≤64
    void Stage(Guid userId, string type, string title, string body, string? linkUrl);
}
```
impl `UserNotificationOutbox` (Notification.Infrastructure, Scoped) ใช้ `USER_NOTIFICATION.Create(…, IClock)` · `type` ที่ Live ใช้: `live.invite` · `live.updated` · `live.cancelled` · `live.reminder_24h` · `live.reminder_1h` · `live.meeting_alert` · `live.meeting_needs_link` · `live.meeting_failed` · `live.google_reconnect`

### 3.3 `Siri.Integrations.Email` + sender job
```csharp
public sealed record EmailCalendarContent(string Method, string IcsContent);
public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody, EmailCalendarContent? Calendar = null);
```
`EmailOutboxSenderJob`: ส่ง `new EmailMessage(…, message.CalendarMethod is null ? null : new EmailCalendarContent(message.CalendarMethod, message.CalendarIcs!))`
`SmtpEmailSender` — โครง MIME เมื่อมี calendar (ไม่มี → พฤติกรรมเดิมทุกไบต์):
```
multipart/mixed
 ├─ multipart/alternative
 │    ├─ text/html; charset=utf-8                 ← HtmlBody
 │    └─ text/calendar; charset=utf-8; method=<METHOD>   ← IcsContent (Content-Transfer-Encoding: 7bit/quoted-printable ตาม MimeKit)
 └─ text/calendar; method=<METHOD>; name="invite.ics"  (Content-Disposition: attachment; filename="invite.ics")   ← สำเนาเดียวกัน
```
สร้างด้วย `MimeKit` (`Multipart("alternative")` + `TextPart("html")` + `MimePart(new ContentType("text","calendar"){Parameters={["method"]=…, ["charset"]="utf-8"}})`; **ห้ามใช้ `BodyBuilder` สำหรับกรณีมี calendar**) · `LoggingEmailSender` ไม่เปลี่ยน
**ตรวจสอบ:** unit test สร้าง `MimeMessage` แล้วเดินต้นไม้ MIME ตรวจ `text/calendar` มี `method` parameter ถูกต้อง + attachment ชื่อ `invite.ics` + ไม่ใช้ network · **Mailpit:** dev stack (`dev.ps1` → SMTP 127.0.0.1:1025) ตรวจด้วยสายตาครั้งแรกว่า Mailpit แสดงไฟล์แนบ/ส่วน calendar (ผู้เขียน contract ยังไม่ได้รันเพราะไม่มี Docker/Mailpit ในเซสชันนี้ — **รายงานตามจริงว่าตรวจแล้วหรือยัง**)

---

## 4. Live: ICS, ข้อความ, invite reconcile

### 4.1 `IcsCalendarBuilder` (pure static, `Siri.Modules.Live.Application`) — เทสต์ได้ไม่ต้อง DB
```csharp
public enum IcsMethod { Publish, Request, Cancel }
public sealed record IcsEvent(Guid SessionId, int Sequence, string Summary, string? Description,
                              DateTime StartsAtUtc, DateTime EndsAtUtc, string JoinUrl, bool Cancelled);
public static string Build(IcsMethod method, IReadOnlyList<IcsEvent> events, string organizerEmail, string organizerName,
                           string uidHost, string? attendeeEmail, string? attendeeName, DateTime nowUtc);
```
กฎ (RFC 5545/5546):
- ขึ้นต้น `BEGIN:VCALENDAR` / `VERSION:2.0` / `PRODID:-//SIRI UpSkill//Live Sessions//TH` / `CALSCALE:GREGORIAN` / `METHOD:{PUBLISH|REQUEST|CANCEL}` / `X-WR-TIMEZONE:Asia/Bangkok`
- ต่อ `VEVENT`: `UID:{SessionId:N}@{uidHost}` (**คงที่ตลอดชีวิตคาบ ทุก method**) · `DTSTAMP:{nowUtc:yyyyMMddTHHmmssZ}` · `SEQUENCE:{n}` · `DTSTART/DTEND` UTC `…Z` · `SUMMARY` · `DESCRIPTION` (มีข้อความ "เข้าห้องผ่านแพลตฟอร์มก่อนเวลา {n} นาที" + `JoinUrl` + คำอธิบายคาบ plain) · `LOCATION:ออนไลน์ — {JoinUrl}` · `URL:{JoinUrl}` · `ORGANIZER;CN={name}:mailto:{organizerEmail}` · `TRANSP:OPAQUE` · `STATUS:CONFIRMED` (`CANCELLED` เมื่อ `Cancelled`) · REQUEST/CANCEL: `ATTENDEE;CN={name};ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:{email}` (**PUBLISH ไม่มี ATTENDEE**) · ไม่ใช่ CANCEL: `VALARM` `TRIGGER:-PT15M` `ACTION:DISPLAY`
- escape ค่า TEXT: `\`→`\\`, `;`→`\;`, `,`→`\,`, newline→`\n` · **folding 75 octets (UTF-8) โดยไม่ตัดกลางอักขระ** · บรรทัดคั่นด้วย CRLF ปิดท้าย CRLF
- **ห้ามมี meetUrl จริง** — ผู้เรียกส่งแต่ `JoinUrl` ของแพลตฟอร์ม (builder ไม่มี parameter อื่นที่เป็น URL)
- unit test: unfold แล้ว assert ทุก property, ภาษาไทยยาวข้าม 75 octet ไม่เสีย, `Cancelled=true` → `STATUS:CANCELLED`, PUBLISH ไม่มี ATTENDEE, หลาย VEVENT, UID ตรงกันระหว่าง PUBLISH/REQUEST/CANCEL ของคาบเดียวกัน, `SummaryInjection` (`\r\nATTENDEE:` ในชื่อคอร์ส ต้องถูก escape/ตัดทิ้ง ไม่เกิดบรรทัดใหม่)

### 4.2 `ThaiDateText` (pure static) — ไม่พึ่ง ICU/TimeZoneInfo
`Format(DateTime startsUtc, DateTime endsUtc)` → `"พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 10:00–12:00 น. (เวลาประเทศไทย)"` — แปลงเป็น **UTC+7 คงที่** (ไทยไม่มี DST) · ชื่อวัน/เดือนจาก array ภายใน · พ.ศ. = ค.ศ.+543 · ข้ามวัน → แสดงวันที่สิ้นสุดด้วย · unit test: ขอบเที่ยงคืน UTC (ข้ามวันตามเวลาไทย), ปีอธิกสุรทิน

### 4.3 `LiveEmailTemplates` (static) — ใช้ `EmailTemplateRenderer.RenderLayout(title, contentHtml)`
ข้อความไทยทั้งหมด · **ทุก string ที่มาจากผู้ใช้/DB (ชื่อคอร์ส, ชื่อคาบ, คำอธิบาย, เหตุผลยกเลิก, ชื่อผู้รับ) ต้อง `WebUtility.HtmlEncode`** ก่อนใส่เนื้อหา · ลิงก์สร้างจาก `LiveOptions.PublicBaseUrl` เท่านั้น · ทุกฉบับมีบรรทัด "ลิงก์เข้าห้องเรียนใช้ได้เฉพาะบัญชีของคุณ — โปรดอย่าส่งต่อ" (ผู้เรียน)

| ชนิด | ผู้รับ | subject | เนื้อหา | ICS | templateKey |
|---|---|---|---|---|---|
| Welcome/Batch | ผู้เรียน (ต่อ course) | ครั้งแรก: `ยืนยันตารางเรียนสด: {คอร์ส}` · ครั้งถัดไป: `เพิ่มคาบเรียนสดใหม่: {คอร์ส}` | ตารางคาบ (วัน-เวลาไทย, ชื่อ, ลิงก์ `…/live/{sid}/join`) สูงสุด 30 แถว + "และอีก n คาบ" | **PUBLISH** หลาย VEVENT (ฉบับละ ≤50 คาบ — เกินแบ่งหลายฉบับ) | `live-invite-batch` |
| Updated | ผู้เรียน | `เปลี่ยนแปลงเวลาเรียนสด: {คาบ} — {คอร์ส}` | เวลาใหม่ + ลิงก์ | **REQUEST** SEQUENCE ใหม่ | `live-session-updated` |
| Cancelled | ผู้เรียน | `ยกเลิกคาบเรียนสด: {คาบ} — {คอร์ส}` | เหตุผล (ถ้ามี) | **CANCEL** | `live-session-cancelled` |
| Lapsed | ผู้เรียนที่หมดสิทธิ์ | `คุณไม่ได้อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสด: {คอร์ส}` | ข้อความกลาง ไม่ระบุสาเหตุ (refund/หมดอายุ) | **CANCEL** | `live-invite-lapsed` |
| Reminder 24h | ผู้เรียน | `พรุ่งนี้มีเรียนสด: {คาบ} — {คอร์ส}` | เวลา + ลิงก์ + เปิดห้องได้ตั้งแต่กี่โมง | **REQUEST** (UID/SEQUENCE เดิม — ไคลเอนต์ dedupe) | `live-reminder-24h` |
| Reminder 1h | ผู้เรียน | `อีก 1 ชั่วโมงเริ่มเรียนสด: {คาบ}` | เวลา + ลิงก์ | ไม่แนบ | `live-reminder-1h` |
| Instructor batch | ผู้สอน | `สร้างตารางสอนสดแล้ว: {คอร์ส}` | ตาราง + ลิงก์หน้าผู้สอน `…/instructor/sessions/{sid}` + สถานะห้อง | **PUBLISH** เฉพาะเมื่อ `PROVIDER != GoogleMeet` | `live-instructor-batch` |
| Instructor update/cancel | ผู้สอน | เหมือนผู้เรียน | — | **ส่งเฉพาะ `PROVIDER != GoogleMeet`** (REQUEST/CANCEL) · `GoogleMeet` ไม่ส่ง (Google อัปเดตปฏิทินผู้สอนเอง) | `live-instructor-*` |
| Instructor reminder 24h/1h | ผู้สอน | เหมือนผู้เรียน (ลิงก์ผู้สอน) | | 24h แนบ REQUEST เฉพาะ `PROVIDER != GoogleMeet` | `live-reminder-*` |
| Meeting alert | ผู้สอน | `ห้องประชุมของคาบ {คาบ} ยังไม่พร้อม` | วิธีแก้ (วางลิงก์/เชื่อม Google ใหม่) + ลิงก์หน้าผู้สอน | ไม่แนบ | `live-meeting-alert` |

In-app (`IUserNotificationOutbox`): 1 รายการต่ออีเมล (ยกเว้น Instructor update/cancel ที่ไม่ส่งอีเมล) · `linkUrl` ผู้เรียน = `/learn/{courseSlug}?tab=live`, ผู้สอน = `/instructor/sessions/{sid}`

### 4.4 `SessionInviteService` + `live-invite-reconcile` (Hangfire, ทุก **2 นาที**, `[DisableConcurrentExecution(timeoutInSeconds: 110)]`, เฉพาะ Workers)
```
RunAsync(ct):
  now = clock.UtcNow
  contexts = ILiveScheduleReader.GetSessionContextsInWindowAsync(now − 3d, now + Live:InviteLookaheadDays, includeCancelled: true)
  meetings = ISessionMeetingRepository.GetBySessionIds(contexts.Select(SessionId))      // ICS_SEQUENCE + PROVIDER
  budget   = 300 emails/run
  foreach courseGroup in contexts.GroupBy(CourseId):                                     // ≤100 กลุ่ม/run
    try:
       enrolled = ILearningAccessContract.GetActiveEnrolledUserIdsAsync(courseId)
       invites  = repo.GetBySessionIds(group ids)                                        // tracked ทุกสถานะ
       owner    = group.First().InstructorUserId
       scheduledFuture = group.Where(Status==Scheduled && StartsAtUtc > now)

       // A. ค้นหาผู้ที่ควรถูกเชิญ (ผู้เรียน = enrolled − {owner}; ผู้สอน = owner)
       foreach s in scheduledFuture: foreach u in (enrolled − {owner}) ∪ {owner}:
            inv = invites[(s,u)]
            null → Add(Create(Pending, role))   | Cancelled && u ยังมีสิทธิ์ → inv.Reinvite()
       // B. หมดสิทธิ์: Learner invite ที่ u ∉ enrolled (คาบยัง Scheduled): `Invited` → ส่ง Lapsed CANCEL → MarkCancelled · `Pending` (ยังไม่เคยส่งอะไร) → MarkCancelled เงียบ ๆ ไม่ส่งอีเมล
       // C. คาบถูกยกเลิก: invite ที่ Invited และ ctx.Status==Cancelled → ส่ง Cancelled CANCEL → MarkCancelled
       // D. คาบเปลี่ยน: invite ที่ Invited, คาบ Scheduled, ICS_SEQUENCE_SENT < meeting.ICS_SEQUENCE → ส่ง Updated REQUEST → MarkInvited(newSeq) + ResetReminders()
       // E. Pending → รวมเป็น batch ต่อ (user, course) → ส่ง Welcome/Batch (ผู้เรียน) หรือ Instructor batch (ผู้สอน) → MarkInvited
       //    ผู้สอน: รอจน meeting.PROVIDER != null (provider ตัดสินแล้ว) จึงส่ง
       // F. Google attendee sync (§6) — เมื่อเปิด flag
       contacts = IUserContactReader.GetUsersContactInfoAsync(recipientUserIds)   // ไม่พบ/อีเมลว่าง → inv.MarkSkipped("no_contact")
       SaveChangesAsync   // outbox + in-app + สถานะ invite ใน transaction เดียวต่อ course
    catch (not OperationCanceled): ChangeTracker.Clear(); log (courseId, exception type — ไม่ log อีเมล); continue
```
- **sequence ที่ใส่ใน ICS** = `inv.NextSequence(meeting.ICS_SEQUENCE)`; batch ใช้ค่าเดียวกันกับทุก VEVENT ของคาบนั้น (คำนวณต่อคาบ)
- "ครั้งแรก" ของ Welcome = ผู้ใช้ **ไม่มี** invite สถานะ `Invited` ใดในคอร์สนี้มาก่อน
- ผู้เรียนที่ **ซื้อหลังคาบเริ่มไปแล้ว** ไม่ได้ invite คาบนั้น (query มองเฉพาะ `StartsAtUtc > now`) — แต่ดูย้อนหลังได้ทันที (entitlement ของ episode)
- enrollment ถูก Reactivate (ซื้อใหม่) → invite `Cancelled` ถูก `Reinvite()` อัตโนมัติ
- idempotent: รัน 2 ครั้งติดกันไม่ส่งซ้ำ (ทุกการส่งผูกกับ transition ของ `STATUS`/`ICS_SEQUENCE_SENT` ใน SaveChanges เดียวกัน; UQ(SESSION_ID,USER_ID) กันแถวซ้ำเมื่อ 2 instance ชนกัน → `DbUpdateException` unique → จับแล้วข้าม course นั้น)
- ผู้สอนไม่ใช่ผู้เรียนของคอร์สตัวเอง: ถ้า owner ปรากฏใน `enrolled` ให้ตัดออกจาก learner set

### 4.5 `live-session-reminders` (Hangfire, ทุก **5 นาที**, `[DisableConcurrentExecution(timeoutInSeconds: 280)]`)
```
contexts = GetSessionContextsInWindowAsync(now, now + 25h, includeCancelled:false) (Scheduled เท่านั้น)
invites  = Invited ของ session เหล่านั้น ที่ REMINDER_24H null หรือ REMINDER_1H null
foreach (inv, ctx): remaining = ctx.StartsAtUtc − now
   24h: remaining ∈ (1h, 24h] && REMINDER_24H null && inv.INVITE_SENT_AT_UTC <= ctx.StartsAtUtc − 24h   → ส่ง + MarkReminder24h
   1h : remaining ∈ (0, 1h]   && REMINDER_1H  null && inv.INVITE_SENT_AT_UTC <= ctx.StartsAtUtc − 1h    → ส่ง + MarkReminder1h
   (ถ้าเชิญหลังจุด 24h แล้ว → ข้าม 24h — อีเมลเชิญเป็นการแจ้งแล้ว; job ล่มจนพ้นหน้าต่าง → ข้ามเงียบ ๆ)
ผู้สอน: ถ้า meeting ของคาบนั้น !IsUsable && MEETING_ALERT_SENT_AT_UTC null && remaining <= 24h → ส่ง Meeting alert (อีเมล+in-app) + ตั้ง flag
SaveChanges ต่อ ก้อน 100 invite (outbox + flag อะตอมมิก)
```
unit test จังหวะเวลา: ขอบ 24h/1h พอดี, เชิญหลัง 24h, เลื่อนคาบ (ResetReminders) แล้วส่งใหม่ตามเวลาใหม่, คาบ Cancelled ไม่ส่ง

---

## 5. Testing checklist (integration + unit)

1. **latecomer:** ซื้อหลังคาบที่ 1 จบ → invite เฉพาะคาบอนาคต; คาบอดีตไม่มี invite
2. **purchase-day batch:** คอร์ส 5 คาบอนาคต → **1 อีเมล** `live-invite-batch` มี `.ics` PUBLISH 5 VEVENT (parse ผ่าน: UID/SEQUENCE/METHOD) + 1 in-app
3. **revoke/expire:** enrollment Revoked → รอบถัดไป invite → `Cancelled` + อีเมล CANCEL (SEQUENCE ≥ ที่ส่งไป +1)
4. **cancel คาบ:** `CancelLiveSession` → ผู้เรียนทุกคนที่ Invited ได้ CANCEL ครั้งเดียว (รันซ้ำ = ไม่ส่งเพิ่ม)
5. **reschedule:** `UpdateLiveSession` → `ICS_SEQUENCE` +1 → ผู้เรียนได้ REQUEST (SEQUENCE ใหม่) และ reminder ถูกรีเซ็ต
6. **idempotency:** รัน reconcile 3 ครั้งติด → outbox เท่าเดิม
7. **ผู้สอน:** สร้างคาบ → รอ provider ตัดสิน → `PROVIDER=Manual` ได้อีเมล+ICS PUBLISH; `PROVIDER=GoogleMeet` ได้อีเมลไม่มี ICS; reminder 24h/1h
8. **ความลับของลิงก์ (Security-critical):** ทุกแถว `EMAIL_OUTBOX` ที่สร้างในเทสต์ — `BodyHtml`, `CalendarIcs` **ต้องไม่มี** URL ห้องจริง และไม่มีโฮสต์ `meet.google.com`/`zoom.us`/`teams.microsoft.com`/`teams.live.com` เลย (เทสต์ตั้ง Manual URL จริงในข้อมูลแล้ว assert ว่าไม่รั่ว) · มี `…/live/{sid}/join` เท่านั้น
9. **XSS:** ชื่อคอร์ส/คาบ/เหตุผลยกเลิกมี `<script>`/`"><img onerror>` → `BodyHtml` ถูก encode; ICS ไม่เกิดบรรทัดใหม่แทรก
10. **ไม่มีอีเมล:** ผู้ใช้ไม่มี contact → `Skipped` ไม่ throw ไม่บล็อกคนอื่น
11. **outbox ล่ม/ล้มกลางทาง:** exception ในคอร์สหนึ่ง → คอร์สอื่นยังส่งปกติ, คอร์สที่ล้ม **ไม่มี** outbox/สถานะค้างครึ่ง ๆ (all-or-nothing ต่อ course)
12. **MIME:** unit test เดินต้นไม้ MimeMessage (§3.3) · LoggingEmailSender ไม่ crash เมื่อมี Calendar
13. **ข้อมูลจริง ไม่มี mock:** เทสต์สร้างข้อมูลผ่าน service/handler จริง ไม่ seed ตัวเลข/ชื่อปลอมลงโค้ด production

---

## 6. Google attendee sync (opt-in ต่อคอร์ส — Q11) — **WP-F แยกจากส่วนอื่น ส่งมอบหลัง WP-E ได้ ไม่บล็อก acceptance หลักของ P11-04**

เหตุผลที่ยังทำ: เป็น mitigation เดียวที่ทำให้ "ลิงก์ที่ถูกส่งต่อ" ต้องขอเข้าห้อง (ผู้เรียนที่ถูกเชิญเป็น attendee เข้าตรงได้) — ดู P11-03 §9 ข้อ 1

- เงื่อนไขทำงานต่อคาบ: `ctx.GoogleAttendeeSyncEnabled` && meeting `PROVIDER=GoogleMeet` && `SYNC_STATUS=Synced` && `PROVIDER_EVENT_ID != null` && คาบ Scheduled อนาคต && ผู้สอนมี account active
- `desired` = อีเมลของ invite `ROLE=Learner, STATUS=Invited` (เรียงตาม `INVITE_SENT_AT_UTC`) — **ถ้า `|desired| > Live:GoogleAttendeeCap` (default 150) → ปิดการ sync อัตโนมัติสำหรับคาบนั้น** (ไม่เรียก Google, แจ้งผู้สอนครั้งเดียวผ่าน in-app `live.meeting_alert`; ICS ยังส่งตามปกติ) ตาม Q11
- ทำงานเมื่อมีความต่างเท่านั้น: มี invite `Invited` ที่ `GOOGLE_ATTENDEE_SYNCED_AT_UTC null` หรือมี invite `Cancelled` ที่เคยมี stamp → เรียก `ICalendarProvider.SetAttendeesAsync(token, eventId, desired)` **ครั้งเดียวต่อคาบ** แล้ว stamp ผู้ที่รวมอยู่ / เคลียร์ stamp ของผู้ที่ถูกถอด · `sendUpdates=none` (อีเมลเชิญมาจากเราไม่ใช่ Google) · `guestsCanSeeOtherGuests=false` (ตั้งไว้แล้วตอนสร้าง event — ผู้เรียนไม่เห็นอีเมลกัน, PDPA)
- ≤20 คาบ/run · transient → ข้าม รอรอบหน้า · `google.unauthorized` → ตามกติกา P11-03 §6.1 (ไม่ retry ในรอบเดียวกัน)
- **อีเมลผู้เรียนออกไปยัง Google** ⇒ ต้องเป็น opt-in ของผู้สอนต่อคอร์สจริง (default `false`), มีข้อความกำกับในหน้า builder + ระบุในข้อตกลง/privacy policy (งาน P7 PDPA เดิม)

### 6.1 Catalog endpoint — `PUT /api/catalog/instructor/courses/{courseId}/live-settings` (บน `InstructorCoursesController`)
`[Authorize(Policy = InstructorOnly)]` · `[EnableRateLimiting("live-user")]` · ownership แบบ Catalog เดิม (**404 ไม่พบ / 403 ไม่ใช่เจ้าของ** แยกกัน — แพทเทิร์น `SetCourseEnrollmentPolicy`) · request `{ "googleAttendeeSyncEnabled": true }` → handler `SetCourseLiveSettings` (vertical slice `Features/SetCourseLiveSettings/{Command,Validator,Handler,Response}.cs`) → `course.SetGoogleAttendeeSync(bool)` → SaveChanges (ไม่ต้อง evict output cache — ไม่อยู่ใน public read model) → 200 `{ courseId, googleAttendeeSyncEnabled }` · `409` เมื่อ course `Archived` · อ่านค่าปัจจุบันผ่าน `GET …/live-schedule` (P11-05 §Catalog addendum)

---

## 7. Work packages (ลำดับบังคับ)

| WP | ใคร | ทำอะไร | ไฟล์หลัก | Acceptance |
|---|---|---|---|---|
| **A** | `DATABASE` | entity `SESSION_INVITE` + config + migration ทั้ง 3 ตัว (§2) + Catalog `COURSE.GoogleAttendeeSyncEnabled` + `EMAIL_OUTBOX_MESSAGE` overload/columns + อัปเดต `docs/DATABASE.md` | `Live/Domain/SESSION_INVITE.cs`, `Live/Infrastructure/SessionInviteConfiguration.cs`, `Catalog/Domain/COURSE.cs`+`CourseConfiguration.cs`, `Notification/Domain/EMAIL_OUTBOX_MESSAGE.cs`+config, `Persistence/Migrations/*` | อ่านทั้ง 3 migration (additive, ไม่มี shadow); identifier ≤63; architecture เขียว |
| **B** | `backend-developer` | Notification contracts + `EmailOutbox` override + `UserNotificationOutbox` + `EmailMessage.Calendar` + `SmtpEmailSender` MIME + `EmailOutboxSenderJob` | §3 | unit: outbox/MIME/Logging ผ่าน; เทสต์เดิมของ outbox/sender ผ่านโดยไม่แก้ assertion |
| **C** | `backend-developer` | `IcsCalendarBuilder`, `ThaiDateText`, `LiveEmailTemplates`, `ISessionInviteRepository`+impl, `SessionInviteService` (pure decision logic แยกเมธอดเทสต์ได้) | `Live/Application/*`, `Live/Infrastructure/SessionInviteRepository.cs` | unit ครบ §4.1–4.3 |
| **D** | `backend-developer` | `LiveInviteReconcileJob` + `LiveSessionRemindersJob` + `RecurringJobsRegistration` (`live-invite-reconcile`=`Cron.MinuteInterval(2)`, `live-session-reminders`=`Cron.MinuteInterval(5)`) + เติม in-app ให้ `InstructorAlertSender` ของ P11-03 ผ่าน `IUserNotificationOutbox` (`live.google_reconnect`/`live.meeting_needs_link`/`live.meeting_failed`) + Meeting alert ใน reminders job | `Live/Infrastructure/*Job.cs`, `Siri.Workers/RecurringJobsRegistration.cs` | unit+integration §5 ข้อ 1–11 |
| **E** | `backend-developer` | Catalog: `GoogleAttendeeSyncEnabled` mapping ใน `LiveScheduleReader`, `SetCourseLiveSettings` handler+controller action | `Catalog/Features/SetCourseLiveSettings/*`, `InstructorCoursesController.cs` | ownership 403/404, 409 Archived |
| **F** | `backend-developer` | attendee sync step (§6) | `SessionInviteService`/`LiveInviteReconcileJob` | unit: cap เกิน → ไม่เรียก Google; diff ถูก; ถอดคนที่ถูก cancel |
| **G** | `backend-developer` | integration tests §5 (ใช้ `SiriApiFactory`/Testcontainers; อีเมลตรวจจากตาราง `EMAIL_OUTBOX` ไม่ผ่าน SMTP) | `tests/Siri.IntegrationTests/LiveInvite*` | เขียนครบ; รายงานว่ายังไม่เคยรันจริงถ้าไม่มี Docker |

---

## 8. สิ่งที่เจ้าของโปรเจ็คต้องทำ / ใช้ได้โดยไม่ต้องมี Google
- **ส่วนหลัก (invite/ICS/reminder/ผู้สอน+ผู้เรียน) ทำงานครบโดยไม่ต้องมี Google credential** และอีเมล dev ออก SMTP จริงไป Mailpit (`Email__Provider=Smtp`)
- production ต้องตั้ง SMTP vendor จริง (ยังไม่ตัดสิน — `Email:Smtp:*`) — ไม่เช่นนั้น `Email:Provider=Log` จะ **ไม่ส่งอีเมลถึงใครเลย** (ระบบ invite ทำงานแต่ไม่มีใครได้รับ) — แจ้งเป็นข้อกำหนดก่อน launch
- attendee sync ต้องมี OAuth ตาม P11-03 §11 + ผู้สอนเปิด flag เอง
- ต้อง apply migration 3 ตัวนี้ขึ้น DB จริงเอง (agent ห้าม)
- ข้อสังเกต: throughput อีเมล 100 ฉบับ/นาที (`EmailOutboxSenderJob.BatchSize`) — ถ้าคาดว่ามีคนซื้อคอร์สสดพร้อมกัน >200 คนใน 2 นาที ให้แจ้งเพื่อปรับเป็น config

## Changelog
(ยังไม่มี revision หลัง FROZEN)
