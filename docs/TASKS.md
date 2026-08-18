# SIRI UpSkill — Work Breakdown (Build Backlog)

อัปเดต 2026-08-18 · สอดคล้องกับ D-13 Bunny Stream · D-14 Stripe (แก้ไขจาก EasySlip) · D-15 Contabo

**วิธีใช้:** หยิบทีละ task ตามลำดับ dependency สั่ง Claude Code ได้ตรง ๆ ว่า `ทำ P0-11`
ทุก task ต้องผ่าน Definition of Done ใน `.claude/rules/workflow.md` ก่อนปิด
**Track:** `INFRA` `BE` `FE` `DESIGN` `QA`
**Est** = คน-วัน (person-day)

| Phase | ชื่อ | สัปดาห์ | คน-วัน |
|-------|-----|--------|--------|
| P0 | Foundation & Infrastructure | 1–3 | ~62 |
| P1 | Catalog, Search & SEO | 4–7 | ~66 |
| P2 | Media & Secure Player (Bunny) | 8–11 | ~64 |
| P3 | Commerce & Stripe Payment | 12–14 | ~38 |
| P4 | Instructor Studio | 15–18 | ~62 |
| P5 | Interactive Learning | 19–21 | ~48 |
| P6 | Admin, CMS & Revenue | 22–25 | ~62 |
| P7 | Hardening & Launch | 26–28 | ~44 |
| | | **28 สัปดาห์** | **~446** |

---

## P0 — Foundation & Infrastructure (สัปดาห์ 1–3)

