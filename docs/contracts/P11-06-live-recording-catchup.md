# Contract: P11-06 แนบบันทึกการสอน (recording) เข้าคาบสด → กลายเป็น `COURSE_EPISODE` ธรรมดา = catch-up

Status: FROZEN · วันที่: 2026-10-06 · Module: `Siri.Modules.Catalog` (vertical slice: `Features/AttachSessionRecording/{Command,Validator,Handler,Response}.cs`, entity UPPERCASE class + PascalCase property) · **ไม่แตะ Learning/Media/Live เลย**

ผู้ลงมือที่แนะนำ: **Claude `backend-developer`** — แก้โครงคอร์สที่ Published แล้ว + ผูก asset วิดีโอที่เป็นของผู้สอน (ownership) + ผลต่อสิทธิ์ดูย้อนหลัง; โค้ดเล็ก (1 handler + 1 action) ไม่ต้อง DATABASE/DESIGN_DATABASE (**ไม่มี schema delta** — `COURSE_LIVE_SESSIONS.RecordingEpisodeId` มีแล้วจาก P11-01)

**Dependency:** P11-01 (domain `COURSE.AttachSessionRecording`) · flow อัปโหลดเดิม (TUS → Bunny webhook → asset `Ready`) ใช้ซ้ำ 100% · JSON/TS → `P11-FE-live-dto-appendix.md` §C

---

## 0. ข้อเท็จจริงจากโค้ด (อ่าน method body)

| # | ข้อเท็จจริง | ผล |
|---|---|---|
| F1 | `AttachEpisodeMediaHandler`/`CreateCourseEpisodeHandler` เดิม **จำกัดเฉพาะคอร์ส `Draft`/`Rejected`** (`NotDraftError`) — แต่ที่ระดับ domain `COURSE.AddSection/AddEpisode/AttachEpisodeMedia` **ไม่มี guard สถานะ** (มีเฉพาะ `RemoveSection/RemoveEpisode` ที่บล็อก Published/Archived) | handler ใหม่ **ต้องไม่ reuse handler เดิม** — เรียก domain method ตรง ๆ เพื่อให้แนบบันทึกบนคอร์ส **Published** ได้ (นี่คือ use case หลัก) · การ *เพิ่ม* episode ในคอร์สที่ขายแล้วไม่ทำลาย progress ของผู้เรียน (ต่างจากการลบ) |
| F2 | `COURSE.AttachSessionRecording(sessionId, episodeId)` มีแล้ว: ไม่เช็ค status คาบ (Cancelled แนบได้), เรียกซ้ำ overwrite ได้, ปฏิเสธ episode ที่ไม่ใช่ของคอร์ส (`InvalidOperationException`) | ใช้ตามนี้ |
| F3 | `COURSE_EPISODE.AttachMedia` ตั้ง `Status=Ready` เอง; `PlaybackSessionService` เช็ค `MEDIA_ASSET.STATUS == Ready` ที่ฝั่ง Media ตอนออก URL | catch-up ใช้ได้ทันทีที่ asset Ready |
| F4 | `CanUserAccessEpisodeAsync` = `HasActiveEnrollmentAsync(userId, courseId)` ของคอร์สที่ episode สังกัด (`LearningAccessContract.cs`) | **latecomer ที่ซื้อหลังคาบจบเข้าดู episode ได้เลยโดยไม่มี logic ใหม่** — เทสต์พิสูจน์ |
| F5 | `IMediaAssetContract.GetAssetSummaryAsync` คืน `(Id, UploadedByUserId, Status string, DurationSeconds)` | ใช้ตรวจ ownership + `Ready` + duration > 0 |
| F6 | `COURSE.AddEpisode` เรียก `RecalculateEpisodeStats()` (EpisodeCount/TotalDurationSeconds) ให้แล้ว | ไม่ต้องคำนวณเอง |
| F7 | `LiveSessionsController` (P11-02) ใช้ `[EnableRateLimiting("default")]` ระดับ class = fixed-window ทั้งแอป (เทรป X-29) | action ใหม่นี้ใส่ `[EnableRateLimiting("live-user")]` ระดับ action (override) — **ไม่แก้ endpoint เดิมของ P11-02 (DONE)** แต่บันทึกเป็นข้อสังเกตให้ integrator-qa |

---

## 1. Scope
**ใน:** `POST /api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}/recording` (บน `LiveSessionsController`) + handler + validator + response + unit/integration test
**นอก:** ดึง recording จาก Google Drive อัตโนมัติ (Q13.5 = ไม่ทำ v1) · ลบ/ถอดบันทึก (ใช้ "แทนที่ในที่เดิม" แทน) · AI transcript/chapter (P12 เลื่อน) · FE (ปุ่ม "อัปโหลดบันทึก" ของ P11-20/P11-25 — ใช้ `MediaApiService.uploadVideoFile` + `watchAsset` เดิมจน asset `Ready` แล้วค่อยเรียก endpoint นี้)

