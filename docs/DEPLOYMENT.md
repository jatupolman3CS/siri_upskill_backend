# Deployment — Contabo VPS (self-hosted)

> Current integration setup (2026-09-08): use the PostgreSQL 17 Compose files and commands in [README.md](../README.md). The SQL Server deployment notes below are historical and do not describe the current database provider. The local stack uses `docker-compose.dev.yml`; production uses `docker-compose.prod.yml` with an explicit migration service.

> **ตัดสินใจแล้ว (Q3):** deploy บน **Contabo VPS** ที่มีอยู่แล้ว ไม่ใช้ cloud managed service
> วิดีโอและ bandwidth หนักอยู่ที่ Bunny Stream อยู่แล้ว VPS จึงรับแค่ API + SSR + DB

## ✅ สถานะจริงของ MSSQL (ตรวจสอบแล้ว 2026-08-17)

มี SQL Server รันอยู่บน Contabo แล้ว: `217.217.253.122,1433` (เครื่อง `vmi3262952`)
**Edition = Developer** (ตรวจจาก `SERVERPROPERTY('Edition')` โดยตรง) ใช้ร่วมกับ database โปรเจ็คอื่นของเจ้าของระบบอยู่แล้ว: `MRMOOMOVIE`, `SIRIEDUMARKET`, `SIRISTUDIOPHOTO`

Database `SIRIUPSKILL` มีเตรียมไว้แล้ว (เดิมว่างเปล่า) — ตั้งค่าให้แล้ว:
- Collation → `Thai_100_CI_AS_SC_UTF8`
- Login แอปเฉพาะ `siriupskill_app` สิทธิ์ `db_owner` จำกัดเฉพาะ `SIRIUPSKILL` เท่านั้น (ตรวจแล้วว่าเข้า DB อื่นบนเครื่องเดียวกันไม่ได้) — **แอปจะไม่ใช้ `sa` เชื่อมต่อ**

## ⚠️ 2 เรื่องที่ยังต้องจัดการ

### 1. License ของ SQL Server — Developer ใช้ production ไม่ได้

| Edition | ค่าใช้จ่าย | ข้อจำกัด | ใช้ production ได้ไหม |
|---------|-----------|---------|---------------------|
| **Developer** ← **ตัวที่ใช้อยู่ตอนนี้** | ฟรี | ไม่จำกัดฟีเจอร์ | ❌ **ห้ามใช้ production เด็ดขาด** (ผิด license) |
| **Express** | ฟรี | **10 GB ต่อ database**, RAM buffer pool ~1.4 GB, 4 cores | ✅ ได้ แต่ต้องจัดการขนาด |
| **Standard** | มีค่า license ต่อ core | 128 GB RAM | ✅ |

ใช้ Developer edition ระหว่างพัฒนา (P0–P6) ได้เต็มที่ ไม่มีปัญหาอะไร — **แต่ต้องตัดสินก่อนเข้า P7 (hardening & launch)** ว่าจะ:
(a) ซื้อ license Standard ให้เครื่องเดียวกัน หรือ
(b) ย้ายไป Express + แยก `WatchEvents` เป็น database ต่างหาก + purge 90 วัน (ดูแผนเดิมด้านล่าง) หรือ
(c) ย้ายไป PostgreSQL (ไม่มีทั้งค่า license และเพดานขนาด — แต่เอกสารทั้งหมดตอนนี้ยึดตาม MSSQL)

**แผนสำรองถ้าไปทาง Express:**
- แยก `WatchEvents` (ตัวที่โตเร็วที่สุด) ไปเป็น **database คนละตัว** — Express นับ 10 GB *ต่อ database* ไม่ใช่ต่อ instance
- purge `WatchEvents` ที่เก่ากว่า 90 วัน หลัง rollup ลง `analytics.*` แล้ว
- เก็บภาพสลิป/ไฟล์แนบไว้ที่ storage ไม่ใช่ใน DB
- ตั้ง alert ที่ 7 GB → เตรียมย้ายไป Standard หรือ PostgreSQL

### 2. Port 1433 เปิดออกอินเทอร์เน็ตสาธารณะ 🔴