**Exit criteria:** ล็อกอินได้ 2 อุปกรณ์ อุปกรณ์ที่ 3 เตะเครื่องเก่าออกอัตโนมัติ · CI เขียว · deploy staging บน Contabo สำเร็จ · restore DB จาก backup ได้จริง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track |
|----|------|------------------------|-----|-----|-------|
| P0-01 | เตรียม Contabo VPS | user ไม่ใช่ root, ssh key-only, ปิด password login, fail2ban, ufw (เปิดแค่ 22/80/443), swap 8GB, unattended-upgrades | — | 1 | INFRA |
| P0-02 | ติดตั้ง Docker + Compose | docker engine, compose v2, log rotation, ตั้ง restart policy | P0-01 | 0.5 | INFRA |
| P0-03 | **ตัดสิน + ติดตั้ง MSSQL** | เลือก edition (Express/Standard) ตาม `DEPLOYMENT.md` · collation `Thai_100_CI_AS_SC_UTF8` · แยก DB `SiriUpSkill` และ `SiriUpSkill_Events` · sa password ใน secret · ไม่ expose 1433 | P0-02 | 1 | INFRA |
| P0-04 | Redis + Caddy | Redis มี password ไม่ออกเน็ต · Caddy reverse proxy + TLS อัตโนมัติ + security headers | P0-02 | 1 | INFRA |
| P0-05 | Backup + restore drill | full รายวัน + log รายชั่วโมง → บีบอัด → เข้ารหัส → อัปขึ้น Bunny Storage/B2 · **ต้องซ้อม restore จริง 1 ครั้งและจับเวลา** | P0-03 | 2 | INFRA |
| P0-06 | Monitoring & alert | Uptime Kuma + Netdata + Seq · alert เข้า LINE/Discord · alert: disk>80%, DB>7GB, api down | P0-04 | 1.5 | INFRA |
| P0-07 | CI pipeline | GitHub Actions: build → unit test → integration test → architecture test → secret scan → npm audit / dotnet list package --vulnerable | P0-20 | 2 | INFRA |
| P0-08 | CD pipeline | build image → GHCR → ssh deploy staging → smoke test → manual approve → backup → migration bundle → deploy prod | P0-07 | 2 | INFRA |
| P0-10 | Solution scaffold | สร้าง `SiriUpSkill.sln` + project ทั้งหมดตาม `ARCHITECTURE.md` §2 · `Directory.Build.props` (nullable, warnaserror, langversion) | — | 1 | BE |
| P0-11 | SharedKernel | `Result<T>`, `DomainError` catalog, `IClock`, `IUserContext`, UUIDv7 helper, `PagedResult<T>` | P0-10 | 2 | BE |
| P0-12 | Persistence baseline | `AppDbContext`, schema separation, audit interceptor (CreatedBy/UpdatedBy), soft-delete filter, migration bundle script | P0-11 | 2 | BE |
| P0-13 | API host baseline | Serilog+OTel, ProblemDetails (RFC 9457), CORS, RateLimiter, health checks, OpenAPI, correlation id middleware | P0-11 | 2 | BE |
| P0-14 | Identity domain + migration | Users/Roles/UserRoles/UserSessions/RefreshTokens/SecurityAudits ตาม `DATABASE.md` | P0-12 | 2 | BE |
| P0-15 | สมัคร + ยืนยันอีเมล | password policy, hash, email confirm token หมดอายุ, rate limit, กัน user enumeration | P0-14, P0-19 | 2 | BE |
| P0-16 | ล็อกอิน + refresh rotation | access 15 นาที (memory) + refresh 30 วัน (httpOnly cookie) + rotation + **reuse detection → revoke ทั้ง family** | P0-14 | 2.5 | BE |
| P0-17 | **SE-03 Concurrent login control** | Redis session registry + mirror `UserSessions` · เกิน limit → revoke เก่าสุด + audit + แจ้งเตือน · limit ตั้งค่าได้ระดับระบบและรายบัญชี · Redis ล่ม = fail closed สำหรับ playback | P0-16 | 3 | BE |
| P0-18 | API จัดการอุปกรณ์ | list session ของตัวเอง + revoke รายตัว/ทั้งหมด | P0-17 | 1 | BE |
| P0-19 | Email outbox + provider | `EmailOutbox` + Hangfire sender + retry/backoff + template layout (ไทย) + provider adapter | P0-12 | 2 | BE |
| P0-20 | Test harness | Testcontainers (MSSQL+Redis), `WebApplicationFactory`, data builder, ArchitectureTests (NetArchTest บังคับ module boundary) | P0-13 | 2 | QA |
| P0-21 | ลืมรหัสผ่าน / รีเซ็ต | token ใช้ครั้งเดียว หมดอายุ 1 ชม. + revoke session ทั้งหมดหลังเปลี่ยนรหัส | P0-16 | 1 | BE |
| P0-22 | Authorization policies | `AdminOnly`, `InstructorOnly`, `EnrolledInCourse`, `CourseOwner` + default deny ที่ระดับ group | P0-16 | 1 | BE |
| P0-23 | Hangfire setup | SQL storage + dashboard ป้องกันด้วย auth + job retention | P0-12 | 1 | BE |
| P0-30 | Angular workspace | Angular 22 + SSR + zoneless + Tailwind v4 + ESLint/Prettier + strict TS + path alias | — | 1.5 | FE |
| P0-31 | **Design system ครั้งแรก** | ใช้ skill `ui-ux-pro-max` (ลง Python ก่อน) → เลือก palette + font ไทย + spacing/radius/shadow scale → `tokens.css` + dark mode + icon set | P0-30 | 3 | DESIGN |
| P0-32 | Core FE | auth interceptor (แนบ token + refresh อัตโนมัติ + คิวคำขอระหว่าง refresh), error interceptor, global error handler, guard, i18n TH/EN | P0-30 | 2.5 | FE |
| P0-33 | Layouts | public / learn / instructor / admin + header + footer + mobile nav + skip-to-content | P0-31 | 2 | FE |
| P0-34 | UI kit ชุดแรก | button, input, select, checkbox, modal, toast, skeleton, empty state, pagination, breadcrumb — a11y ครบ (focus ring, aria) | P0-31 | 3 | FE |
| P0-35 | หน้า auth | สมัคร / ล็อกอิน / ยืนยันอีเมล / ลืมรหัส / จัดการอุปกรณ์ | P0-34, P0-18 | 2.5 | FE |
| P0-36 | E2E setup | Playwright + smoke: สมัคร→ล็อกอิน→ล็อกอินเครื่องที่ 3→เครื่องแรกหลุด | P0-35 | 1.5 | QA |
| P0-37 | Seed & dev bootstrap | seed role/admin/ผู้ใช้ทดสอบ + คำสั่งเดียวเซ็ตอัพเครื่อง dev | P0-14 | 1 | BE |

