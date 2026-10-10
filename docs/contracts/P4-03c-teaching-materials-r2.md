# P4-03c — เอกสารประกอบการสอน (Teaching Materials) → Cloudflare R2

> **สถานะ:** BUILT (backend) · FE ดูหัวข้อ §7 · ผู้ตัดสิน: เจ้าของโปรเจ็ค (2026-10-10: "อัปโหลดเอกสารประกอบการสอนของคอร์สวิดีโอและคอร์สสอนสด ไปที่ R2 ของ Cloudflare — ไม่ใช่ S3 ของ AWS")
> **ต่อยอดจาก:** `P4-03` / `P4-03a` / `P4-03b` (ไฟล์แนบต่อ episode — มีแค่โครง: รับ metadata แต่ไม่มีไฟล์จริง, ไม่มี storage adapter, ลิงก์ดาวน์โหลดชี้วนกลับ endpoint ตัวเอง)

## 1. ขอบเขต

| | ก่อน | หลัง |
|---|---|---|
| ที่เก็บไฟล์ | ไม่มี (`IFileStorage` เป็น interface ว่าง) | **Cloudflare R2** (private bucket) ผ่าน `R2FileStorage` |
| อัปโหลด episode attachment | `POST` JSON `{fileName, storageKey, contentType, sizeBytes, headerBytes}` — **client บอก storageKey และ magic bytes เอง** | `POST` **multipart/form-data** part `file` — server อ่านไบต์เอง ตรวจเอง สแกนเอง และ**เป็นคนตั้ง storageKey** |
| ดาวน์โหลด | `downloadUrl` = path ของ endpoint ตัวเอง (เปิดแล้วได้ JSON) | **signed URL ของ R2 อายุ 5 นาที** (ตั้งได้ 1–15 นาที) หลังเช็คสิทธิ์ |
| ไฟล์ของคาบสอนสด | ไม่มี | **ใหม่:** attachment ต่อ `COURSE_LIVE_SESSION` |
| ลบ episode/section/attachment | ลบแค่แถวใน DB | ลบ object ใน R2 ด้วย (best-effort) |

**ไม่ทำ (ตั้งใจ):** อัปโหลดตรงจาก browser ไป R2 ด้วย presigned PUT (ต้องตั้ง CORS บน bucket + ตรวจ magic bytes หลังอัปโหลด + กวาด orphan — เก็บไว้ถ้าต้องรองรับไฟล์ใหญ่กว่า 100 MB) · เอกสารระดับคอร์ส (ไม่ผูก episode/session) · preview ไฟล์ในเว็บ (ทุกไฟล์ถูกเก็บเป็น `Content-Disposition: attachment`)

## 2. การตัดสินใจ

1. **R2 ไม่ใช่ AWS S3.** ปลายทางคือ `https://{AccountId}.r2.cloudflarestorage.com` (region `auto`, path-style) ไม่มีบัญชี AWS เกี่ยวข้อง `AWSSDK.S3` (4.0.104.2) ถูกใช้เป็น *HTTP client ไลบรารี* คุยกับ API แบบ S3 ที่ R2 รองรับ (Cloudflare ไม่มี .NET SDK ของตัวเอง; เขียน SigV4 เองเสี่ยงกว่า) — ตั้ง `RequestChecksumCalculation/ResponseChecksumValidation = WHEN_REQUIRED` + `DisablePayloadSigning` เพราะ R2 ไม่รองรับ streaming SigV4 / default checksum trailer ของ SDK v4 (ยืนยันโดย unit test ที่ดักรีเควสต์จริง: `R2FileStorageTests`)
2. **Server-side multipart upload ผ่าน API** (ไม่ใช่ presigned PUT) — เหตุผล: magic-bytes/ขนาด/สแกนเป็นของ server ตาม `security.md` ("ตรวจ file upload ด้วย magic bytes"), ไม่ต้องตั้ง CORS บน bucket, ไฟล์เอกสารเล็ก (เพดาน 50 MB ตั้งได้ถึง 100 MB) — วิดีโอยังไป Bunny ด้วย TUS เหมือนเดิม ไม่เกี่ยวกับงานนี้
3. **storageKey ไม่เคยออกจาก server.** รูปแบบ `teaching-materials/courses/{courseId:N}/episodes/{episodeId:N}/{guid:N}{.ext}` และ `…/live-sessions/{sessionId:N}/{guid:N}{.ext}` — ไม่มีชื่อไฟล์ของผู้ใช้อยู่ใน key (ชื่อไฟล์เก็บใน DB แยก ใช้แสดงผล + `Content-Disposition`) endpoint JSON เดิมที่รับ `storageKey` จากผู้เรียก **ถูกถอดแล้ว** (เป็นช่องให้ชี้ไปที่ object อื่นใน bucket แล้วขอ signed URL ได้)
4. **Content-Type ที่เก็บ/เสิร์ฟ มาจากนามสกุลฝั่ง server** (`application/pdf` ฯลฯ) ไม่ใช่ที่ client ส่ง (client ส่งมาแค่ใช้เช็คความสอดคล้อง)
5. **สิทธิ์อ่าน**
   - episode: เหมือนเดิม (`EpisodeAccessHelper` — admin · เจ้าของคอร์ส · episode `IsFreePreview` · มี enrollment active ไม่หมดอายุ)
   - live session: **admin · instructor เจ้าของคอร์ส · ผู้เรียนที่ enrollment Active และไม่หมดอายุ** (ไม่มี free preview — ไม่ login = 401)
   - ไม่มีสิทธิ์ = **404 ตัวเดียวกับ "ไม่มีอยู่จริง"** (กันเดา id) ยกเว้นฝั่งเขียนที่ตอบ 403 ตามธรรมเนียมเดิมของ P4-03