## 2. Schema delta: **none**

## 3. API contract

`POST /api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}/recording` · `[Authorize(Policy = InstructorOnly)]` (class-level เดิม) · `[EnableRateLimiting("live-user")]` (action) · ownership แบบ Catalog (`dbContext.InstructorProfiles()` เทียบ `course.InstructorId` — แพทเทิร์นเดียวกับ `CreateLiveSessionHandler`; **404 ไม่พบ / 403 ไม่ใช่เจ้าของ แยกกัน**)

Request `AttachSessionRecordingCommand(Guid? MediaAssetId, Guid? EpisodeId, Guid? SectionId, string? EpisodeTitle)`
```json
{ "mediaAssetId": "…", "sectionId": null, "episodeTitle": "บันทึก: Kickoff" }     // โหมด A: asset ใหม่
{ "episodeId": "…" }                                                                // โหมด B: ผูก episode ที่มีอยู่แล้ว
```
Validator (FluentValidation): **ต้องส่งอย่างใดอย่างหนึ่งเท่านั้น** `MediaAssetId` XOR `EpisodeId` · `SectionId` ใช้ได้เฉพาะโหมด A · `EpisodeTitle` ≤200 (trim; ว่าง → default)

Response `200 OK` `LiveSessionRecordingResponse(Guid SessionId, Guid RecordingEpisodeId, Guid SectionId, string EpisodeTitle, int DurationSeconds, Guid MediaAssetId, bool ReplacedExisting)`

### 3.1 Handler (`AttachSessionRecordingHandler.HandleAsync(userId, courseId, sessionId, command, ct)`) — ลำดับบังคับ
```
1. course = Courses().Include(Sections→Episodes).Include(LiveSessions).FirstOrDefault(id)        null → 404
2. ownership (instructor profile)                                                                  ไม่ใช่เจ้าของ → 403
3. course.Status == Archived                                                                       → 409 "คอร์สที่เก็บถาวรแล้วแก้ไขไม่ได้"
4. session = course.LiveSessions.FirstOrDefault(id)                                                null → 404
5. clock.UtcNow < session.StartsAtUtc                                                              → 409 reason "live.session_not_started"   (คาบ Cancelled แนบได้ ตาม domain)
6. โหมด A (MediaAssetId):
     asset = IMediaAssetContract.GetAssetSummaryAsync(id)
       null → 404 · asset.UploadedByUserId != userId → 403 · asset.Status != "Ready" หรือ DurationSeconds !> 0 → 409 reason "live.recording_asset_not_ready"
     ถ้าไม่ใช่การแทนที่: asset ถูกใช้โดย episode อื่นในคอร์สนี้แล้ว → 409 reason "live.recording_asset_in_use"
     ถ้า session.RecordingEpisodeId != null และ episode นั้นยังอยู่ในคอร์ส:
         episode.MediaAssetId == asset.Id → 200 idempotent (ReplacedExisting=false, ไม่แก้อะไร)
         ไม่เท่า → course.AttachEpisodeMedia(existingEpisodeId, asset.Id, duration) [แทนที่ในที่เดิม: คง episode/ลำดับ/ผู้เรียนที่เคยเข้า]
                   + ถ้าส่ง EpisodeTitle → episode.UpdateDetails(title, episode.Description) · ReplacedExisting=true
     ไม่งั้น (สร้างใหม่):
         section = SectionId ? หาในคอร์ส (ไม่พบ → 404) : (section ชื่อ "บันทึกการสอนสด" ที่มีอยู่ ?? course.AddSection("บันทึกการสอนสด"))
         episode = course.AddEpisode(section.Id, title ?? $"บันทึก: {session.Title}" (ตัด ≤200), description: null, isFreePreview: false)
         course.AttachEpisodeMedia(episode.Id, asset.Id, duration)
         course.AttachSessionRecording(sessionId, episode.Id)
7. โหมด B (EpisodeId):
     episode ต้องอยู่ในคอร์ส (ไม่พบ → 404) · episode.MediaAssetId == null → 409 reason "live.recording_episode_has_no_media"
     episode ถูกใช้เป็น recording ของคาบอื่นในคอร์สนี้แล้ว → 409 reason "live.recording_episode_in_use"
     course.AttachSessionRecording(sessionId, episodeId)
8. SaveChangesAsync (ครั้งเดียว) → ถ้า course.Status == Published → outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag)  (public detail แสดง hasRecording/จำนวนบทเรียน)
9. คืน LiveSessionRecordingResponse
```
- exception จาก domain (`InvalidOperationException`/`ArgumentException`) → map เป็น `DomainError.Conflict`/`Validation` (ห้ามหลุดเป็น 500) · ห้ามเรียก `AttachEpisodeMediaHandler`/`CreateCourseEpisodeHandler`
- ชื่อ section default เป็น **ข้อมูลเนื้อหาที่ผู้สอนแก้ชื่อได้** (ไม่ใช่ข้อความ UI) — ค่าคงที่ `LiveRecordingDefaults.SectionTitle = "บันทึกการสอนสด"`
- **ไม่แตะ Learning**: ไม่สร้าง/แก้ enrollment/progress; สิทธิ์ดูย้อนหลัง = entitlement ของ episode ที่มีอยู่ (F4)
- ไม่ hook AI/transcribe (P12 เลื่อนทั้งเฟส — Q12)