---

## P1 — Catalog, Search & SEO (สัปดาห์ 4–7)

**Exit criteria:** Lighthouse SEO ≥ 95 · LCP < 2.5s บน 4G · search p95 < 300ms · เปิดหน้า course detail จาก Google ได้และมี rich result

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track |
|----|------|------------------------|-----|-----|-------|
| P1-01 | Category tree | self-reference + slug + sort + admin CRUD + cache Redis + invalidate | P0-12 | 2 | BE |
| P1-02 | Course/Section/Episode domain | entity + invariant (publish ต้องมี ≥1 episode ที่มี media) + migration | P1-01 | 3 | BE |
| P1-03 | Instructor profile | สมัครเป็นผู้สอน + approve flow + `RevenueSharePercent` | P0-14 | 2 | BE |
| P1-04 | Course CRUD (draft) | สร้าง/แก้/ลบ draft + slug generator (ไทย→latin) + ownership check | P1-02 | 2.5 | BE |
| P1-05 | Publish workflow | Draft→InReview→Published/Rejected + validation rule + audit | P1-04 | 2 | BE |
| P1-06 | Full-text search | FTS index + search API + filter (category/level/price/rating) + sort + facet count + pagination | P1-02 | 3 | BE |
| P1-07 | Read model + cache | course list/detail projection + output cache + invalidate ตอน publish/แก้ราคา | P1-06 | 2 | BE |
| P1-08 | Review & rating | เขียนรีวิวได้เมื่อ enroll แล้วเท่านั้น + `CourseStatsUpdater` ที่เดียว + job reconcile รายคืน | P1-02 | 2.5 | BE |
| P1-09 | SEO backend | sitemap.xml (แบ่งหน้า), robots.txt, redirect table | P1-07 | 1.5 | BE |
| P1-20 | Mega menu | หมวดหมู่หลายชั้น + keyboard nav + mobile drawer + a11y (`aria-expanded`) | P0-33, P1-01 | 2.5 | FE |
| P1-21 | หน้า catalog + filter | filter sync กับ URL (กด back แล้วไม่รีเซ็ต) + infinite/pagination + skeleton + empty state | P1-06 | 3.5 | FE |
| P1-22 | หน้า course detail | syllabus accordion, free preview badge, duration รวม, outcomes, instructor card, sticky ปุ่มซื้อบนมือถือ, รีวิว | P1-07 | 4 | FE |
| P1-23 | หน้าแรก | hero, หมวดหมู่, คอร์สแนะนำ, คอร์สใหม่ (data จาก API ยังไม่ต้องต่อ CMS) | P1-22 | 2.5 | FE |
| P1-24 | SEO service | title/meta/canonical/OG + JSON-LD (`Course`, `BreadcrumbList`, `AggregateRating`) + hreflang | P1-22 | 2 | FE |
| P1-25 | Search UI | autocomplete + debounce + recent search + ไม่มีผลลัพธ์แนะนำทางไปต่อ | P1-21 | 2 | FE |
| P1-30 | Seed คอร์สตัวอย่าง | 3 หมวดหลัก + 20 คอร์ส + instructor 5 คน (ข้อมูลไทยจริง ไม่ใช่ lorem) | P1-05 | 1.5 | BE |
| P1-31 | Performance budget ใน CI | Lighthouse CI + bundle size budget (initial ≤ 300KB gz) fail build เมื่อเกิน | P1-24 | 1.5 | QA |
| P1-32 | เทสต์ P1 | integration test search/filter + e2e เดินจากหน้าแรก→หมวด→คอร์ส | P1-22 | 2 | QA |

---

## P2 — Media & Secure Player (สัปดาห์ 8–11) 🔴 เสี่ยงสุด