6. **สิทธิ์เขียน (upload/delete):** admin หรือ instructor เจ้าของคอร์ส (เช็คผ่าน `ICatalogPriceContract.IsInstructorOwnerOf{Episode,Course}Async`) ทำ**ก่อน**อ่านหรือสแกนไบต์ใด ๆ
7. **Virus scan (Q9 — เจ้าของโปรเจ็คตัดสิน 2026-10-10: ข้ามขั้นสแกนไปก่อน):** ยังไม่มี engine → `appsettings.json` ส่งมาเป็น `Attachments:VirusScan:Mode=Disabled` = รับไฟล์โดยไม่สแกนและ log เตือนทุกไฟล์ · `ProductionConfigurationGuard` ไม่บล็อกโหมดนี้แล้ว (เดิมบล็อก) · ค่า default ในโค้ดยังเป็น `Required` (ตั้ง `Attachments__VirusScan__Mode=Required` เพื่อกลับไปปฏิเสธทุกการอัปโหลดด้วย **503 `attachment.virus_scanner_not_configured`**) · เมื่อลงทะเบียน engine จริงโหมดนี้ไม่มีผลอีก · ความเสี่ยงที่เจ้าของรับไว้: ไฟล์ที่ผู้สอนอัปโหลดไม่ผ่านการสแกนมัลแวร์ ด่านที่ยังเหลือ: allow-list นามสกุล+MIME, magic bytes, ปฏิเสธ executable header, อัปโหลดได้เฉพาะเจ้าของคอร์ส/แอดมิน, bucket private, `Content-Disposition: attachment`, ลิงก์ signed อายุสั้น
8. **Bucket ที่ไม่ได้ตั้งค่า = 503 `storage.provider_not_configured`** (ไม่มี fallback ลงดิสก์/หน่วยความจำ) และ host ยัง boot ปกติ (ไม่เพิ่มเข้า `ProductionConfigurationGuard` โดยตั้งใจ — ไฟล์แนบเป็นฟีเจอร์เสริม ไม่ควรทำให้ deploy ทั้งระบบล้ม)

## 3. Schema delta

ตารางใหม่ `CATALOG.LIVE_SESSION_ATTACHMENTS` (migration `AddLiveSessionAttachments` — **ยังไม่ apply ขึ้น DB จริง**)

| คอลัมน์ | ชนิด | หมายเหตุ |
|---|---|---|
| `ID` | uuid PK | UUIDv7 |
| `SESSION_ID` | uuid NOT NULL | FK → `CATALOG.COURSE_LIVE_SESSIONS(ID)` **ON DELETE CASCADE** (session ไม่เคยถูกลบ มีแต่ Cancelled — cascade ทำงานเฉพาะกรณี hard-delete คอร์สนอก soft-delete path) |
| `FILE_NAME` | varchar(255) | ชื่อที่ sanitize แล้ว ใช้แสดง |
| `STORAGE_KEY` | varchar(500) | key ใน R2 — **ไม่ส่งออก API** |
| `CONTENT_TYPE` | varchar(100) | canonical จากนามสกุล |
| `SIZE_BYTES` | bigint | |
| `CREATED_AT_UTC/CREATED_BY/UPDATED_AT_UTC/UPDATED_BY` | `IAuditable` | |