## 4. Frontend notes (รายละเอียดใน appendix §C)
ปุ่ม "อัปโหลดบันทึก" อยู่ที่การ์ดคาบที่จบแล้วใน builder panel และหน้า `/instructor/sessions/:id`: อัปโหลด TUS → รอ asset `Ready` → `POST …/recording {mediaAssetId}` → refetch `live-schedule` · ผู้เรียนเห็น "ดูย้อนหลัง" จาก `my-sessions` (`hasRecording`, `recordingEpisodeId`) ลิงก์ `/learn/{courseSlug}/{recordingEpisodeId}` · แสดง 409 `live.recording_asset_not_ready` เป็น "วิดีโอยังประมวลผลไม่เสร็จ ลองใหม่อีกครั้ง"

## 5. Integration checklist
- สร้างคาบ → จบ (ใช้ `FakeClock`/ตั้งเวลาคาบอดีตตรงใน DB ผ่าน domain) → แนบ asset `Ready` ของผู้สอน → มี section "บันทึกการสอนสด" 1 อัน + episode 1 อัน; คาบที่ 2 แนบ → **ใช้ section เดิม** (ไม่สร้างซ้ำ)
- **คอร์ส Published**: แนบสำเร็จ (200), `EpisodeCount`/`TotalDurationSeconds` อัปเดต, `GET /api/catalog/courses/{slug}` เห็น `hasRecording=true` ทันที (output cache evict — black-box เหมือน `CourseReadModelTests`)
- **latecomer (security-critical):** ผู้เรียน enroll **หลัง** คาบจบ → `PlaybackSessionService.GetByEpisodeIdAsync` ของ `recordingEpisodeId` ได้ URL (ใช้ fake `IVideoProvider` แบบเทสต์ Media เดิม); ผู้ที่ไม่ได้ enroll → ถูกปฏิเสธ (403/404 ตาม Media) — ยืนยันว่าไม่มี logic entitlement ใหม่
- แทนที่: แนบ asset B ทับ asset A → episode id เดิม, `MediaAssetId` เปลี่ยน, `ReplacedExisting=true`; แนบ A ซ้ำ → 200 idempotent ไม่เปลี่ยน
- ปฏิเสธ: asset ของผู้สอนคนอื่น 403 · asset ไม่ `Ready` 409 · คาบอนาคต 409 `live.session_not_started` · คอร์ส Archived 409 · เจ้าของผิด 403 · คอร์ส/คาบ/section ไม่พบ 404 · XOR ผิด 400 · asset ใช้ซ้ำ 409 · episode ไม่มี media 409 · episode ที่เป็น recording ของคาบอื่น 409
- โหมด B ผูก episode ที่มีอยู่ได้ · คาบ Cancelled แนบได้
- unit test handler ทุก branch ด้วย fake `IMediaAssetContract`/in-memory graph ของ aggregate (ไม่ใช่ InMemory EF)

## 6. Work package
| WP | ใคร | ทำอะไร | Acceptance |
|---|---|---|---|
| **A** | `backend-developer` | `Features/AttachSessionRecording/*` + DI (`CatalogModule`) + action บน `LiveSessionsController` (`[EnableRateLimiting("live-user")]`) + unit test | build 0 warning; unit เขียว; handler เดิมไม่ถูกแก้ |
| **B** | `backend-developer` | integration tests §5 | เขียนครบ (รายงานว่ายังไม่เคยรันถ้าไม่มี Docker) |
Dependency เพิ่ม: policy `live-user` (P11-03 §3.5) ต้องมีก่อน — ถ้า P11-03 ยังไม่ merge ให้ใช้ชั่วคราวไม่ได้ (อย่าตกไปใช้ "default") → รอ P11-03 WP-E

## Changelog
(ยังไม่มี revision หลัง FROZEN)