**Exit criteria:** IDM / video downloader extension / `yt-dlp` / devtools ดึงไฟล์ไม่ได้ · เล่นได้ครบ browser matrix · resume ตำแหน่งถูกต้อง · watermark แสดงชื่อผู้เรียนจริง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track |
|----|------|------------------------|-----|-----|-------|
| P2-01 | **ตั้งค่า Bunny Stream + เคลียร์ข้อจำกัด** | เปิด library, **ยืนยันกับ Bunny ว่าแพ็กเกจไหนได้ DRM (MediaCage) และมี FairPlay สำหรับ iOS ไหม** · token auth key · allowed referrer · geo rule · ปิด direct play · **ถ้าไม่มี FairPlay ต้องตัดสินใจ fallback ก่อนทำต่อ** | — | 2 | INFRA |
| P2-02 | `IVideoProvider` + Bunny adapter | create video, direct upload signature, get status, delete, signed playback URL (token hash) | P2-01 | 3 | BE |
| P2-03 | MediaAssets + upload session | ตาราง + API ขอ upload URL + webhook receiver (verify signature) + Hangfire poll สำรอง | P2-02 | 3 | BE |
| P2-04 | **Playback session API** | ตรวจ enrollment active + ไม่หมดอายุ + free preview + device limit + rate limit → ออก signed URL อายุ 2–5 นาที + watermark payload | P2-03, P0-17 | 3.5 | BE |
| P2-05 | DRM license proxy | proxy ไป Bunny พร้อมตรวจสิทธิ์ซ้ำทุกครั้ง · client ห้ามคุย license server ตรง | P2-04 | 2.5 | BE |
| P2-06 | PlaybackSessions log + anomaly | log ทุกครั้งที่ออก token + job flag พฤติกรรมผิดปกติ (>30 ตอน/ชม., หลาย IP) | P2-04 | 2 | BE |
| P2-07 | Progress API | `EpisodeProgress` upsert + heartbeat batch + คำนวณ `ProgressPercent` + `WatchEvents` เข้า DB แยก | P2-04 | 2.5 | BE |
| P2-20 | Player component | ครอบ Shaka Player + DRM config + error mapping ที่อ่านรู้เรื่อง (ไม่ใช่ error code ดิบ) | P2-05 | 4 | FE |
| P2-21 | Player controls | speed 0.5–2.0x, quality/auto, volume, fullscreen, PiP, caption, keyboard (space/←/→/f/m), ปุ่มขนาด ≥44px | P2-20 | 3 | FE |
| P2-22 | **Dynamic watermark** | overlay ชื่อ+อีเมล ขยับสุ่มทุก ~8 วิ, opacity ต่ำ, ไม่บังเนื้อหาสำคัญ, ลบด้วย devtools แล้ววิดีโอต้องหยุด | P2-20 | 2.5 | FE |
| P2-23 | Resume + heartbeat | จำตำแหน่ง, ถามผู้ใช้ว่าจะเล่นต่อไหม, heartbeat ทุก 15 วิ, offline-safe (queue แล้วส่งซ้ำ) | P2-07 | 2.5 | FE |
| P2-24 | หน้า /learn | curriculum sidebar + สถานะดูแล้ว + ไปตอนถัดไปอัตโนมัติ + progress bar + responsive มือถือ | P2-21 | 3.5 | FE |
| P2-25 | Free preview flow | ดูตัวอย่างได้โดยไม่ต้องซื้อ + CTA ชวนซื้อเมื่อจบตัวอย่าง | P2-24 | 1.5 | FE |
| P2-30 | Browser matrix test | Chrome/Edge/Firefox/Safari macOS/Safari iOS/Android Chrome — บันทึกผลเป็นตาราง + ระบุ fallback | P2-24 | 2 | QA |
| P2-31 | **Anti-piracy pen-test** | ลองดึงด้วย IDM, extension ยอดนิยม, `yt-dlp`, copy manifest URL ไปเปิดที่อื่น, devtools network — **ต้องล้มเหลวทุกทาง** ถ้าทางใดสำเร็จถือว่า P2 ยังไม่ผ่าน | P2-30 | 2 | QA |
| P2-32 | Analytics rollup job | `WatchEvents` → `analytics.EpisodeDropOff` + `DailyCourseStats` รายคืน + purge > 90 วัน | P2-07 | 2 | BE |

---