Index `IX_LIVE_SESSION_ATTACHMENTS_SESSION_ID_CREATED_AT_UTC (SESSION_ID, CREATED_AT_UTC)` · ไม่มี soft-delete (ไฟล์ล้วน ไม่ใช่เงิน/สิทธิ์เรียน) · ตาราง `CATALOG.EPISODE_ATTACHMENTS` ไม่เปลี่ยน

## 4. API (ทั้งหมดใต้ `/api/catalog`)

DTO เป็น camelCase, enum เป็น string, เวลาเป็น UTC ISO-8601 · error = RFC 9457 ProblemDetails มี `errorCode` ใน extensions

### 4.1 Episode attachments (route เดิม)

| Method | Path | Auth | ผลลัพธ์ |
|---|---|---|---|
| GET | `/episodes/{episodeId}/attachments` | anonymous ได้ (ตามกฎ episode) | `200 EpisodeAttachment[]` · 404 |
| GET | `/episodes/{episodeId}/attachments/{attachmentId}/download` | anonymous ได้ (ตามกฎ episode) | `200 EpisodeAttachmentDownload` · 404 · 503 |
| **POST** | `/episodes/{episodeId}/attachments` | login (instructor เจ้าของ / admin) | `201 EpisodeAttachment` · 400 · 401 · 403 · 404 · 409 · 503 — **multipart/form-data, part ชื่อ `file`** |
| DELETE | `/episodes/{episodeId}/attachments/{attachmentId}` | login (เจ้าของ / admin) | `204` · 401 · 403 · 404 |

```ts
interface EpisodeAttachment {          // = EpisodeAttachmentResponse
  id: string; episodeId: string;
  fileName: string; contentType: string; sizeBytes: number;
  createdAtUtc: string;
}
interface EpisodeAttachmentDownload {  // = EpisodeAttachmentDownloadResponse  (เพิ่ม expiresAtUtc)
  id: string; episodeId: string;
  fileName: string; contentType: string; sizeBytes: number;
  downloadUrl: string;                 // signed R2 URL, https, ใช้ทันที ห้ามเก็บ
  expiresAtUtc: string;
}
```

### 4.2 Live-session attachments (ใหม่ — **ทุก endpoint ต้อง login**)

| Method | Path | ใครได้ | ผลลัพธ์ |
|---|---|---|---|
| GET | `/live-sessions/{sessionId}/attachments` | admin · เจ้าของคอร์ส · ผู้เรียน enrollment active | `200 LiveSessionAttachment[]` · 401 · 404 |
| GET | `/live-sessions/{sessionId}/attachments/{attachmentId}/download` | เหมือนข้างบน | `200 LiveSessionAttachmentDownload` · 401 · 404 · 503 |
| POST | `/live-sessions/{sessionId}/attachments` | admin · เจ้าของคอร์ส | `201 LiveSessionAttachment` · 400 · 401 · 403 · 404 · 409 (session ยกเลิกแล้ว / ไฟล์ครบจำนวน) · 503 — multipart part `file` |
| DELETE | `/live-sessions/{sessionId}/attachments/{attachmentId}` | admin · เจ้าของคอร์ส | `204` · 401 · 403 · 404 |

```ts
interface LiveSessionAttachment {          // = LiveSessionAttachmentResponse
  id: string; sessionId: string;
  fileName: string; contentType: string; sizeBytes: number;
  createdAtUtc: string;
}
interface LiveSessionAttachmentDownload {  // = LiveSessionAttachmentDownloadResponse
  id: string; sessionId: string;
  fileName: string; contentType: string; sizeBytes: number;
  downloadUrl: string; expiresAtUtc: string;
}
```
เพิ่มไฟล์ให้ session ที่จบแล้วได้ (สไลด์หลังเรียน) — ปฏิเสธเฉพาะ session ที่ `Cancelled`

### 4.3 กติกาไฟล์ (server เป็นผู้ตัดสิน — FE เช็คซ้ำเพื่อ UX เท่านั้น)