เครื่องนี้ต่อได้จากอินเทอร์เน็ตทั่วไปโดยตรง (ทดสอบแล้วจากเครื่อง dev คนละที่กับ VPS) และ**ใช้ร่วมกับโปรเจ็คอื่น** — ถ้า `sa` รั่วหรือถูกเดารหัส เสี่ยงข้อมูลทุกโปรเจ็คบนเครื่องพร้อมกัน ไม่ใช่แค่ SIRI UpSkill
สิ่งที่ควรทำโดยเร็วที่สุด (ต้องมีสิทธิ์ SSH เข้า VPS):
- จำกัด firewall (ufw/iptables) ให้พอร์ต 1433 รับเฉพาะ IP ที่รู้จัก (เครื่อง dev, เครื่องรัน API จริง)
- เปลี่ยนรหัส `sa` เป็นระยะ และเก็บไว้ใช้เฉพาะงาน admin เท่านั้น (แอปใช้ `siriupskill_app` ตามที่ตั้งไว้แล้ว)
- พิจารณาเปลี่ยนเป็นเข้าถึงผ่าน SSH tunnel/VPN แทนการเปิดพอร์ตตรงในระยะยาว
- เมื่อ API ย้ายไป deploy บน VPS เครื่องเดียวกันจริงจัง (ตาม docker network ที่ออกแบบไว้เดิม) ให้ปิด 1433 ออกเน็ตไปเลย เหลือแค่ local docker network

## Sizing ที่แนะนำ

| ทรัพยากร | ขั้นต่ำ | แนะนำ |
|---------|--------|-------|
| vCPU | 4 | 6–8 |
| RAM | 8 GB | 16 GB (MSSQL กินเยอะ) |
| Disk | 100 GB NVMe | 200 GB+ |
| Bandwidth | — | วิดีโอไม่ผ่าน VPS (Bunny CDN จัดการ) จึงไม่ใช่คอขวด |

## Stack บนเครื่อง (Docker Compose)

```
caddy            → reverse proxy + TLS อัตโนมัติ (Let's Encrypt) + security headers + rate limit ชั้นนอก
angular-ssr      → Node container รัน Angular SSR (หน้า public)
api              → ASP.NET Core 10 Web API (Hangfire Client + Dashboard + Processing Server + Recurring Jobs เป็นค่า default — ดู "Background jobs" ด้านล่าง)
workers          → .NET 10 Worker Service (ทางเลือก: Hangfire Processing Server + Recurring Jobs แยก process)
mssql            → SQL Server (persistent volume + tempdb แยก)
redis            → cache + session/device registry + (D-23) duplicate-guard / send-throttle / unread-count cache ของ notification
kafka            → (ทางเลือก, opt-in) broker KRaft node เดียว สำหรับ pipeline อีเมล/แจ้งเตือน (D-23) — ดูหัวข้อ "Kafka" ด้านล่าง
seq              → เก็บ log แบบ query ได้
backup           → sidecar cron: dump DB → บีบอัด → เข้ารหัส → อัปขึ้น off-site
uptime-kuma      → เช็ค uptime + แจ้งเตือน
```

## Background jobs (Hangfire) — ใครรัน job server

งานเบื้องหลังทั้งหมด (ส่งอีเมล outbox, สร้างห้อง Live/Meet, invite + reminder ของคาบสด, recount enrollment, reconcile การจ่ายเงิน, ...) **ทำงานก็ต่อเมื่อมี Hangfire processing server รันอยู่เท่านั้น** — ถ้าไม่มี: ห้อง Live ค้าง "รอ", ไม่มีอีเมลยืนยัน/เชิญ/เตือนส่งออกเลย

| ทางเลือก | ตั้งค่า | เมื่อไหร่ใช้ |
|---|---|---|
| **API รัน server เอง** (default) | ไม่ต้องตั้งอะไร (`Hangfire__ServerInApi=true` เป็นค่า default) | deploy container เดียว (image `Dockerfile.api` รันแค่ `Siri.Api`) — ครบในตัว |
| **แยก Workers** | `Hangfire__ServerInApi=false` ที่ API + deploy `Siri.Workers` | อยาก scale/restart งานเบื้องหลังแยกจาก web |
| **ทั้งสองอย่างพร้อมกัน** | (ปล่อย default + deploy Workers เพิ่ม) | ปลอดภัย — ดูด้านล่าง |