## P3 — Commerce & Stripe Payment (สัปดาห์ 12–14)

> แก้ไข 2026-08-18: เปลี่ยนจาก EasySlip เป็น **Stripe** ทั้ง phase (D-14/Q2 ฉบับแก้ไข — สเปคเต็ม `PAYMENT.md`) ไม่มี flow อัปสลิปอีกต่อไป

**Exit criteria:** ซื้อ→สแกน QR→จ่ายในแอปธนาคาร→เข้าเรียนได้อัตโนมัติผ่าน webhook · webhook ยิงซ้ำ/ปลอมไม่มีผลใด ๆ 100% · ออร์เดอร์ไม่มีสถานะค้าง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track |
|----|------|------------------------|-----|-----|-------|
| P3-01 | Stripe PaymentIntent integration | Stripe.net adapter หลัง `IPaymentMethod` + สร้าง PaymentIntent (thb, promptpay, amount หน่วยสตางค์) + Options+ValidateOnStart + key อยู่ใน user-secrets/env เท่านั้น + retry/backoff ตอนเรียก Stripe | — | 2 | BE |
| P3-02 | Cart + Order domain | ตาราง + pricing engine ที่ server (ราคา, ส่วนลด, ยอดรวม) + กันซื้อคอร์สที่ซื้อแล้วซ้ำ | P1-02 | 3 | BE |
| P3-03 | Order lifecycle | AwaitingPayment 30 นาที + job หมดอายุ + cancel PaymentIntent ที่ Stripe + จ่ายสำเร็จหลังหมดอายุต้องเปิดออร์เดอร์ให้ใหม่หรือ refund (ห้ามกลืนเงิน) | P3-02, P3-01 | 2 | BE |
| P3-04 | **Stripe webhook endpoint** | ตรวจ `Stripe-Signature` ทุก request + idempotent (**unique index** บน StripeEventId) + เก็บ raw event JSON + จัดการ succeeded/payment_failed/canceled ใน transaction เดียวกับ order state + rate limit | P3-01, P3-03 | 2.5 | BE |
| P3-05 | Refund flow | Stripe refund API + track สถานะ async (Stripe ขอเลขบัญชีจากผู้ซื้อเอง — refund ค้างได้) + audit ทุกรายการ | P3-04 | 2 | BE |
| P3-06 | Payment state machine | Pending→Processing→Succeeded/Failed/Expired/Refunded ใน transaction เดียว · กัน race ระหว่าง webhook กับ expiry job · audit ทุกการเปลี่ยนสถานะ | P3-04 | 2 | BE |
| P3-07 | Payment ops queue | คิวกรณีผิดปกติ (จ่ายซ้ำ/จ่ายหลังหมดอายุ/refund ค้าง) + admin resolve + audit + แจ้งผู้ซื้อ | P3-06 | 1.5 | BE |
| P3-08 | Auto-enroll + ใบเสร็จ | outbox event → สร้าง enrollment (คิด `ExpiresAtUtc` จาก `AccessDurationDays`) + อีเมลใบเสร็จ + in-app notification | P3-06 | 2 | BE |
| P3-09 | Promo code | สร้าง/ตรวจ/ใช้ + redemption แบบ atomic ที่ระดับ SQL + จำกัดต่อผู้ใช้ | P3-02 | 2.5 | BE |
| P3-10 | Reconcile job | list PaymentIntents จาก Stripe API รายวันเทียบกับ Orders + จับ webhook ที่หลุด + รายงานรายการที่ไม่แมตช์ | P3-08 | 2 | BE |
| P3-20 | ตะกร้า | เพิ่ม/ลบ, สรุปยอด, ใส่โค้ดส่วนลด, persist ข้ามอุปกรณ์ | P3-02 | 2 | FE |
| P3-21 | หน้า checkout + QR | แสดง QR จาก `next_action.promptpay_display_qr_code` ใหญ่ชัดบนมือถือ + นับถอยหลัง + วิธีจ่ายทีละขั้นเป็นภาษาไทย + แจ้งว่า statement ขึ้นชื่อ Stripe | P3-01 | 3 | FE |
| P3-22 | สถานะการจ่ายแบบ real-time | polling สถานะผ่าน backend (ห้ามตัดสินจาก client) + ข้อความแต่ละสถานะชัดเจน (รอจ่าย/สำเร็จ/หมดเวลา/คืนเงิน) | P3-04 | 2 | FE |
| P3-23 | ประวัติคำสั่งซื้อ | รายการ + รายละเอียด + สถานะจ่าย/คืนเงิน + ดาวน์โหลดใบเสร็จ | P3-08 | 2 | FE |
| P3-24 | คอร์สของฉัน | เรียงตามล่าสุด + ความคืบหน้า + ปุ่มเรียนต่อ + วันหมดอายุ | P3-08 | 2 | FE |
| P3-30 | หน้า admin payment ops | รายการจ่ายทั้งหมด + สถานะจาก Stripe + ops queue + สั่ง refund + audit trail | P3-07 | 2 | FE |
| P3-31 | เทสต์ P3 | e2e ซื้อ→จ่าย (Stripe test mode)→เข้าเรียน · webhook ยิงซ้ำ · signature ปลอม · จ่ายแข่งกับ expiry · refund flow | P3-30 | 3 | QA |