- นามสกุลที่รับ: `.pdf .zip .7z .gz .tar.gz .docx .xlsx .pptx .txt .csv .json .md .png .jpg .jpeg` (บล็อก `.exe .dll .bat .sh .ps1 .msi .jar …` และไฟล์ที่ไม่อยู่ในรายการ)
- เนื้อไฟล์ต้องตรง magic bytes ของนามสกุล (PDF=`%PDF`, ZIP-family=`PK`, PNG/JPEG/7z/GZ…) และปฏิเสธ header ของ executable (MZ, ELF, Mach-O, Java class) ทุกกรณี · ไฟล์ข้อความห้ามมี NUL byte
- ขนาด: > 0 และ ≤ `Catalog:Attachments:MaxFileSizeBytes` (default **50 MB**, ตั้งได้ไม่เกิน **100 MB**) · จำนวนต่อ episode/session ≤ `MaxAttachmentsPerParent` (default **30**)
- ชื่อไฟล์ถูก sanitize (ตัด path, อักขระควบคุม และ `< > : " | ? *`, ย่อให้ ≤ 255 โดยคงนามสกุล) — ชื่อที่ได้กลับมาใน response อาจต่างจากที่ส่ง

### 4.4 `errorCode` ที่ FE ต้องจัดการ

| status | `errorCode` | ความหมาย / ข้อความแนะนำ |
|---|---|---|
| 400 | `validation` | ชนิด/ขนาด/เนื้อหาไฟล์ไม่ผ่าน (`title` เป็นข้อความไทยพร้อมแสดง) ; ไม่มี part `file` → ValidationProblemDetails |
| 401 | — | ต้อง login |
| 403 | `forbidden` | ไม่ใช่เจ้าของคอร์ส |
| 404 | `not_found` | ไม่พบ / ไม่มีสิทธิ์ |
| 409 | `conflict` | session ถูกยกเลิก หรือไฟล์ครบจำนวนสูงสุด |
| 413 | — | เกินเพดาน request ของ server/proxy (ไม่มี ProblemDetails เสมอไป) |
| 503 | `attachment.virus_scanner_not_configured` | ยังไม่เปิดระบบตรวจไวรัส → "ขณะนี้ยังอัปโหลดเอกสารไม่ได้ กรุณาติดต่อผู้ดูแล" |
| 503 | `storage.provider_not_configured` | ยังไม่ตั้งค่าที่เก็บไฟล์ (R2) → ข้อความเดียวกัน |
| 503 | `unavailable` | R2 ใช้ไม่ได้ชั่วคราว → "ลองใหม่อีกครั้ง" |
| 429 | — | rate limit `default` (100/นาที ทั้งระบบ) บน POST/DELETE |

## 5. Configuration

```jsonc
"Storage": { "R2": { "AccountId": "", "AccessKeyId": "", "SecretAccessKey": "", "BucketName": "", "Endpoint": "" } },
"Catalog": { "Attachments": { "MaxFileSizeBytes": 52428800, "DownloadUrlTtlSeconds": 300, "MaxAttachmentsPerParent": 30 } },
"Attachments": { "VirusScan": { "Mode": "Required" } }
```
dev → `dotnet user-secrets set "Storage:R2:AccountId" "…"` (+ `AccessKeyId`, `SecretAccessKey`, `BucketName`) · prod → env `Storage__R2__AccountId` ฯลฯ · ใช้ R2 API token สิทธิ์ *Object Read & Write* ผูกกับ bucket นี้ bucket เดียว · **bucket ต้องเป็น private** (ไม่เปิด public access / ไม่ผูก custom domain — presigned URL ใช้กับ custom domain ไม่ได้)

## 6. Ops ที่ต้องทำก่อนใช้จริง (เจ้าของโปรเจ็ค)

1. สร้าง R2 bucket (private) + API token แล้วตั้ง 4 ค่าข้างบน
2. **reverse proxy ต้องยอม body ≥ ขนาดไฟล์สูงสุด:** nginx `client_max_body_size 110m;` สำหรับ `/api/` (default ของ nginx = 1 MB → ผู้สอนจะเจอ 413) ; ถ้ามี Caddy ด้านหน้า ตรวจ `request_body max_size`
3. apply migration `AddLiveSessionAttachments` (ตามด้วย migration ค้างอื่น ๆ — ดู `dotnet ef migrations list`)
4. (ไม่บังคับแล้ว) Q9 virus scan engine — เจ้าของเลือกข้ามไปก่อน ดู §2 ข้อ 7 ; เมื่อมี engine จริงให้ลงทะเบียน `IAttachmentVirusScanner` ตัวใหม่ (ไม่ต้องแก้ handler)
5. (ไม่บังคับ) R2 lifecycle rule: ลบ object ใน prefix `teaching-materials/` ที่ไม่มีแถวใน DB — ตอนนี้ orphan เกิดได้เฉพาะกรณี R2 ลบไม่สำเร็จหลังลบแถวแล้ว หรือ hard-delete คอร์สนอก soft-delete path