- ตัวแปรเดียว: `Hangfire:ServerInApi` (env `Hangfire__ServerInApi`, bool, default **true**) — API ใช้โค้ดชุดเดียวกับ Workers เป๊ะ (`AddHangfireWorker` → queue/จำนวน worker เท่ากัน) และ schedule recurring job ชุดเดียวกัน **id เดียวกัน** (`RecurringJobIds`) ใน `Siri.Workers`. ใน environment `IntegrationTest` ค่า default คือ false (เทสต์ไม่สตาร์ท job เอง) · dev (`scripts/dev.ps1`) ตั้ง `false` เพราะรัน Workers แยกอยู่แล้ว
- **รัน API server + Workers พร้อมกันได้ ไม่ซ้ำซ้อนอันตราย**: (1) recurring job ถูกเก็บใน storage เดียว key ด้วย id → ลงทะเบียนซ้ำ = เขียนทับ definition เดิม ไม่เกิด job ที่สอง (2) scheduler ของ Hangfire ใช้ distributed lock ใน storage จึง enqueue แต่ละรอบ **ครั้งเดียว** ไม่ว่ามีกี่ server (3) job ที่เขียน/ส่งของออกนอกระบบทุกตัวมี `[DisableConcurrentExecution]` (lock อยู่ใน storage → กันข้าม server ด้วย): email outbox, live sync/invite/reminder, announcement, order expiry, stripe reconciliation, analytics, pdpa, bunny poll (4) ที่เหลือ 3 ตัวไม่มี lock โดยตั้งใจเพราะ idempotent — `course-enrollment-recount` (นับใหม่จาก enrollment จริง ทีละแถวใน transaction + row lock), `playback-anomaly-detection` (อ่านอย่างเดียว เขียนแค่ audit), `course-search-reindex` (upsert ตาม course id) — มีเทสต์ล็อกไว้ (`HangfireHostingTests`) ว่า job ใหม่ต้องมี lock หรือขึ้น allow-list พร้อมเหตุผล
- ข้อควรระวัง: ถ้า pod ของ API ถูก restart/scale-down กลางงาน Hangfire จะรัน job นั้นซ้ำเอง (at-least-once) — job ทั้งหมดออกแบบให้ idempotent อยู่แล้ว (มี idempotency key / status transition) · ตั้ง `terminationGracePeriodSeconds` ≥ 30 วินาที
- **ตรวจว่าระบบ Live ใช้งานได้จริงไหม**: `GET /api/live/admin/status` (Admin เท่านั้น) บอก job server รันอยู่ไหม, recurring job ลงทะเบียน/รันล่าสุดเมื่อไหร่, อีเมล (provider/ค้าง/ล้มเหลว), Live provider/Google/public URL, จำนวนห้องที่ค้าง และ `warnings` เป็นรหัสคงที่ (`no_job_server`, `email_unconfigured`, `outbox_backlog`, `public_base_url_not_https`, `google_not_configured`, `meetings_stuck_pending`, ...) · Production จะ log สรุปรหัสเดียวกันนี้ **บรรทัดเดียว** (level Warning) ~90 วินาทีหลัง start — ค้นใน log ด้วยคำว่า `Live system check`

## Admin คนแรกของ DB ว่าง (Owner bootstrap) + runbook เปิดระบบ Live

- ตั้ง env `Identity__Bootstrap__OwnerEmails__0` (`__1`, `__2` ... เพิ่มได้) เป็นอีเมลเจ้าของระบบ (**ไม่ใช่ secret**) · เจ้าของ**สมัครบัญชีเองตามปกติ + ยืนยันอีเมล** แล้ว `OwnerBootstrapService` (`src/Siri.Api/Bootstrap/`, ตรวจทุก 30 วินาทีจนทุกคนครบ แล้วหยุด) จะให้ role ทั้งสี่ (Learner/Instructor/Admin/SuperAdmin) + โปรไฟล์ผู้สอนที่อนุมัติแล้ว — เฉพาะบัญชีที่ **มีอยู่แล้วและ Active** (ไม่สร้างบัญชี ไม่แตะรหัสผ่าน ไม่ให้บัญชีที่ยังไม่ยืนยันอีเมล) · ไม่ต้อง restart · role มีผลตอน login ครั้งถัดไป · ไม่ตั้งค่า = ไม่ทำอะไรเลย · ใช้ได้ทุก environment รวม Production (ต่างจาก `--seed` ที่ห้ามใน Production)
- ขั้นตอนเปิดระบบคอร์สสอนสดทั้งหมด (env ที่ต้องมี, Google Cloud, เช็คครั้งแรกบนเบราว์เซอร์, ตารางแก้ปัญหา): **[`docs/runbooks/live-course-go-live.md`](runbooks/live-course-go-live.md)**

Subdomain: `siriupskill.com` (SSR) · `api.siriupskill.com` · `admin.siriupskill.com` · `staging.siriupskill.com`