---

## P4 — Instructor Studio (สัปดาห์ 15–18)

**Exit criteria:** instructor สร้างคอร์ส 10 ตอนจนขึ้นขายได้เอง โดยไม่ต้องให้ dev ช่วย

| ID | Task | รายละเอียด | Dep | Est | Track |
|----|------|-----------|-----|-----|-------|
| P4-01 | Course builder API | reorder section/episode แบบ batch + optimistic concurrency + autosave endpoint | P1-04 | 3 | BE |
| P4-02 | Upload flow API | ขอ upload URL, ติดตามสถานะ, ยกเลิก, แทนที่ไฟล์ | P2-03 | 2 | BE |
| P4-03 | Attachment API | อัปไฟล์แนบต่อ episode + จำกัดชนิด/ขนาด + สแกนไวรัส | P4-02 | 2 | BE |
| P4-04 | Instructor analytics API | ยอดขาย/ช่วงเวลา, นักเรียน active, drop-off ต่อ episode, คอร์สขายดี (อ่านจาก `analytics.*`) | P2-32 | 3 | BE |
| P4-20 | Builder UI — โครงสร้าง | drag & drop (Angular CDK) + **ย้ายด้วยคีย์บอร์ดได้** + undo + autosave indicator | P4-01 | 5 | FE |
| P4-21 | Builder UI — อัปโหลด | อัปหลายไฟล์, progress, resumable, retry, สถานะ transcode | P4-02 | 3.5 | FE |
| P4-22 | Builder UI — ตั้งค่าคอร์ส | ราคา, หมวด, ระดับ, outcomes, requirements, thumbnail, SEO, ตั้ง free preview | P4-20 | 3 | FE |
| P4-23 | หน้า analytics | กราฟยอดขาย + drop-off chart (ECharts) + ตารางนักเรียน + export CSV | P4-04 | 3.5 | FE |
| P4-24 | หน้า dashboard ผู้สอน | สรุปรายได้เดือนนี้, คอร์สรอตรวจ, คำถามที่ยังไม่ตอบ | P4-23 | 2 | FE |
| P4-25 | Onboarding ผู้สอน | สมัครเป็นผู้สอน + กรอกข้อมูล + สถานะรออนุมัติ | P1-03 | 2 | FE |
| P4-30 | Usability test | ทดสอบกับ instructor จริง 3 คน แล้วแก้ตามผล | P4-22 | 2 | QA |
| P4-31 | เทสต์ P4 | e2e สร้างคอร์สครบจนขึ้นขาย | P4-22 | 2 | QA |

---

## P5 — Interactive Learning (สัปดาห์ 19–21)

**Exit criteria:** เรียนจบแล้วได้ certificate อัตโนมัติ ตรวจสอบย้อนกลับได้จากหน้า verify สาธารณะ