## 7. งาน Frontend (`siri_upskill_ui`)

1. **Models + ApiService** ใน `features/` ที่เหมาะ (instructor/course-builder หรือ `features/catalog/data`): mirror §4 ทั้ง 2 ชุด (episode + live session) — upload ใช้ `FormData` + `HttpClient.post` (อย่าตั้ง `Content-Type` เอง) และ `reportProgress: true, observe: 'events'` เพื่อแสดงความคืบหน้า ; ห้ามเรียก `HttpClient` จาก component ตรง (`frontend.md`)
   > **แก้หลังทำจริง (2026-10-10):** แอปใช้ `provideHttpClient(withFetch())` และ `FetchBackend` ของ Angular **ไม่เคยส่ง `UploadProgress`** (ถ้าขอ `reportUploadProgress` จะ throw) — แถบ progress จะค้างที่ 0% ตลอดการอัปโหลด 50 MB จึงทำ `UPLOAD_HTTP_CLIENT` (`src/app/core/http/upload-http-client.ts`): `HttpClient` ที่ใช้ `withXhr()` ใน child injector ใช้ interceptor ชุดเดียวกับแอป (base URL / bearer / `ApiError` + 401 refresh) เฉพาะการอัปโหลดเท่านั้น คำขออื่นยังเป็น fetch ตามเดิม
2. **Course builder (ผู้สอน):** ต่อ episode แต่ละแถวมีปุ่ม/พับเปิด "เอกสารประกอบ" → panel แสดงรายการ (ชื่อ · ขนาด · ลบ) + ปุ่มเลือกไฟล์/ลากวาง + progress + error ตาม §4.4 ; ต่อ live session แต่ละแถวใน `live-schedule-panel` ทำเหมือนกัน (ซ่อนปุ่มอัปโหลดเมื่อ session Cancelled) ควรทำเป็น component เดียว reuse ได้ (`kind: 'episode' | 'session'`)
3. **ผู้เรียน:** (ก) `episode-attachments` เดิม — แก้การดาวน์โหลดให้ใช้ `downloadUrl` จริง (anchor ชั่วคราว/`location.assign` ไม่ใช่ `window.open` หลัง async เพราะโดน popup blocker) + จัดการ 503/404 ; (ข) แท็บ live (`live-tab` / session card) แสดงเอกสารของแต่ละคาบสำหรับผู้เรียนที่ enroll แล้ว (เรียก endpoint §4.2) — ซ่อนทั้งส่วนถ้า 404/ว่าง
4. i18n th/en ทุกข้อความ (ไม่ hardcode) · a11y: input file มี label มองเห็น, ปุ่มไอคอนมี `aria-label`, สถานะอัปโหลดประกาศด้วย `aria-live` · ใช้ signal/OnPush/standalone ตาม `frontend.md` · loading/error/empty ครบ
5. เทสต์: unit ของ service + component (อัปโหลดสำเร็จ/ล้มเหลวตาม errorCode, ลบ, ดาวน์โหลด) — ห้าม assert ด้วย CSS class

## 8. Tests ฝั่ง backend

- `tests/Siri.UnitTests/Storage/*` — `R2FileStorage` (ดัก HTTP จริง: path-style, SigV4 region `auto`, ไม่มี checksum trailer/aws-chunked, `Content-Disposition`, presign), options, header builder
- `tests/Siri.UnitTests/Catalog/TeachingMaterialStorageTests.cs`, `AttachmentFileNamesTests.cs`, `EpisodeAttachmentTests.cs`
- `tests/Siri.IntegrationTests/TeachingMaterialsIntegrationTests.cs` — HTTP จริงบน `SiriApiFactory` (สิทธิ์ทุกบทบาท, IDOR matrix, ปฏิเสธไฟล์ปลอม, เพดานขนาด/จำนวน, 503 ทั้ง 3 แบบ, ลบแล้ว object หาย, ลบ episode/section แล้ว object หาย, course ที่ถูก soft-delete)