⚠️ **ตั้งค่า `caddy` ให้ forward header บอก scheme จริงไปหา `angular-ssr`** (`X-Forwarded-Proto`/`Forwarded` — Caddy's `reverse_proxy` ทำให้อัตโนมัติอยู่แล้วโดย default ปกติ ไม่ต้องเพิ่ม directive พิเศษ แค่ต้อง**ยืนยัน**ตอน deploy จริง) แล้วตั้ง env `NG_TRUST_PROXY_HEADERS=true` ให้ container `angular-ssr` — ถ้าไม่ทำ Angular SSR (`SeoService`, task P1-24) จะเข้าใจผิดว่า request เป็น `http://` เสมอ (เช็คจาก `socket.encrypted` ตรง ๆ แทน) เพราะ Caddy→Node เป็น plain HTTP ภายใน docker network แม้ฝั่งผู้ใช้เข้าผ่าน `https://` จริง — canonical/OG URL ในหน้า SSR จะผิดเป็น `http://...` เงียบ ๆ ถ้าลืมข้อนี้

## Meilisearch (ค้นหาคอร์ส + ชื่อผู้สอน — D-22)

ตั้งค่าเป็น env ให้ **ทั้ง API และ Workers** (ทั้งสองโหลด `AddCatalogModule`; Workers เป็นคนรัน job `course-search-reindex`):

| env | ความหมาย |
|---|---|
| `Meilisearch__Url` | base URL ของ Meilisearch เช่น `http://host:7700` (ว่าง = ปิดฟีเจอร์ ใช้ `pg_trgm`) |
| `Meilisearch__ApiKey` | **secret** — key ที่ต้องมีสิทธิ์ `search`, `documents.add`, `documents.get`, `documents.delete`, `indexes.create`, `indexes.get`, `settings.update`, `tasks.get` (master key ใช้ได้) · ว่างหรือ `CHANGE_ME` = ปิดฟีเจอร์ |
| `Meilisearch__DocumentsIndexUid` | uid ของ index (default `siriupskill_documents`; ใช้ได้เฉพาะ `A-Z a-z 0-9 - _`) |
| `Meilisearch__Enabled` | `false` = ปิดทันทีทั้งที่ตั้งค่าครบ (default `true`) |

- ตั้งแล้ว restart → ตอน start API จะสร้าง index + settings และเติมให้เองถ้า index ว่าง (log: `Meilisearch index is ready…` / `initial reindex indexed N`) ไม่ต้องสั่งอะไรเพิ่ม · ถ้าไม่ได้ผลดู log บรรทัด `Meilisearch course search is off (...)` จะบอกเหตุผล (ไม่มี URL / key ยังเป็น `CHANGE_ME` / ถูกปิด)
- ตรวจสถานะ: `GET /api/catalog/admin/search/status` (Admin) → `enabled`, `reachable`, `indexedCourses` เทียบ `publishedCoursesInDatabase` · สั่งสร้างใหม่ทั้งหมด: `POST /api/catalog/admin/search/reindex`
- **ความปลอดภัย:** ถ้า URL เป็น `http://` ไป public IP ตัว bearer key วิ่งแบบไม่เข้ารหัส → ใช้ `https://` (reverse proxy หน้า Meilisearch) หรือ private network, แนะนำสร้าง key แยกที่จำกัด action ตามตารางด้านบนแทน master key และอย่า commit ลงไฟล์ใด ๆ (`.env_prd`/Jenkins credentials เท่านั้น)
- Meilisearch ล่มไม่ทำให้เว็บล่ม — ค้นหาตกกลับไป PostgreSQL อัตโนมัติ (ดู `ARCHITECTURE.md` § 6)
- ทดสอบ client กับ server จริงด้วยมือ (ไม่ต้อง boot API): ตั้ง env `SIRI_MEILISEARCH_LIVE_URL` + `SIRI_MEILISEARCH_LIVE_KEY` (+ `SIRI_MEILISEARCH_LIVE_INDEX` ถ้าไม่ใช่ `siriupskill_documents`) แล้ว `dotnet test tests/Siri.UnitTests --filter MeilisearchLiveSmokeTests --logger "console;verbosity=detailed"` — เขียน document ทดสอบด้วย id สุ่มลง index แล้วลบออกหมด ไม่แตะข้อมูลอื่น (ถ้าไม่ตั้ง env เทสต์ถูกข้ามเอง)

## Cloudflare R2 (เอกสารประกอบการสอน — P4-03c · ไม่ตั้ง = อัปโหลด/ดาวน์โหลดไฟล์แนบตอบ 503)

เก็บไฟล์แนบของ episode และของคาบสอนสด (สไลด์ ใบงาน ฯลฯ) ใน **R2 bucket แบบ private** — ไม่ใช่ AWS S3 (ใช้ API แบบ S3 ที่ R2 รองรับผ่านไลบรารี `AWSSDK.S3` เป็น HTTP client เท่านั้น) วิดีโอยังอยู่ที่ Bunny ตามเดิม ตั้งเป็น env ให้ **API** (Workers ไม่ใช้):

| env | ความหมาย |
|---|---|
| `Storage__R2__AccountId` | Cloudflare account id (ใช้ประกอบ endpoint `https://{id}.r2.cloudflarestorage.com`) |
| `Storage__R2__AccessKeyId` | **secret** — access key ของ R2 API token (สิทธิ์ *Object Read & Write* ผูกกับ bucket นี้ bucket เดียว) |
| `Storage__R2__SecretAccessKey` | **secret** — secret ของ token เดียวกัน |
| `Storage__R2__BucketName` | ชื่อ bucket (ต้อง **private**: ไม่เปิด public access, ไม่ผูก custom domain — presigned URL ใช้กับ custom domain ไม่ได้) |
| `Storage__R2__Endpoint` | ไม่บังคับ — override endpoint (เช่น jurisdiction EU `https://{id}.eu.r2.cloudflarestorage.com`) |
| `Catalog__Attachments__MaxFileSizeBytes` / `DownloadUrlTtlSeconds` / `MaxAttachmentsPerParent` | เพดานไฟล์ (default 50 MB, สูงสุด 100 MB) / อายุลิงก์ดาวน์โหลด (default 300 วิ) / จำนวนไฟล์ต่อ episode หรือ session (default 30) |

- **reverse proxy ต้องยอม request body ใหญ่พอ** — อัปโหลดวิ่งผ่าน API (multipart) ไม่ได้ตรงไป R2: nginx ตั้ง `client_max_body_size 110m;` ที่ location `/api/` (default ของ nginx คือ **1 MB** → ผู้สอนจะเจอ 413) ; ถ้ามี Caddy อยู่หน้า ตรวจ `request_body { max_size 110MB }`
- ไม่ต้องตั้ง CORS บน bucket (browser ไม่คุยกับ R2 ด้วย XHR — อัปโหลดผ่าน API, ดาวน์โหลดเป็นการ navigate ไป presigned URL ที่ตอบ `Content-Disposition: attachment`)
- **ไม่มี virus scan (Q9 — เจ้าของโปรเจ็คตัดสิน 2026-10-10 ให้ข้ามขั้นสแกนไปก่อน):** `appsettings.json` ส่งมาเป็น `Attachments:VirusScan:Mode=Disabled` ไฟล์ที่ผ่านการตรวจชนิด/MIME/magic bytes จะถูกรับโดยไม่สแกน และ log เตือน `Attachment accepted WITHOUT a virus scan` ทุกไฟล์ · `ProductionConfigurationGuard` ไม่บล็อกโหมดนี้แล้ว · อยากกลับไปบังคับสแกน: ตั้ง `Attachments__VirusScan__Mode=Required` (อัปโหลดจะตอบ 503 `attachment.virus_scanner_not_configured` จนกว่าจะลงทะเบียน engine จริง) · ด่านอื่นยังอยู่ครบ: allow-list นามสกุล+MIME, magic bytes, ปฏิเสธ header ของ executable, อัปโหลดได้เฉพาะเจ้าของคอร์ส/แอดมิน, bucket private, เสิร์ฟด้วย `Content-Disposition: attachment` + ลิงก์ signed อายุสั้น
- ไม่ตั้ง R2 แล้ว host ยัง boot ปกติ (ตอบ 503 `storage.provider_not_configured` เฉพาะ endpoint ไฟล์แนบ) — ไม่อยู่ใน `ProductionConfigurationGuard` โดยตั้งใจ
- orphan: ลบแถว/episode/section แล้วระบบลบ object ใน R2 ให้ (best-effort — พลาดแล้ว log warning ไว้) ถ้าอยาก sweep เพิ่มใช้ R2 lifecycle/สคริปต์เทียบ prefix `teaching-materials/` กับ `CATALOG.EPISODE_ATTACHMENTS`/`LIVE_SESSION_ATTACHMENTS`

## Kafka (pipeline อีเมล/แจ้งเตือน — D-23 · opt-in, ปิดอยู่เป็น default)

ไม่เปิด = ไม่ต้องทำอะไร: `Notification__Delivery__Transport` default เป็น `Database` (Hangfire job `email-outbox-send` ส่งอีเมลเหมือนเดิมทุกอย่าง) · สถาปัตยกรรมเต็ม: `ARCHITECTURE.md` § 9

> ✅ **pipeline ผ่าน end-to-end กับ Kafka broker จริงแล้ว (2026-10-09):** `NotificationKafkaPipelineIntegrationTests` 11/11 รันกับ broker บน dev cluster ของเจ้าของ (SASL/PLAIN) + PostgreSQL 18 + Redis-protocol server (Garnet) จริงบนเครื่อง dev — relay → Kafka → consumer → SMTP (recording sender), retry, exhausted, duplicate, poison→DLQ, stale-queued, broker ล่ม, in-app → unread-count cache
> ⚠️ **ที่ยังไม่ได้ทดสอบ:** ชุดคำสั่ง `docker run` ด้านล่าง (เขียนจาก image doc ของ `apache/kafka` — เครื่อง dev ไม่มี Docker) และ path Testcontainers (`apache/kafka:4.2.2`) ที่จะรันใน CI — ลองบน staging ก่อนเปิด production · รันกับ broker อื่นของเรา: ตั้ง env `SIRI_IT_KAFKA_LIVE_BOOTSTRAP` (+ `_PROTOCOL`/`_MECHANISM`/`_USERNAME`/`_PASSWORD`) หรือ `SIRI_IT_KAFKA` (loopback ไม่มี auth) — ดู `KafkaFixture`; test สร้าง/ลบเฉพาะ topic และ group ที่ขึ้นต้น `siriupskill-it-`

**1) Broker (single node, KRaft) — container ใน network `siri-net` เหมือน `siri_postgres`, ไม่ publish port ออกเน็ต**