| ID | Task | รายละเอียด | Dep | Est | Track |
|----|------|-----------|-----|-----|-------|
| P5-01 | Quiz domain + API | quiz/question/option/attempt + จำกัดจำนวนครั้ง + คำนวณคะแนน + เฉลย | P1-02 | 3.5 | BE |
| P5-02 | Assignment + grading API | ส่งงาน, ไฟล์แนบ, instructor ให้คะแนน + feedback | P4-03 | 3 | BE |
| P5-03 | Certificate | เงื่อนไขจบคอร์ส → สร้าง PDF (QuestPDF) + serial + verify code + หน้า verify สาธารณะ | P2-07 | 3 | BE |
| P5-04 | Q&A API | thread ใต้ episode + ตอบกลับ + upvote + badge ผู้สอน + report/moderation | P1-02 | 3 | BE |
| P5-20 | Quiz UI | ทำข้อสอบ, ตัวจับเวลา (ถ้ามี), ผลลัพธ์ + เฉลย, ทำใหม่ | P5-01 | 3 | FE |
| P5-21 | Assignment UI | อัปไฟล์, ดูสถานะ, ดูคะแนน + feedback | P5-02 | 2.5 | FE |
| P5-22 | Certificate UI | ปุ่มดาวน์โหลด + แชร์ LinkedIn + หน้า verify | P5-03 | 2 | FE |
| P5-23 | Q&A UI | thread ใต้ player, ตอบ, mention, แจ้งเตือน, รายงานเนื้อหา | P5-04 | 3.5 | FE |
| P5-24 | Attachment UI | ดาวน์โหลดไฟล์แนบผ่าน signed URL | P4-03 | 1 | FE |
| P5-30 | เทสต์ P5 | e2e เรียนจบคอร์ส→ได้ certificate→verify ได้ | P5-22 | 2 | QA |

---

## P6 — Admin, CMS & Revenue (สัปดาห์ 22–25)

**Exit criteria:** ทีมการตลาดจัดโปรหน้าแรกได้เองโดยไม่ต้อง deploy · ปิดยอด payout เดือนแรกได้

| ID | Task | รายละเอียด | Dep | Est | Track |
|----|------|-----------|-----|-----|-------|
| P6-01 | CMS API | banner, menu, page, blog post + sanitize HTML ฝั่ง server (allowlist) + SEO field + redirect | P0-12 | 4 | BE |
| P6-02 | Flash sale + bundle | ตั้งเวลาเริ่ม/จบ + ราคาพิเศษ + bundle หลายคอร์ส + ลำดับความสำคัญของส่วนลด | P3-09 | 3 | BE |
| P6-03 | **Revenue split** | คำนวณตอนออร์เดอร์ Paid + snapshot % + หัก payment fee ตาม Q4 + reversal เมื่อ refund | P3-08 | 3 | BE |
| P6-04 | Payout batch | ปิดรอบรายเดือน + สร้าง batch + หัก withholding tax + export ไฟล์โอน + mark paid | P6-03 | 3 | BE |
| P6-05 | Announcement + bulk email | ส่งหานักเรียนที่ enroll + ตั้งเวลา + ผ่าน outbox + unsubscribe | P0-19 | 2.5 | BE |
| P6-06 | Admin ops API | user management, course moderation, refund, audit log viewer, feature flag | P0-22 | 3 | BE |
| P6-20 | Admin — CMS | จัด banner (drag), เมนู, blog editor + preview + จัดการรูป | P6-01 | 5 | FE |
| P6-21 | Admin — marketing | promo code, flash sale, bundle + ปฏิทินแคมเปญ | P6-02 | 3.5 | FE |
| P6-22 | Admin — revenue | รายงานรายได้, split ต่อ instructor, payout batch, export | P6-04 | 3.5 | FE |
| P6-23 | Admin — users & courses | ค้นหา/แก้/ระงับผู้ใช้, ตรวจคอร์สรออนุมัติ, refund, audit log | P6-06 | 3.5 | FE |
| P6-24 | หน้า blog สาธารณะ | list + detail + SEO + related + share | P6-01 | 2.5 | FE |
| P6-25 | ผูกหน้าแรกกับ CMS | หน้าแรกดึง banner/section จาก CMS แทน hardcode | P6-01 | 1.5 | FE |
| P6-30 | เทสต์ P6 | e2e จัดโปร + ตรวจว่าราคาที่ผู้ซื้อเห็นถูกต้องตามลำดับส่วนลด | P6-21 | 2.5 | QA |