```bash
docker run -d --name siri_kafka --restart unless-stopped --network siri-net --memory 1g \
  -v siri_kafka_data:/var/lib/kafka/data \
  -e KAFKA_NODE_ID=1 \
  -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://siri_kafka:9092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@siri_kafka:9093 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 \
  -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 \
  -e KAFKA_AUTO_CREATE_TOPICS_ENABLE=false \
  -e KAFKA_HEAP_OPTS="-Xms512m -Xmx512m" \
  apache/kafka:4.2.2
```
- `AUTO_CREATE_TOPICS_ENABLE=false` เพราะแอปสร้าง topic เองตอน start (`KafkaTopicProvisioner`, partition/retention ตาม `Notification:Delivery:*`) · เปลี่ยนเวอร์ชัน image ให้เปลี่ยนที่ `tests/Siri.IntegrationTests/Fixtures/KafkaFixture.cs` ด้วย
- ต้องการ **~1 GB RAM** (JVM) บน VPS ที่ใช้ร่วมกับ Postgres/Redis/แอป — เช็ค `docker stats` ก่อนตัดสินใจเปิด · ข้อมูล = record เล็ก ๆ เก็บ 7 วัน (ไม่กี่ MB ต่อวัน)
- **ห้าม publish port 9092/9093 ออกเน็ต** (`-p`) — listener เป็น `PLAINTEXT` ไม่เข้ารหัสไม่ยืนยันตัวตน ปลอดภัยเพราะอยู่ใน `siri-net` เท่านั้น ถ้าจะใช้ cluster ภายนอก/managed ให้ใช้ `Kafka__SecurityProtocol=SaslSsl` (ดูตารางด้านล่าง) — `SaslPlaintext` ส่งรหัสผ่าน SASL แบบไม่เข้ารหัส

**2) ตั้งค่าแอป — ทุก host ที่รัน Hangfire server (API เมื่อ `Hangfire__ServerInApi=true` + Workers ถ้ามี) ต้องตั้ง "เหมือนกัน"**

| env | ความหมาย |
|---|---|
| `Notification__Delivery__Transport` | `Database` (default) \| `Kafka` |
| `Kafka__BootstrapServers` | `siri_kafka:9092` (คั่นหลาย broker ด้วย `,`) |
| `Kafka__AllowPlaintext` | `true` = ยืนยันว่า broker อยู่ใน private network (production ปฏิเสธ `Plaintext`/`SaslPlaintext` ถ้าไม่ตั้ง) |
| `Kafka__TopicPrefix` | namespace ของ topic/consumer group — production `siriupskill`, staging `siriupskill-staging` (ห้ามซ้ำกันถ้าใช้ cluster เดียว) |
| `Kafka__ReplicationFactor` | จำนวนสำเนา = 1 บน single node |
| `Kafka__SecurityProtocol` / `SaslMechanism` / `SaslUsername` / `SaslPassword` / `SslCaLocation` | สำหรับ broker ที่มี TLS/SASL — `SaslUsername`/`SaslPassword` เป็น **secret** (env/user-secrets เท่านั้น) |
| `Notification__Delivery__MaxEmailsPerMinute` | งบส่งต่อนาทีร่วมทุก consumer (0 = ไม่จำกัด) — ตั้งให้ต่ำกว่าเพดานของ SMTP provider |
| `Notification__Delivery__EmailConsumerInstances`, `__Partitions`, `__QueuedStaleAfterMinutes` (15, ขั้นต่ำ 10 — ตั้งให้เกินเวลาที่ burst ใหญ่สุดใช้ระบาย: ผู้รับ ÷ อัตราส่ง), `__RelayPollIntervalMs` (1000), `__RelayBatchSize` (100), `__UnreadCountCacheSeconds` (30) | ปรับจูน (default ใช้ได้เลย) |

ค่าทั้งหมดข้างบนใส่ใน `.env` (Development/QA) หรือ `.env_prd` (Production) ได้เลย — `DotEnvLoader` อ่านเป็น config ชั้นถัดจาก `appsettings*.json` (ชนะ appsettings แต่แพ้ user-secrets และ env var จริง) ไม่ต้องมี key อื่นเพิ่ม · key ที่ **ไม่มีผล** (ไม่มีโค้ดอ่าน): `Kafka__ConsumerGroupId` (consumer group คำนวณจาก `Kafka__TopicPrefix`), `Redis__InstanceName` · อ่านค่า Redis จาก `Redis__ConnectionString` ตัวเดียว (`REDIS_PASSWORD` ใช้กับ container Redis ไม่ใช่แอป)