---

## P7 — Hardening & Launch (สัปดาห์ 26–28)

| ID | Task | รายละเอียด | Dep | Est | Track |
|----|------|-----------|-----|-----|-------|
| P7-01 | Load test | k6: 5,000 concurrent viewer, 500 checkout/นาที, search burst — หา bottleneck แล้วแก้ | ทุก phase | 3 | QA |
| P7-02 | Security review | OWASP Top 10, IDOR sweep ทุก endpoint, dependency scan, secret scan | — | 3 | BE |
| P7-03 | Pen-test ภายนอก | จ้างภายนอก + ปิดช่อง high/critical ครบ | P7-02 | 3 | QA |
| P7-04 | PDPA | consent banner, privacy/cookie policy, export/ลบข้อมูลตัวเอง (anonymize), data retention job, DPA กับ vendor | — | 3 | BE |
| P7-05 | ปรับ index + query | ตรวจ query ช้าจาก production trace แล้วเพิ่ม index | P7-01 | 2 | BE |
| P7-06 | Observability + runbook | dashboard, alert rule, runbook เหตุการณ์ที่พบบ่อย, on-call | P0-06 | 2 | INFRA |
| P7-07 | DR drill | ซ้อมกู้จาก backup แบบเต็ม + จับเวลา RTO/RPO จริง | P0-05 | 1.5 | INFRA |
| P7-08 | a11y audit | axe + คีย์บอร์ดทั้งระบบ + screen reader ตัวอย่างหน้าหลัก | — | 2.5 | QA |
| P7-09 | Content & legal | เงื่อนไขการใช้งาน, นโยบายคืนเงิน, เนื้อหาหน้า static, ตรวจข้อความไทยทั้งระบบ | — | 2 | — |
| P7-10 | Closed beta | ผู้ใช้จริง 50 คน 1 สัปดาห์ + เก็บ feedback + แก้ | P7-03 | 5 | — |
| P7-11 | Go-live | DNS, cert, monitoring, rollback plan, ประกาศ | P7-10 | 1 | INFRA |

---

## Critical path (ห้ามช้า)

```
P0-01→P0-03→P0-12→P0-14→P0-16→P0-17 ──► P1-02→P1-06→P1-22
                                              │
P2-01 (ต้องเริ่มเช็คกับ Bunny ตั้งแต่สัปดาห์ 4!) ──► P2-02→P2-04→P2-20→P2-31
                                              │
                                    P3-01/P3-04→P3-06→P3-08→P3-31
```

**งานที่ควรเริ่มขนานล่วงหน้า (ไม่ต้องรอถึง phase ของตัวเอง):**
- **P2-01** — ถามเรื่อง DRM/FairPlay กับ Bunny ตั้งแต่สัปดาห์ 4 คำตอบอาจเปลี่ยนแผน P2 ทั้งก้อน
- **P0-31** — design system ต้องนิ่งก่อน P1 เริ่มทำหน้าจอ ไม่งั้นต้องรื้อ
- **P3-01** — สมัครบัญชี Stripe Thailand (KYC ใช้เวลา) + เปิด PromptPay ใน dashboard + ทดสอบ test mode ตั้งแต่ P1
- **P0-03** — ตัดสิน MSSQL edition ให้จบตั้งแต่สัปดาห์แรก

## หมายเหตุ

- ประเมินบนสมมติฐาน BE 2 + FE 2 + QA 1 (Q5 ยังไม่ยืนยัน) — ถ้าทีมเล็กกว่านี้ ยืด timeline ตามสัดส่วน อย่าลดขอบเขต security
- Q4 (สูตร revenue split) ยังไม่ตอบ — ต้องตอบก่อน P6-03
- ถ้าต้องออกตลาดเร็ว: จบที่ P3 (~14 สัปดาห์) ขายได้จริง แล้วให้ทีมงานภายในอัปโหลดคอร์สแทน instructor ชั่วคราว