**3) ลำดับเปิดใช้ (ปลอดภัยทุกขั้น ย้อนได้)**
1. apply migration `AddNotificationKafkaDelivery` ด้วย migration bundle ก่อน — additive ล้วน (คอลัมน์ nullable 2 ตัว + partial index) แอปเวอร์ชันเก่าที่ยังรันอยู่ไม่กระทบ
2. deploy แอปเวอร์ชันใหม่โดย `Transport=Database` (พฤติกรรมเดิม; job ส่งอีเมลหันมาใช้ Redis claim ร่วมด้วย) **ให้ครบทุก host ที่รัน Hangfire server ก่อนไปขั้นถัดไป** — binary เวอร์ชันเก่าไม่มี claim ร่วมนี้ ถ้ามี host เก่าเหลืออยู่ตอนพลิกเป็น `Kafka` มันจะส่งอีเมลซ้ำกับ consumer ได้
3. รัน container `siri_kafka` (ข้อ 1) → `docker exec siri_kafka /opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:9092 | head -1` ต้องตอบ
4. ตั้ง `Notification__Delivery__Transport=Kafka` + `Kafka__*` ที่ทุก host ที่รัน Hangfire server แล้ว restart (ช่วง host ใหม่/เก่าทับกันไม่ส่งซ้ำ — Redis claim ร่วม)
5. ตรวจ: ส่งอีเมลทดสอบ (ลืมรหัสผ่าน) → ใน log ต้องเห็น `Kafka topics ensured`, `Kafka consumer email-delivery subscribed ...`; แถวใน `NOTIFY.EMAIL_OUTBOX` ไป `Pending → Queued → Sent` ภายในไม่กี่วินาที (ไม่ใช่รอรอบ 1 นาที); Hangfire dashboard ยังเห็น `email-outbox-send` รันแต่ไม่ทำอะไร

**ย้อนกลับ**: ตั้ง `Transport=Database` + restart — job กลับมาส่งเอง และ adopt แถว `Queued` ที่ค้างเกิน `QueuedStaleAfterMinutes` ให้อัตโนมัติ (อยากให้เร็วกว่านั้น: `UPDATE "NOTIFY"."EMAIL_OUTBOX" SET "STATUS"='Pending', "QUEUED_AT_UTC"=NULL WHERE "STATUS"='Queued';`) ไม่มีข้อมูลหาย เพราะ DB เป็น source of truth ตลอด

**ตรวจ/ดูแลประจำ**
- สถานะ outbox: `SELECT "STATUS", count(*) FROM "NOTIFY"."EMAIL_OUTBOX" GROUP BY 1;` · dead letter (ต้องมีคนดู): `... WHERE "STATUS"='Failed' AND "NEXT_RETRY_AT_UTC" IS NULL` (อ่านสาเหตุที่ `LAST_ERROR`) · หลังแก้สาเหตุแล้วอยากส่งใหม่: `UPDATE "NOTIFY"."EMAIL_OUTBOX" SET "STATUS"='Pending', "ATTEMPTS"=0, "NEXT_RETRY_AT_UTC"=NULL, "LAST_ERROR"=NULL WHERE "ID"='<uuid>';`
- consumer lag: `docker exec siri_kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 --describe --group siriupskill.notification.email` (LAG ควรเป็น 0 หรือลดลง)
- DLQ (record ที่ประมวลผลไม่ได้): `docker exec siri_kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic siriupskill.notification.email.dlq.v1 --from-beginning --timeout-ms 5000` — แต่ละอันเป็น JSON (`reason`, `sourceTopic`, `offset`, `originalValue`); แถว outbox ที่อยู่เบื้องหลังยังอยู่ใน DB และถูก publish ใหม่เองเมื่อ `Queued` เกิน stale window
- metric (OTLP): `notification.email.exhausted` > 0 = ต้องไปดู · `notification.relay.failures` ขึ้นต่อเนื่อง = broker เข้าไม่ถึง (อีเมลไม่หาย แค่ล่าช้า) · `notification.email.throttled` = ชนงบ `MaxEmailsPerMinute`
- Kafka ล่ม **ไม่ทำให้เว็บล่ม**: API แค่เขียนแถว outbox; อีเมลค้าง `Pending` จนกว่า broker กลับมาแล้วถูกส่งตามลำดับ
- backup: ไม่ต้อง backup Kafka — ไม่ใช่ source of truth (ถ้า volume หาย แถว `Queued` จะถูก publish ใหม่เองภายใน stale window)

## CI/CD

```
push → GitHub Actions:
  build + unit test + integration test (Testcontainers) + architecture test
  → build docker image (api, ssr) → push GHCR
  → ssh เข้า Contabo → docker compose pull && up -d (staging)
  → smoke test
  → manual approve → deploy production
  → รัน EF migration bundle แยกขั้นตอน (ไม่ใช่ตอน app start)
```
- Migration ขึ้น production ด้วย **bundle** เท่านั้น และ **backup ก่อนรันทุกครั้ง**
- สร้าง bundle ด้วย `backend/scripts/migrate-bundle.sh` (self-contained, `linux-x64` — ตรงกับ VPS/Docker target ด้านบน) ไม่ฝัง connection string ไว้ในไฟล์ — ส่งตอนรันจริงผ่าน `--connection` หรือ env `ConnectionStrings__Default`, ต้องก็อบ `appsettings.json` ไปวางข้าง ๆ ด้วย (ดู comment ในสคริปต์)
- Zero-downtime: รัน 2 replica ของ api หลัง caddy + health check (ต้องออกแบบ migration ให้ backward compatible: เพิ่มคอลัมน์ก่อน → deploy โค้ด → ค่อยลบของเก่าในรอบถัดไป)

## Backup & DR (สำคัญมากเพราะ self-host = ไม่มีใครกู้ให้)

- Full backup รายวัน + transaction log ทุก 1 ชั่วโมง
- เก็บ **off-site** (Bunny Storage หรือ Backblaze B2) — เก็บบน VPS เครื่องเดียวกันไม่นับเป็น backup
- เข้ารหัสไฟล์ backup ก่อนอัป
- เก็บย้อนหลัง 30 วัน + snapshot รายเดือน 12 เดือน
- **ซ้อมกู้จริงทุกไตรมาส** และบันทึกเวลาที่ใช้ (ถ้าไม่เคยซ้อม = ไม่มี backup)
- RPO เป้าหมาย ≤ 1 ชม. · RTO ≤ 4 ชม.

## Hardening เครื่อง

- SSH: key-only, ปิด root login, เปลี่ยน port, fail2ban
- ufw: เปิดแค่ 22 (จำกัด IP ถ้าทำได้), 80, 443 — **MSSQL 1433 และ Redis 6379 ห้ามเปิดออกเน็ตเด็ดขาด** (อยู่ใน docker network เท่านั้น)
- Redis ต้องตั้ง password + `bind` ภายใน; MSSQL sa password แข็งแรงและเก็บใน secret
- อัปเดต OS อัตโนมัติ (unattended-upgrades) + รีบูตตามรอบที่กำหนด
- Docker: ห้าม mount docker socket เข้า container ที่รับ request จากภายนอก
- ตั้ง swap 4–8 GB กัน OOM ตอน MSSQL พีค

## Monitoring & alert

> **อัปเดต 2026-09-21:** Uptime Kuma (`https://kuma.siristudiophoto.com/`) และ .NET Aspire
> Dashboard (`https://monitor.siristudiophoto.com/`, แทนที่แผน Seq เดิมด้านล่าง) รันอยู่จริงบน
> VPS แล้ว (shared ข้ามหลายโปรเจ็คบนเครื่องเดียวกัน) — ขั้นตอนตั้งค่าละเอียดอยู่ที่
> `docs/runbooks/uptime-and-log-monitoring-setup.md` ค้างอยู่ 2 อย่างที่ต้องเจ้าของโปรเจ็คทำเอง
> (login Kuma เพิ่ม monitor + รันคำสั่งเดียวบน VPS หา OTLP endpoint จริง) — Claude Code ทำแทนไม่ได้
> ทั้งคู่ (ห้ามกรอกรหัสผ่าน + ไม่มีสิทธิ์ SSH เข้า production โดยตรง)

- Uptime Kuma: เช็ค `/health`, หน้าแรก, และ Bunny playback ตัวอย่าง
- Netdata หรือ Prometheus+Grafana: CPU / RAM / disk / IO
- ~~Seq~~ .NET Aspire Dashboard: log + trace + metric ผ่าน OTLP (`Observability:OtlpEndpoint`, P0-13)
- Alert เข้า LINE Notify หรือ Discord webhook
- ต้อง alert เป็นพิเศษ: disk > 80%, DB > 7 GB (ถ้าใช้ Express), Stripe webhook ล้มเหลว/ตอบไม่ทันติดต่อกัน, ออร์เดอร์ค้างสถานะผิดปกติ (ops queue) > 20 รายการ

## สิ่งที่ต้องยอมรับเมื่อ self-host

- ไม่มี auto-scaling — ถ้า traffic พุ่งต้องเพิ่มเครื่องเอง
- ไม่มี managed failover — เครื่องล่ม = ระบบล่มจนกว่าจะกู้
- งาน ops (patch, backup, monitor) เป็นภาระของทีมเอง ~2–4 ชม./สัปดาห์
- ถ้ารับความเสี่ยงนี้ไม่ได้ตอน scale ขึ้น ควรวางแผนย้าย DB ไป managed service ในภายหลัง
