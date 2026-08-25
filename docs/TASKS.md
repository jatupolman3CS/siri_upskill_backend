# SIRI UpSkill — Work Breakdown (Build Backlog)

อัปเดต **2026-08-24** (audit สถานะจริงทั้ง 160 task เทียบกับโค้ดบน branch `feat/p2-p6-scaffold`) · สอดคล้องกับ D-13 Bunny Stream · D-14 Stripe (แก้ไขจาก EasySlip) · D-15 Contabo · D-16 SiriLearn reconciliation (เพิ่ม P8–P10 หลัง launch) · D-17 mockup scaffold · mockup v2026-08-21 gap-fill

**วิธีใช้:** หยิบทีละ task ตามลำดับ dependency สั่งได้ตรง ๆ ว่า `ทำ P3-09`
ทุก task ต้องผ่าน Definition of Done ใน `.claude/rules/workflow.md` ก่อนปิด
**Track:** `INFRA` `BE` `FE` `DESIGN` `QA`
**Est** = คน-วัน (person-day) — ใช้จัด **ลำดับความสำคัญ/ความเสี่ยง** เท่านั้น ไม่ใช่แผนเวลาจริง (ตัวเลข 28 สัปดาห์คิดบนสมมติฐานทีมมนุษย์ BE2+FE2+QA1 ซึ่งไม่ตรงกับโหมดทำงานจริงที่เป็น agent — ดู Q5)

### คอลัมน์ Status (บังคับอัปเดตทุกครั้งที่ปิดงาน)

| Status | ความหมาย | ใครเปลี่ยนเป็นค่านี้ได้ |
|--------|----------|------------------------|
| `TODO` | ยังไม่มีโค้ด | — |
| `BUILT` | มีโค้ดครบตาม acceptance + build/unit test เขียว **แต่ยังไม่มี integration test และยังไม่ผ่าน review** | agent ที่ build (ตั้งเองได้) |
| `PART` | ทำบางส่วน ยังไม่ครบ acceptance — **ต้องมีบรรทัดอธิบายใน § "สถานะจริง"** ว่าขาดอะไร | agent ที่ build (ตั้งเองได้) |
| `BLOCK` | ติดการตัดสินใจหรือ infra ที่ agent ทำเองไม่ได้ | ใครก็ได้ ถ้าระบุเหตุผล |
| `DONE` | ครบ acceptance + build/test เขียว + **มี integration test** + ผ่าน review แล้ว | **Claude Code เท่านั้น** (หลัง review) — agent ที่ build ห้ามตั้งค่านี้ให้ตัวเอง |

**Own:** `CC` = Claude Code · `AG` = Antigravity · `OWN` = เจ้าของโปรเจ็ค (infra/บัญชี/การตัดสินใจธุรกิจ) · `AG+CC` = AG build แล้ว CC เข้าไปแก้ต่อ

> ⚠️ **`BUILT` ≠ เสร็จ** — ณ 2026-08-24 ยังไม่มี integration test ของ 7 โมดูลใหม่ (Commerce/Media/Learning/Payout/Cms/Community/Analytics) แม้แต่ไฟล์เดียว (`backend/tests/Siri.IntegrationTests/` มีแต่ Identity/Catalog) และไม่มี commit เลยตั้งแต่ scaffold → CI ยังไม่เคยรันบนงาน P1–P6 เลยสักครั้ง ทุกอย่างที่เป็น `BUILT` จึงยังไม่เคยถูกพิสูจน์กับ MSSQL/Redis จริง

| Phase | ชื่อ | สัปดาห์ | คน-วัน |
|-------|-----|--------|--------|
| P0 | Foundation & Infrastructure | 1–3 | ~62 |
| P1 | Catalog, Search & SEO | 4–7 | ~67 |
| P2 | Media & Secure Player (Bunny) | 8–11 | ~64 |
| P3 | Commerce & Stripe Payment | 12–14 | ~40 |
| P4 | Instructor Studio | 15–18 | ~67 |
| P5 | Interactive Learning | 19–21 | ~48 |
| P6 | Admin, CMS & Revenue | 22–25 | ~70 |
| P7 | Hardening & Launch | 26–28 | ~44 |
| | | **28 สัปดาห์ (ถึง launch)** | **~461** |
| P8 | Growth & Engagement (หลัง launch) | post-launch | ~26 |
| P9 | B2B Corporate Portal (หลัง launch) | post-launch | ~26 |
| P10 | Subscription (หลัง launch) | post-launch | ~20 |

---

## P0 — Foundation & Infrastructure (สัปดาห์ 1–3)

**Exit criteria:** ล็อกอินได้ 2 อุปกรณ์ อุปกรณ์ที่ 3 เตะเครื่องเก่าออกอัตโนมัติ · CI เขียว · deploy staging บน Contabo สำเร็จ · restore DB จาก backup ได้จริง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P0-01 | เตรียม Contabo VPS | user ไม่ใช่ root, ssh key-only, ปิด password login, fail2ban, ufw (เปิดแค่ 22/80/443), swap 8GB, unattended-upgrades | — | 1 | INFRA | TODO | OWN |
| P0-02 | ติดตั้ง Docker + Compose | docker engine, compose v2, log rotation, ตั้ง restart policy | P0-01 | 0.5 | INFRA | TODO | OWN |
| P0-03 | **ตัดสิน + ติดตั้ง MSSQL** | เลือก edition (Express/Standard) ตาม `DEPLOYMENT.md` · collation `Thai_100_CI_AS_SC_UTF8` · แยก DB `SiriUpSkill` และ `SiriUpSkill_Events` · sa password ใน secret · ไม่ expose 1433 | P0-02 | 1 | INFRA | PART | OWN |
| P0-04 | Redis + Caddy | Redis มี password ไม่ออกเน็ต · Caddy reverse proxy + TLS อัตโนมัติ + security headers | P0-02 | 1 | INFRA | TODO | OWN |
| P0-05 | Backup + restore drill | full รายวัน + log รายชั่วโมง → บีบอัด → เข้ารหัส → อัปขึ้น Bunny Storage/B2 · **ต้องซ้อม restore จริง 1 ครั้งและจับเวลา** | P0-03 | 2 | INFRA | TODO | OWN |
| P0-06 | Monitoring & alert | Uptime Kuma + Netdata + Seq · alert เข้า LINE/Discord · alert: disk>80%, DB>7GB, api down | P0-04 | 1.5 | INFRA | TODO | OWN |
| P0-07 | CI pipeline | GitHub Actions: build → unit test → integration test → architecture test → secret scan → npm audit / dotnet list package --vulnerable | P0-20 | 2 | INFRA | PART | CC |
| P0-08 | CD pipeline | build image → GHCR → ssh deploy staging → smoke test → manual approve → backup → migration bundle → deploy prod | P0-07 | 2 | INFRA | TODO | OWN |
| P0-09 | **[ใหม่ 2026-08-24] Apply migration ค้าง + ปลด FTS blocker** | migration 4 ตัวยัง Pending บน DB จริง (`AddCourseFullTextIndex`, `AddCommerceMediaLearningPayoutCmsCommunityAnalytics`, `AddAnnouncementsAndNotifications`, `AddLearningPathsAndAttachments`) · **`AddCourseFullTextIndex` อยู่ก่อนอีก 3 ตัวในลำดับ และ fail ด้วย error 7609 (ไม่มี Full-Text Search component บน Contabo) → `dotnet ef database update` ตายที่ตัวนี้เสมอ = schema ของ P2–P6 ลง DB จริงไม่ได้เลย** · ทางเลือก: (ก) ติดตั้ง FTS component บน VPS แล้ว apply ทั้งชุด [เจ้าของเลือกไว้แล้ว] (ข) ใส่ guard `IF SERVERPROPERTY('IsFullTextInstalled') = 1` ใน migration (แก้ได้เพราะยังไม่เคย apply สำเร็จ) แล้ว P1-06 ต้องมี fallback path · **ห้าม agent รัน `database update` เอง** | P0-03 | 0.5 | INFRA | BLOCK | OWN |
| P0-10 | Solution scaffold | สร้าง `SiriUpSkill.sln` + project ทั้งหมดตาม `ARCHITECTURE.md` §2 · `Directory.Build.props` (nullable, warnaserror, langversion) | — | 1 | BE | DONE | CC |
| P0-11 | SharedKernel | `Result<T>`, `DomainError` catalog, `IClock`, `IUserContext`, UUIDv7 helper, `PagedResult<T>` | P0-10 | 2 | BE | DONE | CC |
| P0-12 | Persistence baseline | `AppDbContext`, schema separation, audit interceptor (CreatedBy/UpdatedBy), soft-delete filter, migration bundle script | P0-11 | 2 | BE | DONE | CC |
| P0-13 | API host baseline | Serilog+OTel, ProblemDetails (RFC 9457), CORS, RateLimiter, health checks, OpenAPI, correlation id middleware | P0-11 | 2 | BE | DONE | CC |
| P0-14 | Identity domain + migration | Users/Roles/UserRoles/UserSessions/RefreshTokens/SecurityAudits ตาม `DATABASE.md` | P0-12 | 2 | BE | DONE | CC |
| P0-15 | สมัคร + ยืนยันอีเมล | password policy, hash, email confirm token หมดอายุ, rate limit, กัน user enumeration | P0-14, P0-19 | 2 | BE | DONE | CC |
| P0-16 | ล็อกอิน + refresh rotation | access 15 นาที (memory) + refresh 30 วัน (httpOnly cookie) + rotation + **reuse detection → revoke ทั้ง family** | P0-14 | 2.5 | BE | DONE | CC |
| P0-17 | **SE-03 Concurrent login control** | Redis session registry + mirror `UserSessions` · เกิน limit → revoke เก่าสุด + audit + แจ้งเตือน · limit ตั้งค่าได้ระดับระบบและรายบัญชี · Redis ล่ม = fail closed สำหรับ playback | P0-16 | 3 | BE | DONE | CC |
| P0-18 | API จัดการอุปกรณ์ | list session ของตัวเอง + revoke รายตัว/ทั้งหมด | P0-17 | 1 | BE | DONE | CC |
| P0-19 | Email outbox + provider | `EmailOutbox` + Hangfire sender + retry/backoff + template layout (ไทย) + provider adapter | P0-12 | 2 | BE | DONE | CC |
| P0-20 | Test harness | Testcontainers (MSSQL+Redis), `WebApplicationFactory`, data builder, ArchitectureTests (NetArchTest บังคับ module boundary) | P0-13 | 2 | QA | DONE | CC |
| P0-21 | ลืมรหัสผ่าน / รีเซ็ต | token ใช้ครั้งเดียว หมดอายุ 1 ชม. + revoke session ทั้งหมดหลังเปลี่ยนรหัส | P0-16 | 1 | BE | DONE | CC |
| P0-22 | Authorization policies | `AdminOnly`, `InstructorOnly`, `EnrolledInCourse`, `CourseOwner` + default deny ที่ระดับ group | P0-16 | 1 | BE | DONE | CC |
| P0-23 | Hangfire setup | SQL storage + dashboard ป้องกันด้วย auth + job retention | P0-12 | 1 | BE | DONE | CC |
| P0-30 | Angular workspace | Angular 22 + SSR + zoneless + Tailwind v4 + ESLint/Prettier + strict TS + path alias | — | 1.5 | FE | DONE | CC |
| P0-31 | **Design system ครั้งแรก** | ใช้ skill `ui-ux-pro-max` (ลง Python ก่อน) → เลือก palette + font ไทย + spacing/radius/shadow scale → `tokens.css` + dark mode + icon set | P0-30 | 3 | DESIGN | DONE | CC |
| P0-32 | Core FE | auth interceptor (แนบ token + refresh อัตโนมัติ + คิวคำขอระหว่าง refresh), error interceptor, global error handler, guard, i18n TH/EN | P0-30 | 2.5 | FE | DONE | CC |
| P0-33 | Layouts | public / learn / instructor / admin + header + footer + mobile nav + skip-to-content | P0-31 | 2 | FE | DONE | CC |
| P0-34 | UI kit ชุดแรก | button, input, select, checkbox, modal, toast, skeleton, empty state, pagination, breadcrumb — a11y ครบ (focus ring, aria) | P0-31 | 3 | FE | DONE | CC |
| P0-35 | หน้า auth | สมัคร / ล็อกอิน / ยืนยันอีเมล / ลืมรหัส / จัดการอุปกรณ์ | P0-34, P0-18 | 2.5 | FE | DONE | CC |
| P0-36 | E2E setup | Playwright + smoke: สมัคร→ล็อกอิน→ล็อกอินเครื่องที่ 3→เครื่องแรกหลุด | P0-35 | 1.5 | QA | PART | CC |
| P0-37 | Seed & dev bootstrap | seed role/admin/ผู้ใช้ทดสอบ + คำสั่งเดียวเซ็ตอัพเครื่อง dev | P0-14 | 1 | BE | DONE | CC |
| P0-39 | **[ใหม่ 2026-08-24] Commit ของค้าง + ทำให้ CI เขียวจริง** | ตอนนี้มีไฟล์ค้าง ~430 ไฟล์บน `feat/p2-p6-scaffold` และไม่มี commit เลยตั้งแต่ `40935d9` → (1) แยกไม่ออกว่างานไหนของ agent ตัวไหน (2) **CI (P0-07) ไม่เคยรันบนงาน P1–P6 เลย** ซึ่งเป็นที่เดียวที่มี Docker รัน integration test ได้ · ต้อง: commit แยกตาม task ID → push → CI เขียว (backend/frontend/secret-scan ครบ 3 job) · หลังจากนี้บังคับ **1 task = 1 commit** ข้อความ `feat(module): ... [P3-09]` | P0-07 | 1 | INFRA | TODO | OWN |

---

## P1 — Catalog, Search & SEO (สัปดาห์ 4–7)

**Exit criteria:** Lighthouse SEO ≥ 95 · LCP < 2.5s บน 4G · search p95 < 300ms · เปิดหน้า course detail จาก Google ได้และมี rich result

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P1-01 | Category tree | self-reference + slug + sort + admin CRUD + cache Redis + invalidate | P0-12 | 2 | BE | DONE | CC |
| P1-02 | Course/Section/Episode domain | entity + invariant (publish ต้องมี ≥1 episode ที่มี media) + migration | P1-01 | 3 | BE | DONE | CC |
| P1-03 | Instructor profile | สมัครเป็นผู้สอน + approve flow + `RevenueSharePercent` | P0-14 | 2 | BE | DONE | CC |
| P1-04 | Course CRUD (draft) | สร้าง/แก้/ลบ draft + slug generator (ไทย→latin) + ownership check | P1-02 | 2.5 | BE | DONE | CC |
| P1-05 | Publish workflow | Draft→InReview→Published/Rejected + validation rule + audit | P1-04 | 2 | BE | DONE | CC |
| P1-06 | Full-text search | FTS index + search API + filter (category/level/price/rating) + sort + facet count + pagination | P1-02 | 3 | BE | BLOCK | CC |
| P1-07 | Read model + cache | course list/detail projection + output cache + invalidate ตอน publish/แก้ราคา | P1-06 | 2 | BE | DONE | CC |
| P1-08 | Review & rating | เขียนรีวิวได้เมื่อ enroll แล้วเท่านั้น + `CourseStatsUpdater` ที่เดียว + job reconcile รายคืน — **[แก้ dep 2026-08-24] ปลดบล็อกแล้ว**: ตาราง `LEARNING.ENROLLMENTS` มีจริงแล้ว เช็คสิทธิ์ผ่าน `Learning.Contracts.ILearningAccessContract` (ห้าม reference `Learning.Domain` ตรง) | P1-02, P3-08 | 2.5 | BE | TODO | — |
| P1-09 | SEO backend | sitemap.xml (แบ่งหน้า), robots.txt, redirect table | P1-07 | 1.5 | BE | DONE | CC |
| P1-10 | Search filter ตามผู้สอน | เพิ่ม filter `instructorId` ใน search API + facet ผู้สอน (delta จากสเปค SiriLearn 2026-08-19 — LX-01 ฉบับเต็ม) | P1-06 | 1 | BE | DONE | CC |
| P1-20 | Mega menu | หมวดหมู่หลายชั้น + keyboard nav + mobile drawer + a11y (`aria-expanded`) | P0-33, P1-01 | 2.5 | FE | DONE | CC |
| P1-21 | หน้า catalog + filter | filter sync กับ URL (กด back แล้วไม่รีเซ็ต) + infinite/pagination + skeleton + empty state | P1-06 | 3.5 | FE | DONE | CC |
| P1-22 | หน้า course detail | syllabus accordion, free preview badge, duration รวม, outcomes, instructor card, sticky ปุ่มซื้อบนมือถือ, รีวิว | P1-07 | 4 | FE | DONE | CC |
| P1-23 | หน้าแรก | hero, หมวดหมู่, คอร์สแนะนำ, คอร์สใหม่ (data จาก API ยังไม่ต้องต่อ CMS) | P1-22 | 2.5 | FE | DONE | CC |
| P1-24 | SEO service | title/meta/canonical/OG + JSON-LD (`Course`, `BreadcrumbList`, `AggregateRating`) + hreflang | P1-22 | 2 | FE | DONE | CC |
| P1-25 | Search UI | autocomplete + debounce + recent search + ไม่มีผลลัพธ์แนะนำทางไปต่อ | P1-21 | 2 | FE | TODO | — |
| P1-30 | Seed คอร์สตัวอย่าง | 3 หมวดหลัก + 20 คอร์ส + instructor 5 คน (ข้อมูลไทยจริง ไม่ใช่ lorem) | P1-05 | 1.5 | BE | DONE | CC |
| P1-31 | Performance budget ใน CI | Lighthouse CI + bundle size budget (initial ≤ 300KB gz) fail build เมื่อเกิน | P1-24 | 1.5 | QA | TODO | — |
| P1-32 | เทสต์ P1 | integration test search/filter + e2e เดินจากหน้าแรก→หมวด→คอร์ส | P1-22 | 2 | QA | TODO | — |

---

## P2 — Media & Secure Player (สัปดาห์ 8–11) 🔴 เสี่ยงสุด

> **สถานะจริง (audit 2026-08-24 — แทนที่โน้ต scaffold เดิมที่ล้าสมัยแล้ว):** ไม่เหลือ `NotImplementedException` แม้แต่จุดเดียวใน `MEDIA`/`LEARNING` — Antigravity implement ไปแล้วเกือบทั้งเฟส (ดูคอลัมน์ Status รายแถว) **แต่ยังไม่มี integration test สักตัวและยังไม่เคยรันกับ MSSQL/Redis จริง** · จุดที่ยังเป็นรูใหญ่: P2-05 (DRM license proxy ยังไม่มีเลย) และ P2-20 (ยังไม่มี Shaka Player จริง — ใช้ `<video>` ธรรมดา) ดู § "สถานะจริงของงานที่ยังไม่ปิด" ก่อนหยิบงานในเฟสนี้

**Exit criteria:** IDM / video downloader extension / `yt-dlp` / devtools ดึงไฟล์ไม่ได้ · เล่นได้ครบ browser matrix · resume ตำแหน่งถูกต้อง · watermark แสดงชื่อผู้เรียนจริง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P2-01 | **ตั้งค่า Bunny Stream + เคลียร์ข้อจำกัด** | เปิด library เสร็จแล้ว, DRM tier ตัดสินแล้วเป็น **MediaCage Basic** (ไม่ใช่ Widevine/FairPlay จริง — ดู `docs/DECISIONS.md` Q1) — เหลือ: token auth key · allowed referrer · geo rule · ปิด direct play · **ยืนยันว่า custom Shaka Player ทำงานร่วมกับ MediaCage Basic's "Embed View only" ได้จริงไหม ก่อนเริ่ม P2-20** (ยังไม่ทดสอบ) | — | 2 | INFRA | PART | OWN |
| P2-02 | `IVideoProvider` + Bunny adapter | create video, direct upload signature, get status, delete, signed playback URL (token hash) | P2-01 | 3 | BE | BUILT | AG |
| P2-03 | MediaAssets + upload session | ตาราง + API ขอ upload URL + webhook receiver (verify signature) + Hangfire poll สำรอง | P2-02 | 3 | BE | BUILT | AG |
| P2-04 | **Playback session API** | ตรวจ enrollment active + ไม่หมดอายุ + free preview + device limit + rate limit → ออก signed URL อายุ 2–5 นาที + watermark payload | P2-03, P0-17 | 3.5 | BE | BUILT | AG+CC |
| P2-05 | DRM license proxy | proxy ไป Bunny พร้อมตรวจสิทธิ์ซ้ำทุกครั้ง · client ห้ามคุย license server ตรง | P2-04 | 2.5 | BE | TODO | — |
| P2-06 | PlaybackSessions log + anomaly | log ทุกครั้งที่ออก token + job flag พฤติกรรมผิดปกติ (>30 ตอน/ชม., หลาย IP) | P2-04 | 2 | BE | PART | AG |
| P2-07 | Progress API | `EpisodeProgress` upsert + heartbeat batch + คำนวณ `ProgressPercent` + `WatchEvents` เข้า DB แยก | P2-04 | 2.5 | BE | BUILT | AG |
| P2-20 | Player component | ครอบ Shaka Player + DRM config + error mapping ที่อ่านรู้เรื่อง (ไม่ใช่ error code ดิบ) | P2-05 | 4 | FE | PART | AG |
| P2-21 | Player controls | speed 0.5–2.0x, quality/auto, volume, fullscreen, PiP, caption, keyboard (space/←/→/f/m), ปุ่มขนาด ≥44px | P2-20 | 3 | FE | PART | AG |
| P2-22 | **Dynamic watermark** | overlay ชื่อ+อีเมล+timestamp ขยับสุ่มทุก ~8 วิ, opacity ต่ำ, ไม่บังเนื้อหาสำคัญ, ลบด้วย devtools แล้ววิดีโอต้องหยุด (timestamp = delta จากสเปค SiriLearn — payload มาจาก server ห้าม client กำหนดเอง) | P2-20 | 2.5 | FE | BUILT | AG |
| P2-23 | Resume + heartbeat | จำตำแหน่ง, ถามผู้ใช้ว่าจะเล่นต่อไหม, heartbeat ทุก 15 วิ, offline-safe (queue แล้วส่งซ้ำ) | P2-07 | 2.5 | FE | BUILT | AG |
| P2-24 | หน้า /learn | curriculum sidebar + สถานะดูแล้ว + ไปตอนถัดไปอัตโนมัติ + progress bar + responsive มือถือ | P2-21 | 3.5 | FE | BUILT | AG |
| P2-25 | Free preview flow | ดูตัวอย่างได้โดยไม่ต้องซื้อ + CTA ชวนซื้อเมื่อจบตัวอย่าง | P2-24 | 1.5 | FE | PART | AG |
| P2-30 | Browser matrix test | Chrome/Edge/Firefox/Safari macOS/Safari iOS/Android Chrome — บันทึกผลเป็นตาราง + ระบุ fallback | P2-24 | 2 | QA | TODO | — |
| P2-31 | **Anti-piracy pen-test** | ลองดึงด้วย IDM, extension ยอดนิยม, `yt-dlp`, copy manifest URL ไปเปิดที่อื่น, devtools network — **ต้องล้มเหลวทุกทาง** ถ้าทางใดสำเร็จถือว่า P2 ยังไม่ผ่าน ⚠️ **DRM tier จริงคือ MediaCage Basic (clear-key, ไม่ใช่ Widevine/FairPlay — `docs/DECISIONS.md` Q1) แปลว่า bar นี้ทำได้เต็มร้อยจริงไหมยังไม่ยืนยัน** — รันเทสต์เต็มรูปแบบแล้วรายงานผลตรง ๆ ถ้ามีทางใดสำเร็จ ห้ามปิดจุดนั้นด้วยการลด scope ของเทสต์นี้เอง ต้องรายงานเจ้าของโปรเจ็คแล้วตัดสินใจ (อัปเกรด Enterprise / เสริม watermark-rate-limit / ยอมรับความเสี่ยง) — ไม่ใช่ทำเงียบ ๆ | P2-30 | 2 | QA | BLOCK | — |
| P2-32 | Analytics rollup job | `WatchEvents` → `analytics.EpisodeDropOff` + `DailyCourseStats` รายคืน + purge > 90 วัน | P2-07 | 2 | BE | BUILT | AG |

---

## P3 — Commerce & Stripe Payment (สัปดาห์ 12–14)

> แก้ไข 2026-08-18: เปลี่ยนจาก EasySlip เป็น **Stripe** ทั้ง phase (D-14/Q2 ฉบับแก้ไข — สเปคเต็ม `PAYMENT.md`) ไม่มี flow อัปสลิปอีกต่อไป

> **สถานะจริง (audit 2026-08-24 — แทนที่โน้ต scaffold เดิม):** Stripe adapter/webhook/order/cart/refund/tax-invoice implement แล้วจริง (`Stripe.net` 52.3.0 อยู่ใน `Directory.Packages.props`) `IPaymentMethod` ถูกออกแบบใหม่แล้วตามที่วางไว้ (`Siri.Integrations.Payment/Stripe/`) — ที่ยังขาดคือ job หมดอายุออร์เดอร์ (P3-03), promo redemption (P3-09), ops queue ฝั่ง admin (P3-07) และ FE ตะกร้า/ประวัติคำสั่งซื้อ (P3-20/23) ดู § "สถานะจริงของงานที่ยังไม่ปิด"

**Exit criteria:** ซื้อ→สแกน QR→จ่ายในแอปธนาคาร→เข้าเรียนได้อัตโนมัติผ่าน webhook · webhook ยิงซ้ำ/ปลอมไม่มีผลใด ๆ 100% · ออร์เดอร์ไม่มีสถานะค้าง

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P3-01 | Stripe PaymentIntent integration | Stripe.net adapter หลัง `IPaymentMethod` + สร้าง PaymentIntent (thb, promptpay, amount หน่วยสตางค์) + Options+ValidateOnStart + key อยู่ใน user-secrets/env เท่านั้น + retry/backoff ตอนเรียก Stripe | — | 2 | BE | BUILT | AG |
| P3-02 | Cart + Order domain | ตาราง + pricing engine ที่ server (ราคา, ส่วนลด, ยอดรวม) + กันซื้อคอร์สที่ซื้อแล้วซ้ำ | P1-02 | 3 | BE | BUILT | AG |
| P3-03 | Order lifecycle | AwaitingPayment 30 นาที + job หมดอายุ + cancel PaymentIntent ที่ Stripe + จ่ายสำเร็จหลังหมดอายุต้องเปิดออร์เดอร์ให้ใหม่หรือ refund (ห้ามกลืนเงิน) | P3-02, P3-01 | 2 | BE | PART | AG |
| P3-04 | **Stripe webhook endpoint** | ตรวจ `Stripe-Signature` ทุก request + idempotent (**unique index** บน StripeEventId) + เก็บ raw event JSON + จัดการ succeeded/payment_failed/canceled ใน transaction เดียวกับ order state + rate limit | P3-01, P3-03 | 2.5 | BE | BUILT | AG |
| P3-05 | Refund flow | Stripe refund API + track สถานะ async (Stripe ขอเลขบัญชีจากผู้ซื้อเอง — refund ค้างได้) + audit ทุกรายการ | P3-04 | 2 | BE | BUILT | AG |
| P3-06 | Payment state machine | Pending→Processing→Succeeded/Failed/Expired/Refunded ใน transaction เดียว · กัน race ระหว่าง webhook กับ expiry job · audit ทุกการเปลี่ยนสถานะ | P3-04 | 2 | BE | PART | AG |
| P3-07 | Payment ops queue | คิวกรณีผิดปกติ (จ่ายซ้ำ/จ่ายหลังหมดอายุ/refund ค้าง) + admin resolve + audit + แจ้งผู้ซื้อ | P3-06 | 1.5 | BE | PART | AG |
| P3-08 | Auto-enroll + ใบเสร็จ | outbox event → สร้าง enrollment (คิด `ExpiresAtUtc` จาก `AccessDurationDays`) + ออกใบกำกับภาษี e-Tax ผ่าน `TaxInvoiceService` ที่ scaffold ไว้แล้ว (VAT 7%, ตาราง `TaxInvoices` จาก D-17 gap-fill) + อีเมลใบเสร็จ/e-Tax + in-app notification | P3-06 | 2.5 | BE | BUILT | AG+CC |
| P3-09 | Promo code | สร้าง/ตรวจ/ใช้ + redemption แบบ atomic ที่ระดับ SQL + จำกัดต่อผู้ใช้ | P3-02 | 2.5 | BE | DONE | AG |
| P3-10 | Reconcile job | list PaymentIntents จาก Stripe API รายวันเทียบกับ Orders + จับ webhook ที่หลุด + รายงานรายการที่ไม่แมตช์ | P3-08 | 2 | BE | BUILT | AG |
| P3-20 | ตะกร้า | เพิ่ม/ลบ, สรุปยอด, ใส่โค้ดส่วนลด, persist ข้ามอุปกรณ์ | P3-02 | 2 | FE | TODO | — |
| P3-21 | หน้า checkout + QR | แสดง QR จาก `next_action.promptpay_display_qr_code` ใหญ่ชัดบนมือถือ + นับถอยหลัง + วิธีจ่ายทีละขั้นเป็นภาษาไทย + แจ้งว่า statement ขึ้นชื่อ Stripe | P3-01, P3-02, P3-03 | 3 | FE | BUILT | AG |
| P3-22 | สถานะการจ่ายแบบ real-time | polling สถานะผ่าน backend (ห้ามตัดสินจาก client) + ข้อความแต่ละสถานะชัดเจน (รอจ่าย/สำเร็จ/หมดเวลา/คืนเงิน) | P3-04 | 2 | FE | BUILT | AG |
| P3-23 | ประวัติคำสั่งซื้อ | รายการ + รายละเอียด + สถานะจ่าย/คืนเงิน + ดาวน์โหลดใบเสร็จ | P3-08 | 2 | FE | TODO | — |
| P3-24 | คอร์สของฉัน | เรียงตามล่าสุด + ความคืบหน้า + ปุ่มเรียนต่อ + วันหมดอายุ | P3-08 | 2 | FE | BUILT | AG |
| P3-25 | **[ใหม่ mockup 2026-08-21]** หน้าชำระเงินสำเร็จ | หน้าแยกหลังจ่ายเงินสำเร็จ (ไม่ใช่แค่ status inline ใน P3-22): สรุปคำสั่งซื้อ+วิธีจ่าย+ยอด, ยืนยันว่าส่ง e-Tax invoice ไปอีเมลแล้ว, การ์ดคอร์สที่ซื้อพร้อมปุ่ม "เริ่มเรียน" ต่อคอร์ส, ดาวน์โหลดใบเสร็จ PDF, ลิงก์ไปหน้าการเรียนของฉัน | P3-08 | 1.5 | FE | PART | AG |
| P3-30 | หน้า admin payment ops | รายการจ่ายทั้งหมด + สถานะจาก Stripe + ops queue + สั่ง refund + audit trail | P3-07 | 2 | FE | TODO | — |
| P3-31 | เทสต์ P3 | e2e ซื้อ→จ่าย (Stripe test mode)→เข้าเรียน · webhook ยิงซ้ำ · signature ปลอม · จ่ายแข่งกับ expiry · refund flow | P3-30 | 3 | QA | TODO | — |

---

## P4 — Instructor Studio (สัปดาห์ 15–18)

> **สถานะจริง (แก้ 2026-08-25):** **P4-01 ปิดแล้ว** (`DONE` — Antigravity ทำ, Claude Code review ผ่าน: endpoint ครบ 10 ตัว, ownership ครบทุกจุด, optimistic concurrency คืน 409 จริง, integration test 12 ตัวใน `CourseBuilderTests.cs`) หน้า builder เซฟของจริงได้แล้ว · **ที่ยังเหลือใน P4:** `P4-04` (analytics API ยังไม่มีเลย → หน้า analytics ยังเป็น mock), `P4-21` (UI อัปโหลด), `P4-25` (onboarding ผู้สอน) และรูใน P4-20 ที่ review เจอ (ดู § "สถานะจริงของงานที่ยังไม่ปิด") · **P4-26/P4-27 dep ข้ามไป P6-08/P6-05 ซึ่ง `BUILT` แล้ว จึงไม่ติดลำดับเฟสอีกต่อไป**

**Exit criteria:** instructor สร้างคอร์ส 10 ตอนจนขึ้นขายได้เอง โดยไม่ต้องให้ dev ช่วย

| ID | Task | รายละเอียด | Dep | Est | Track | Status | Own |
|----|------|-----------|-----|-----|-------|--------|-----|
| P4-01 | Course builder API | reorder section/episode แบบ batch + optimistic concurrency + autosave endpoint | P1-04 | 3 | BE | DONE | AG |
| P4-02 | Upload flow API | ขอ upload URL, ติดตามสถานะ, ยกเลิก, แทนที่ไฟล์ | P2-03 | 2 | BE | BUILT | AG |
| P4-03 | Attachment API | อัปไฟล์แนบต่อ episode + จำกัดชนิด/ขนาด + สแกนไวรัส | P4-02 | 2 | BE | PART | AG |
| P4-04 | Instructor analytics API | ยอดขาย/ช่วงเวลา, นักเรียน active, drop-off ต่อ episode, คอร์สขายดี (อ่านจาก `analytics.*`) | P2-32 | 3 | BE | TODO | — |
| P4-20 | Builder UI — โครงสร้าง | drag & drop (Angular CDK) + **ย้ายด้วยคีย์บอร์ดได้** + undo + autosave indicator | P4-01 | 5 | FE | PART | AG |
| P4-21 | Builder UI — อัปโหลด | อัปหลายไฟล์, progress, resumable, retry, สถานะ transcode | P4-02 | 3.5 | FE | TODO | — |
| P4-22 | Builder UI — ตั้งค่าคอร์ส | ราคา, หมวด, ระดับ, outcomes, requirements, thumbnail, SEO, ตั้ง free preview | P4-20 | 3 | FE | PART | AG |
| P4-23 | หน้า analytics | กราฟยอดขาย + drop-off chart (ECharts) + ตารางนักเรียน + export CSV | P4-04 | 3.5 | FE | PART | AG |
| P4-24 | หน้า dashboard ผู้สอน | สรุปรายได้เดือนนี้, คอร์สรอตรวจ, คำถามที่ยังไม่ตอบ | P4-23 | 2 | FE | BUILT | AG |
| P4-25 | Onboarding ผู้สอน | สมัครเป็นผู้สอน + กรอกข้อมูล + สถานะรออนุมัติ | P1-03 | 2 | FE | TODO | — |
| P4-26 | **[ใหม่ mockup 2026-08-21]** หน้ารายได้ผู้สอน | สรุป 70/30 + รอบจ่ายวันที่ 25, การ์ดสถิติ (รายได้สะสม/รอบล่าสุด/ประมาณการรอบถัดไป), ตั้งค่า/แก้ไขบัญชีรับเงิน (เลขบัญชีต้อง mask เสมอ), ตารางประวัติการจ่ายรายรอบ | P6-08 | 2.5 | FE | BUILT | AG |
| P4-27 | **[ใหม่ mockup 2026-08-21]** หน้าประกาศผู้สอน | เลือกคอร์สของตัวเอง → หัวข้อ+ข้อความ+checkbox ส่งอีเมล → ส่งประกาศ, รายการประกาศที่ผ่านมา | P6-05 | 2 | FE | BUILT | AG |
| P4-30 | Usability test | ทดสอบกับ instructor จริง 3 คน แล้วแก้ตามผล | P4-22 | 2 | QA | TODO | — |
| P4-31 | เทสต์ P4 | e2e สร้างคอร์สครบจนขึ้นขาย + คลุมหน้ารายได้ผู้สอน/ประกาศผู้สอนที่เพิ่มเข้ามา (P4-26/27) | P4-22 | 2 | QA | TODO | — |

---

## P5 — Interactive Learning (สัปดาห์ 19–21)

> **สถานะจริง (audit 2026-08-24 — แทนที่โน้ต scaffold เดิม):** Quiz/Assignment/Certificate ฝั่ง BE implement ครบแล้ว (รวม QuestPDF + verify code + QR) **แต่ฝั่ง FE ยังไม่มีหน้า quiz/assignment/Q&A/attachment เลยสักหน้า** (P5-20/21/23/24) — เฟสนี้เหลืองาน FE เป็นหลัก

**Exit criteria:** เรียนจบแล้วได้ certificate อัตโนมัติ ตรวจสอบย้อนกลับได้จากหน้า verify สาธารณะ

| ID | Task | รายละเอียด | Dep | Est | Track | Status | Own |
|----|------|-----------|-----|-----|-------|--------|-----|
| P5-01 | Quiz domain + API | quiz/question/option/attempt + จำกัดจำนวนครั้ง + คำนวณคะแนน + เฉลย | P1-02 | 3.5 | BE | BUILT | AG |
| P5-02 | Assignment + grading API | ส่งงาน, ไฟล์แนบ, instructor ให้คะแนน + feedback | P4-03 | 3 | BE | BUILT | AG |
| P5-03 | Certificate | เงื่อนไขจบคอร์ส → สร้าง PDF (QuestPDF) + serial + verify code + **QR code บนใบ cert ชี้ไปหน้า verify** + หน้า verify สาธารณะ (QR = delta จากสเปค SiriLearn 2026-08-19) | P2-07 | 3 | BE | BUILT | AG |
| P5-04 | Q&A API | thread ใต้ episode + ตอบกลับ + upvote + badge ผู้สอน + report/moderation | P1-02 | 3 | BE | BUILT | AG |
| P5-20 | Quiz UI | ทำข้อสอบ, ตัวจับเวลา (ถ้ามี), ผลลัพธ์ + เฉลย, ทำใหม่ | P5-01 | 3 | FE | TODO | — |
| P5-21 | Assignment UI | อัปไฟล์, ดูสถานะ, ดูคะแนน + feedback | P5-02 | 2.5 | FE | TODO | — |
| P5-22 | Certificate UI | ปุ่มดาวน์โหลด + แชร์ LinkedIn + หน้า verify | P5-03 | 2 | FE | PART | AG |
| P5-23 | Q&A UI | thread ใต้ player, ตอบ, mention, แจ้งเตือน, รายงานเนื้อหา | P5-04 | 3.5 | FE | TODO | — |
| P5-24 | Attachment UI | ดาวน์โหลดไฟล์แนบผ่าน signed URL | P4-03 | 1 | FE | TODO | — |
| P5-30 | เทสต์ P5 | e2e เรียนจบคอร์ส→ได้ certificate→verify ได้ | P5-22 | 2 | QA | TODO | — |

---

## P6 — Admin, CMS & Revenue (สัปดาห์ 22–25)

> **สถานะจริง (audit 2026-08-24 — แทนที่โน้ต scaffold เดิม):** CMS/announcement/admin-ops/dashboard-summary/earnings implement แล้ว · **สองจุดที่ยังเป็นปัญหาจริง:** (1) P6-03 revenue split hardcode 70/30 ทั้งที่ Q4 ยังไม่ตอบ (2) P6-04 payout batch ไม่เคยรวมยอดจริง (`AddItem()` ไม่ถูกเรียกจากที่ไหนเลย) · ฝั่ง FE หน้า admin 4 หน้า (P6-21/23/26/27) ยังเป็น mock ไม่ต่อ API ทั้งที่ BE พร้อมแล้ว ดู § "สถานะจริงของงานที่ยังไม่ปิด"

**Exit criteria:** ทีมการตลาดจัดโปรหน้าแรกได้เองโดยไม่ต้อง deploy · ปิดยอด payout เดือนแรกได้

| ID | Task | รายละเอียด | Dep | Est | Track | Status | Own |
|----|------|-----------|-----|-----|-------|--------|-----|
| P6-01 | CMS API | banner, menu, page, blog post + sanitize HTML ฝั่ง server (allowlist) + SEO field + redirect | P0-12 | 4 | BE | BUILT | AG |
| P6-02 | Flash sale + bundle | ตั้งเวลาเริ่ม/จบ + ราคาพิเศษ + bundle หลายคอร์ส + ลำดับความสำคัญของส่วนลด | P3-09 | 3 | BE | PART | AG |
| P6-03 | **Revenue split** | คำนวณตอนออร์เดอร์ Paid + snapshot % + หัก payment fee ตาม Q4 + reversal เมื่อ refund | P3-08 | 3 | BE | BLOCK | AG |
| P6-04 | Payout batch | ปิดรอบรายเดือน + สร้าง batch + หัก withholding tax + export ไฟล์โอน + mark paid | P6-03 | 3 | BE | PART | AG |
| P6-05 | Announcement + bulk email | **[แก้ไข mockup 2026-08-21 — ยืนยันเป็นเครื่องมือของผู้สอนเอง ไม่ใช่แอดมิน]** ผู้สอนส่งประกาศถึงผู้เรียนที่ enroll ในคอร์สของตัวเองเท่านั้น (ownership check: `course.InstructorId == caller` — ไม่ใช่ endpoint ระดับแอดมิน) + ตั้งเวลา + ผ่าน outbox + unsubscribe + list ประกาศที่เคยส่งของคอร์สนั้น ⚠️ ต้องสร้างตาราง `notify.Announcements`/`Notifications` เอง (สเก็ตช์มีใน `DATABASE.md` แล้วแต่ D-17 ไม่ได้ scaffold — `notify` เป็น vertical slice เดิม ไม่ใช่ 7 โมดูล UPPERCASE) — "เปิดอ่าน %" ที่เห็นใน mockup ต้องการ email-open tracking ซึ่งยังไม่มีกลไกรองรับจริง (SMTP adapter ปัจจุบันยังไม่ตัดสิน vendor) **ถ้าเป็นไปไม่ได้ในรอบนี้ให้ตัดฟีลด์นี้ออกจาก UI ไปก่อน อย่าใส่เลขปลอม** | P0-19 | 2.5 | BE | BUILT | AG |
| P6-06 | Admin ops API | user management (ค้นหา/filter role/suspend-reactivate), course moderation, refund, audit log viewer, feature flag | P0-22 | 3 | BE | PART | AG |
| P6-07 | **[ใหม่ mockup 2026-08-21]** Admin dashboard summary API | ยอดขายวันนี้ (live query Orders/Payments ไม่ใช่ rollup), ผู้เรียนสมัครใหม่วันนี้+รวมทั้งหมด, จำนวนคอร์สรออนุมัติ, จำนวนคำขอคืนเงินรออนุมัติ, คอร์สขายดี 30 วัน (จาก `analytics.DailyCourseStats` rollup) — ⚠️ ข้าม module boundary (Identity/Catalog/Commerce/Analytics) ต้องออกแบบผ่าน Contracts ของแต่ละโมดูลตาม `ARCHITECTURE.md` ห้าม reference `Domain`/`Infrastructure` ข้ามโมดูลตรง ๆ | P6-06 | 2 | BE | BUILT | AG |
| P6-08 | **[ใหม่ mockup 2026-08-21]** Instructor earnings query API | GET รายได้สะสม/รอบล่าสุด/ประมาณการรอบถัดไป/ประวัติการจ่ายของผู้สอนที่ login อยู่เท่านั้น (ownership: filter ด้วย `InstructorId` จาก `IUserContext`) อ่านจาก `RevenueSplits`/`PayoutBatchItems` ที่มีอยู่แล้ว | P6-04 | 1.5 | BE | BUILT | AG |
| P6-20 | Admin — CMS | จัด banner (drag), เมนู, blog editor + preview + จัดการรูป | P6-01 | 5 | FE | PART | AG |
| P6-21 | Admin — marketing | promo code, flash sale, bundle + ปฏิทินแคมเปญ | P6-02 | 3.5 | FE | PART | AG |
| P6-22 | Admin — revenue | รายงานรายได้, split ต่อ instructor, payout batch, export **+ คิวอนุมัติคำขอคืนเงิน** (mockup 2026-08-21: แดชบอร์ดลิงก์ "คำขอคืนเงินรออนุมัติ" มาที่หน้านี้ ไม่ใช่หน้าแยก) | P6-04 | 4 | FE | PART | AG |
| P6-23 | **[แก้ไข mockup 2026-08-21 — แยกจาก users&courses เดิม]** Admin — อนุมัติคอร์ส | คิวคอร์สรออนุมัติ (badge จำนวน), การ์ดต่อคอร์ส (thumbnail, ป้าย "ส่งครั้งแรก"/"แก้ไขรอบ N", ผู้สอน, เวลาส่ง, จำนวนบท/ชม./ราคา), ปุ่มดูตัวอย่าง/ส่งกลับแก้ไข/อนุมัติเผยแพร่ — **backend พร้อมแล้วจาก P1-05 ทำได้ทันทีไม่ต้องรอ P6-06** | P0-22 | 2 | FE | PART | AG |
| P6-24 | หน้า blog สาธารณะ | list + detail + SEO + related + share | P6-01 | 2.5 | FE | BUILT | AG |
| P6-25 | ผูกหน้าแรกกับ CMS | หน้าแรกดึง banner/section จาก CMS แทน hardcode | P6-01 | 1.5 | FE | TODO | — |
| P6-26 | **[ใหม่ mockup 2026-08-21]** Admin — แดชบอร์ดภาพรวม | การ์ดสถิติ 4 ตัว (ยอดขายวันนี้/ผู้เรียนใหม่วันนี้/คอร์สรออนุมัติ/คำขอคืนเงินรออนุมัติ — ลิงก์ไปหน้าที่เกี่ยวข้อง), รายการที่ต้องจัดการ (action item feed), ตารางคอร์สขายดี 30 วัน | P6-07 | 2 | FE | PART | AG |
| P6-27 | **[ใหม่ mockup 2026-08-21]** Admin — ผู้ใช้และสิทธิ์ | ค้นหา+filter ตาม role, ตาราง (ชื่อ/อีเมล/role/สถานะ/เข้าใช้ล่าสุด), เชิญผู้ใช้, action ระงับ/คืนสิทธิ์/เปลี่ยน role ต่อแถว — พ่วง audit log viewer (P6-06) เป็น tab รอง เพราะ mockup ไม่มีหน้าแยกสำหรับ audit log | P6-06 | 3 | FE | PART | AG |
| P6-30 | เทสต์ P6 | e2e จัดโปร + ตรวจว่าราคาที่ผู้ซื้อเห็นถูกต้องตามลำดับส่วนลด + คลุมแดชบอร์ด/อนุมัติคอร์ส/ผู้ใช้ที่เพิ่มเข้ามา (P6-26/27) | P6-21 | 3 | QA | TODO | — |

---

## P7 — Hardening & Launch (สัปดาห์ 26–28)

| ID | Task | รายละเอียด | Dep | Est | Track | Status | Own |
|----|------|-----------|-----|-----|-------|--------|-----|
| P7-01 | Load test | k6: 5,000 concurrent viewer, 500 checkout/นาที, search burst — หา bottleneck แล้วแก้ | ทุก phase | 3 | QA | TODO | — |
| P7-02 | Security review | OWASP Top 10, IDOR sweep ทุก endpoint, dependency scan, secret scan | — | 3 | BE | TODO | — |
| P7-03 | Pen-test ภายนอก | จ้างภายนอก + ปิดช่อง high/critical ครบ | P7-02 | 3 | QA | TODO | — |
| P7-04 | PDPA | consent banner, privacy/cookie policy, export/ลบข้อมูลตัวเอง (anonymize), data retention job, DPA กับ vendor | — | 3 | BE | TODO | — |
| P7-05 | ปรับ index + query | ตรวจ query ช้าจาก production trace แล้วเพิ่ม index | P7-01 | 2 | BE | TODO | — |
| P7-06 | Observability + runbook | dashboard, alert rule, runbook เหตุการณ์ที่พบบ่อย, on-call | P0-06 | 2 | INFRA | TODO | — |
| P7-07 | DR drill | ซ้อมกู้จาก backup แบบเต็ม + จับเวลา RTO/RPO จริง | P0-05 | 1.5 | INFRA | TODO | — |
| P7-08 | a11y audit | axe + คีย์บอร์ดทั้งระบบ + screen reader ตัวอย่างหน้าหลัก | — | 2.5 | QA | TODO | — |
| P7-09 | Content & legal | เงื่อนไขการใช้งาน, นโยบายคืนเงิน, เนื้อหาหน้า static, ตรวจข้อความไทยทั้งระบบ | — | 2 | — | TODO | — |
| P7-10 | Closed beta | ผู้ใช้จริง 50 คน 1 สัปดาห์ + เก็บ feedback + แก้ | P7-03 | 5 | — | TODO | — |
| P7-12 | **[ใหม่ 2026-08-24] ตั้ง secret production ครบก่อน go-live** | `DataProtection:EncryptionKeyBase64` ตอนนี้ยังเป็น dev placeholder ใน `appsettings.json` — ถ้าขึ้น prod ทั้งอย่างนี้ เลขบัญชี/เลขผู้เสียภาษีของผู้สอนจะถูกเข้ารหัสด้วยคีย์ที่อยู่ในไฟล์ที่ commit (เท่ากับไม่ได้เข้ารหัส) · ตั้งจริงด้วย `openssl rand -base64 32` ผ่าน env/Key Vault · พร้อมกัน: Stripe live key, Bunny `CdnHostname`/API key, SMTP vendor, `Cors:AllowedOrigins` ของ domain จริง, `Seo:PublicBaseUrl` | P7-02 | 0.5 | INFRA | TODO | OWN |
| P7-11 | Go-live | DNS, cert, monitoring, rollback plan, ประกาศ | P7-10 | 1 | INFRA | TODO | — |

---

## P8 — Growth & Engagement (หลัง launch — LX-08/09/10/11)

> เพิ่มจากสเปค SiriLearn (D-16, 2026-08-19) — ทั้ง phase ไม่บล็อก launch v1 · ลำดับ P8/P9/P10 สลับได้ตามธุรกิจ
> **ก่อนเริ่ม P8-01:** เจ้าของโปรเจ็คต้องสมัคร OAuth app เอง (Google Cloud Console, Meta for Developers, Apple Developer — Apple มีค่าสมาชิกรายปี) แล้วเก็บ client id/secret เข้า user-secrets/env

**Exit criteria:** ล็อกอินด้วย Google ได้จริง + จดโน้ตแล้วคลิกกลับไป seek ได้ + จบ quiz แล้ว XP ขึ้นครั้งเดียว (ยิงซ้ำไม่ขึ้นซ้ำ) + leaderboard แสดงผล rollup ไม่ query สด

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P8-01 | Social login backend | OAuth Google/Facebook/Apple + ตาราง `identity.UserExternalLogins` · ผูกกับบัญชีเดิมได้เฉพาะ email ที่ **ยืนยันแล้ว** (กัน account takeover ผ่าน email ปลอม) · บัญชีใหม่จาก social = confirmed ทันที · endpoint start/callback เข้า rate limit policy "auth" เดิม (backend.md บังคับ endpoint กลุ่ม login) · ไม่แตะ password/refresh flow เดิม | P0-16 | 4 | BE | TODO | — |
| P8-02 | Social login UI | ปุ่ม 3 เจ้าบนหน้า login/register + จัดการบัญชีที่ผูกไว้ (ดู/ถอด) ในหน้า account | P8-01 | 2 | FE | TODO | — |
| P8-03 | Timestamp notes API | CRUD โน้ตผูก (Enrollment, Episode, PositionSeconds) + paginate + ownership check ทุก query (โน้ตเป็นของผู้เรียนคนเดียว) | P2-07 | 2 | BE | TODO | — |
| P8-04 | Timestamp notes UI | sidebar ใน player: เพิ่มโน้ตจากตำแหน่งปัจจุบัน, คลิกโน้ต → seek, แก้/ลบ | P8-03, P2-24 | 2.5 | FE | TODO | — |
| P8-05 | Learning Path domain + API | path = ลำดับคอร์ส (admin CRUD) + progress รวมทั้ง path ต่อผู้ใช้ + หน้า public (SEO) | P1-02 | 3 | BE | BUILT | AG |
| P8-06 | Learning Path UI | หน้า list/detail + สถานะคอร์สถัดไปที่ควรเรียน + SSR/JSON-LD | P8-05 | 3 | FE | TODO | — |
| P8-07 | Gamification domain | XP event (จบ episode/ผ่าน quiz/จบคอร์ส) เขียนผ่าน updater ตัวเดียว + `UQ(UserId, SourceType, SourceRefId)` กันแต้มซ้ำระดับ DB + leaderboard rollup รายคืน (Hangfire) | P2-07, P5-01 | 4 | BE | TODO | — |
| P8-08 | XP + Leaderboard UI | XP บนโปรไฟล์ + หน้า leaderboard (รายเดือน/ตลอดกาล) + ตั้งค่า opt-out ไม่แสดงชื่อ | P8-07 | 2.5 | FE | TODO | — |
| P8-30 | เทสต์ P8 | e2e social login (OAuth test app) · โน้ต seek ถูกตำแหน่ง · replay XP event ไม่ได้แต้มซ้ำ (พิสูจน์ที่ระดับ DB) | P8-08 | 2.5 | QA | TODO | — |

---

## P9 — B2B Corporate Portal (หลัง launch — CO-01..04)

> เพิ่มจากสเปค SiriLearn (D-16, 2026-08-19) · **ติดการตัดสินใจ Q7** (โมเดลการขาย/คิดเงิน B2B) — ต้องตอบก่อนเริ่ม P9-03
> module ใหม่ `Siri.Modules.Corporate` (schema `corporate`) — ตาราง sketch อยู่ `DATABASE.md`

**Exit criteria:** HR import พนักงาน 100 คนจาก Excel สำเร็จ (แถวผิดมีรายงาน ไม่ล้มทั้งไฟล์) · assign คอร์สบังคับแล้วพนักงานเห็น deadline · HR เห็นรายงานเฉพาะ org ตัวเองเท่านั้น (IDOR test ข้าม org ต้องล้มเหลว 100%)

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P9-01 | Corporate module + org domain | `Siri.Modules.Corporate` ใหม่ตาม module boundary เดิม + Organizations/OrgMembers · `HRAdmin`/`CorporateLearner` เป็น **org role บน `OrgMembers.OrgRole` เท่านั้น ไม่ใช่ global role ใน `identity.Roles`** (สิทธิ์ HR ต้องผูกกับ org เดียว — global role แสดง "admin ของ org ไหน" ไม่ได้) + policy `OrgAdminOnly` ตัดสินจาก membership ของ org ใน route | P0-14 | 3 | BE | TODO | — |
| P9-02 | Excel import พนักงาน | parse xlsx ฝั่ง server (ตรวจ magic bytes + จำกัดขนาด) + validate รายแถว + รายงาน error รายแถว + invite ผ่าน `notify.EmailOutbox` + idempotent (import ซ้ำไม่สร้างซ้ำ) | P9-01 | 3 | BE | TODO | — |
| P9-03 | Org course license + assign | org ได้สิทธิ์คอร์ส (v-แรก: invoice-based, Superadmin activate เอง — รอ Q7) → HR assign ให้พนักงาน → สร้าง enrollment `Source=Corporate` · **นับที่นั่งกับ `SeatCount` แบบ atomic ที่ระดับ SQL** (ห้ามอ่านมาเช็คใน memory — database.md ระบุกรณีที่นั่งไว้ตรง ๆ) · ห้ามชน/ทับ enrollment ที่ผู้ใช้ซื้อเอง · license ใช้ Status (revoke ≠ ลบแถว) | P9-01, P3-08 | 3 | BE | TODO | — |
| P9-04 | Mandatory training | ตั้งคอร์สบังคับ + deadline ต่อคน/กลุ่ม + สถานะ (ยังไม่เริ่ม/กำลังเรียน/เสร็จ/เลยกำหนด) + แจ้งเตือนอัตโนมัติผ่าน outbox | P9-03 | 2.5 | BE | TODO | — |
| P9-05 | HR reporting API | ความคืบหน้ารายคน/แผนก/คอร์ส + filter + paginate + **ทุก query กรองด้วย org ของ caller เท่านั้น** (org id มาจาก membership ไม่ใช่ request) | P9-03 | 3 | BE | TODO | — |
| P9-06 | Export Excel/PDF | สร้างไฟล์ผ่าน Hangfire (ไฟล์ใหญ่ไม่บล็อก request) → แจ้งลิงก์ดาวน์โหลด signed URL อายุสั้น | P9-05 | 2 | BE | TODO | — |
| P9-20 | HR portal UI | dashboard + จัดการพนักงาน/กลุ่ม + assign คอร์ส + รายงาน + export — CSR + `noindex` เหมือน `/admin` | P9-05 | 5 | FE | TODO | — |
| P9-21 | Corporate learner UX | ป้าย "คอร์สบังคับ" + deadline ใน my-courses + แจ้งเตือนใกล้ครบกำหนด | P9-04 | 1.5 | FE | TODO | — |
| P9-30 | เทสต์ P9 | IDOR ข้าม org ทุก endpoint · import ซ้ำ idempotent · e2e assign→เรียน→รายงานอัปเดต | P9-20 | 3 | QA | TODO | — |

---

## P10 — Subscription (หลัง launch — LX-12)

> เพิ่มจากสเปค SiriLearn (D-16, 2026-08-19) · **ติดการตัดสินใจ Q6** — คำถามย่อย opt-in vs บังคับทุกคอร์ส ต้องตอบก่อนเริ่ม P10-02, สูตรแบ่งรายได้ต้องตอบก่อนเริ่ม P10-05
> **Prerequisite เชิงธุรกิจ:** ต้องตัดสินใจเปิดรับบัตรเครดิตก่อน — PromptPay ผ่าน Stripe เป็น non-recurring ทำ auto-renew ไม่ได้ (ดู `PAYMENT.md`)

**Exit criteria:** สมัคร→ต่ออายุอัตโนมัติ→ยกเลิก ครบ loop ใน Stripe test clock · subscription ขาด = playback ปฏิเสธทันที · บัตรตายมี grace period + dunning email

| ID | Task | รายละเอียด / acceptance | Dep | Est | Track | Status | Own |
|----|------|------------------------|-----|-----|-------|--------|-----|
| P10-01 | เปิดรับบัตรเครดิต | เปิด card ใน Stripe Dashboard + Stripe Payment Element ฝั่ง checkout เดิม + webhook path เดิมรองรับ (ไม่มีตารางใหม่ — `Payments.Method` รองรับ Card อยู่แล้ว) | P3-31 | 2 | BE/FE | TODO | — |
| P10-02 | Subscription domain | `SubscriptionPlans`/`Subscriptions` + สถานะ (Active/PastDue/Cancelled/Expired) + entitlement "คอร์สที่ร่วม subscription" (flag ต่อคอร์ส — **default เสนอ: instructor opt-in แต่ opt-in vs บังคับทุกคอร์สเป็นส่วนหนึ่งของ Q6 ต้องยืนยันก่อนเริ่ม task นี้**) | P10-01, Q6 | 3 | BE | TODO | — |
| P10-03 | Stripe Billing integration | recurring ด้วยบัตรเท่านั้น + webhook `invoice.paid`/`invoice.payment_failed`/`customer.subscription.*` (idempotent ผ่าน `StripeWebhookEvents` เดิม) + grace period + dunning | P10-02 | 3.5 | BE | TODO | — |
| P10-04 | Playback entitlement | แก้ playback session API (P2-04): สิทธิ์ = enrollment active **หรือ** subscription active ⚠️ แตะตรรกะ entitlement ที่เป็น security-critical — ต้องผ่าน security review + เทสต์ IDOR/expiry ใหม่ทั้งชุด | P10-02 | 2 | BE | TODO | — |
| P10-05 | Revenue split สำหรับ subscription | สูตรตาม **Q6** (default ที่เสนอ: pool รายได้เดือนนั้นแบ่งตามสัดส่วนนาทีที่คอร์สของแต่ละ instructor ถูกดูจริงจาก `WatchEvents`) + reversal เมื่อ refund | P10-03, P6-03 | 3 | BE | TODO | — |
| P10-20 | Subscription UI | หน้า plan + สมัคร/ยกเลิก/เปลี่ยนแผน + สถานะบัตร/ประวัติบิล + ป้าย "รวมใน subscription" บนหน้าคอร์ส | P10-03 | 3.5 | FE | TODO | — |
| P10-30 | เทสต์ P10 | Stripe test clock: ต่ออายุ/บัตรตาย/ยกเลิก · subscription หมด → playback ถูกปฏิเสธ · split คำนวณถูกตาม Q6 | P10-20 | 2.5 | QA | TODO | — |

---

## สถานะจริงของงานที่ยังไม่ปิด (audit 2026-08-24)

> ตรวจจากโค้ดจริงบน `feat/p2-p6-scaffold` ไม่ใช่จากรายงานของ agent — build เขียว 0 warning/0 error, unit **539/539** ผ่าน, architecture **4/4** ผ่าน, integration test ของ 7 โมดูลใหม่ **0 ไฟล์**
> ทุกบรรทัดข้างล่างคือ "สิ่งที่ยังขาด" ของ task ที่ตารางด้านบนขึ้น `PART`/`BLOCK`/`TODO`-ทั้งที่มีโค้ดอยู่แล้ว — Antigravity ใช้บรรทัดนี้เป็น to-do ได้ตรง ๆ

### ปัญหาข้ามเฟส (แก้ก่อน ไม่งั้นทุก task ถัดไปสืบทอดปัญหาเดิม)

| # | เรื่อง | หลักฐาน | ผลถ้าไม่แก้ |
|---|-------|---------|------------|
| X-1 | **ไม่มี integration test ของ 7 โมดูลใหม่เลย** | `backend/tests/Siri.IntegrationTests/` มีแต่ไฟล์ของ Identity/Catalog | DoD ของ `workflow.md` ถูกข้ามทุก task · บั๊กระดับ DB constraint (unique index, cascade, transaction) มองไม่เห็นจาก unit test ที่ใช้ fake |
| X-2 | **ไม่มี commit ตั้งแต่ scaffold** (~430 ไฟล์ค้าง) | `git log` ล่าสุด = `40935d9 ci:` | CI ไม่เคยรัน → integration test ไม่เคยถูกรันจริงเลยแม้แต่ครั้งเดียว (เครื่อง dev ไม่มี Docker) · review ต่อ task ทำไม่ได้เพราะแยก diff ไม่ออก → ดู `P0-39` |
| X-3 | **หน้า FE 6 หน้าเป็น mock ล้วน ไม่เรียก API เลย** ทั้งที่ backend พร้อมแล้ว | `instructor-analytics-page.ts` (ตัวเลข+ชื่อ/อีเมลนักเรียนปลอม hardcode), `admin-dashboard-page.ts`, `admin-users-page.ts`, `admin-marketing-page.ts`, `admin-course-moderation-page.ts`, `order-success-page.ts` — ทั้ง 6 ไฟล์ inject แค่ `SeoService`/`ActivatedRoute` | หน้าดูเหมือนเสร็จแต่ใช้งานจริงไม่ได้ · เสี่ยงถูกนับว่า "จบ P6" ทั้งที่ยังไม่ต่อสายเลย |
| X-4 | **ข้อความ user-facing hardcode ในเทมเพลต** (ผิด `frontend.md` + DoD) | `features/instructor` 0/5 ไฟล์ใช้ i18n, `features/admin` 2/7, `features/commerce` 1/2, `features/blog` 1/2 (auth/catalog/learning ทำถูกอยู่แล้ว) | สลับ TH/EN แล้วหน้าเหล่านี้ไม่เปลี่ยนภาษา · รื้อทีหลังแพงกว่า |
| X-5 | **role name เป็น string ดิบกระจายหลายที่** | `EpisodeAttachmentEndpoints.cs:40,64` (`IsInRole("Admin")`), `CertificateEndpoints.cs:147` (`Roles.Contains("Admin")`) — ค่าคงที่จริงอยู่ที่ `Identity/Domain/Role.cs` ซึ่งโมดูลอื่นมองไม่เห็น | พิมพ์ผิดหนึ่งตัว = เปิดสิทธิ์ให้ทุกคนแบบเงียบ ๆ · ควรมี `RoleNames` ใน `Siri.SharedKernel` แบบเดียวกับ `AuthorizationPolicyNames` พร้อม test ยืนยันว่าค่าตรงกับ `Role.*Name` |
| X-6 | `Siri.Modules.Analytics` ผสม pattern | มีทั้ง `Application/I*Repository` + `Infrastructure/*Repository` (Repository+Service) และ `Features/AdminDashboardSummary/` (vertical slice) ในโมดูลเดียว | `backend.md` ห้ามผสมสองรูปแบบในโมดูลเดียว — เลือกทางเดียวแล้วบันทึกเหตุผล |

### P2 — Media & Player

- **P2-01** `PART` — library เปิดแล้ว + tier ตัดสินแล้ว (MediaCage Basic) แต่ยังไม่ได้ตั้ง token auth key / allowed referrer / geo rule / ปิด direct play และ **ยังไม่เคยทดสอบว่า custom player ใช้กับ "Embed View only" ได้จริงไหม** · `VideoProvider:CdnHostname` ยังเป็น placeholder
- **P2-05** `TODO` — ไม่มีโค้ด DRM license proxy เลยสักบรรทัด (grep `license` ใน `Siri.Modules.Media` + `Siri.Integrations.Video` = 0 hit)
- **P2-06** `PART` — `PLAYBACK_SESSION.cs` มีและถูกเขียน log แล้ว แต่ **ไม่มี job ตรวจพฤติกรรมผิดปกติ** (`Siri.Workers/RecurringJobsRegistration.cs` มี 4 job: outbox, bunny poll, analytics rollup, stripe reconcile)
- **P2-20 / P2-21** `PART` — ⚠️ **ยังไม่มี Shaka Player จริง**: `frontend/package.json` ไม่มี `shaka-player` เป็น dependency เลย และ `features/learning/video-player/video-player.html:8` ใช้ `<video>` ธรรมดา → ไม่มี DASH/HLS manifest, ไม่มี EME/DRM, ไม่มี error mapping ของ Shaka ตามที่ acceptance เขียน (ขัดกับ D-09 ทั้งข้อ) · UI controls/watermark ที่ทำไว้ยังใช้ต่อได้ แต่ core player ต้องเขียนใหม่
- **P2-25** `PART` — มี badge free preview บนหน้า course detail แล้ว แต่ยังไม่มี flow เล่นตัวอย่างจริง + CTA ชวนซื้อเมื่อจบตัวอย่าง
- **P2-31** `BLOCK` — acceptance ("IDM/yt-dlp/devtools ต้องล้มเหลวทุกทาง") ขัดกับ DRM tier ที่เลือก (MediaCage Basic = clear-key) → ต้องตัดสิน **Q8** ก่อน (ดู `DECISIONS.md`)

### P3 — Commerce

- **P3-03** `PART` — ไม่มี job หมดอายุออร์เดอร์ (ไม่มีใน `RecurringJobsRegistration.cs`) → กติกา "AwaitingPayment 30 นาที" ไม่เกิดขึ้นจริง และเคสจ่ายหลังหมดอายุยังทดสอบไม่ได้
- **P3-06** `PART` — state machine อยู่ใน `Domain/PAYMENT.cs` (`MarkProcessing`/`MarkSucceeded`/…) ครบ แต่ "กัน race ระหว่าง webhook กับ expiry job" พิสูจน์ไม่ได้ตราบใดที่ยังไม่มี expiry job (P3-03)
- **P3-07** `PART` — `PAYMENT_OPS_QUEUE` ถูกเขียนจาก `StripeWebhookHandler` แล้ว แต่ **ไม่มี endpoint ให้ admin resolve** และไม่มีการแจ้งผู้ซื้อ
- **P3-09** `DONE` (AG build 2026-08-25 · Claude Code review รอบสอง 2026-08-25 11:35 — ผ่าน) — `PromoCodeService.ValidatePromoCodeAsync` + `POST /api/commerce/promo-codes/validate` · `OrderService.CreateAsync` คำนวณส่วนลด server-side และหักโควตา atomic ด้วย `PromoCodeRepository.TryRedeemAsync` · VAT 7% คำนวณจากยอดหลังหักส่วนลด · Frontend checkout page ต่อ API validate จริง · **ปิดครบ 3 จุดที่ review ติง:**
  1. ✅ **Transaction คลุมทั้งชุด**: เพิ่ม `IOrderRepository.ExecuteInTransactionAsync` ใช้ `BeginTransactionAsync()` ห่อหุ้มการสร้าง `ORDER`, การหักโควตา `REDEEMED_COUNT`, และการบันทึก `PROMO_REDEMPTIONS` ในธุรกรรมเดียวกัน หากเกิด error/conflict ระบบจะ Rollback ทั้งชุดทันที ไม่เหลือ order ตกค้างหรือยอดส่วนลดค้าง
  2. ✅ **แก้ TOCTOU `MAX_PER_USER`**: เพิ่ม index `IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_USER_ID` (Migration `AddPromoRedemptionsUserIndex`) + ใน `TryRedeemAsync` ใช้ `SELECT ... WITH (UPDLOCK, HOLDLOCK)` ล็อก range ป้องกัน race condition เมื่อ user คนเดียวยิง request ซ้ำพร้อมกัน
  3. ✅ **Reversal ครบวงจร**: เชื่อมต่อ `PromoCodeRepository.RevertRedemptionAsync` เข้ากับ `RefundService.ApproveAsync` (เมื่อ admin อนุมัติคืนเงิน) และ `StripeWebhookHandler.HandlePaymentIntentCanceledAsync` (เมื่อ Stripe ยกเลิก payment intent) คืนโควตา `REDEEMED_COUNT` และลบแถว `PROMO_REDEMPTIONS` อัตโนมัติ
  · Unit tests 568/568 ผ่าน, Architecture 4/4 ผ่าน, Integration tests (`PromoCodeTests.cs`) ครอบคลุม validation, order redemption, multi-user race, single-user `MAX_PER_USER` race, transaction rollback, และ refund reversal ครบทั้งหมด
  · **ผลตรวจของ Claude Code (อ่านโค้ดจริงทีละจุด ไม่ได้เชื่อรายงาน):** `ExecuteInTransactionAsync` ใช้ `CreateExecutionStrategy()` ถูกต้อง (จำเป็นเมื่อเปิด retry-on-failure) มี guard กันซ้อน transaction และ rollback เมื่อผลลัพธ์เป็น `Result` ที่ล้มเหลว — ยืนยันแล้วว่า `Result<TValue> : Result` จริง (`SharedKernel/Result.cs:47`) pattern `result is Result { IsSuccess: false }` จึงจับ `Result<OrderResponse>` ได้จริง ไม่ commit ทับความล้มเหลว · `OrderService.CreateAsync:146` ครอบ `AddAsync` + `TryRedeemAsync` (+ auto-enroll เคสส่วนลด 100%) ไว้ในธุรกรรมเดียวกันจริง · per-user ใช้ `FromSqlInterpolated` + `WITH (UPDLOCK, HOLDLOCK)` บนช่วง (PROMO_CODE_ID, USER_ID) ซึ่งเป็น key-range lock ที่ทำงานได้เพราะอยู่ใน transaction จริงและมี index ใหม่รองรับ (index ตั้งใจไม่ unique เพราะ `MAX_PER_USER` > 1 ได้) · reversal เรียกครบ 3 ทาง: `OrderService.CancelAsync`, `RefundService`, `StripeWebhookHandler`
  · **เทสต์ที่พิสูจน์แต่ละข้อโดยตรง:** `CreateOrder_SingleUserConcurrencyRace_OnlyOneSucceedsWhenMaxPerUserIsOne`, `CreateOrder_TransactionRollback_DoesNotLeakRedemptionOrDeductQuota`, `Refund_WithPromoCode_RevertsPromoRedemptionAndQuota` + FE `applies promo code and recalculates total discount`
  · **ค้างที่ไม่ใช่ความผิดของ task นี้:** migration `AddPromoRedemptionsUserIndex` ยังไม่ apply (รอ `P0-09`) และ integration test ทั้งหมดยังไม่เคยรันจริงจนกว่า `P0-39` จะ push ให้ CI รัน
- **P3-20** `TODO` — ฝั่ง BE มี `CartEndpoints` แล้ว แต่ยังไม่มีหน้าตะกร้าใน FE
- **P3-23** `TODO` — ยังไม่มีทั้งหน้า FE และ endpoint `GET /orders` (มีแต่ `GET /orders/{id}`)
- **P3-25** `PART` — หน้ามีครบตาม mockup แต่เป็น UI ล้วน ยังไม่เรียก API (ดู X-3)
- **P3-30** `TODO` — ยังไม่มีหน้า admin payment ops (BE ก็ขาดตาม P3-07)

### P4 — Instructor Studio

- **P4-01** `DONE` (review โดย Claude Code 2026-08-25) — endpoint ครบ: `GetCourseBuilder`, Section CRUD + `ReorderCourseSections`, Episode CRUD + `ReorderCourseEpisodes`, `AutosaveCourse` · ตรวจแล้วทุก handler เช็ค ownership ผ่าน `InstructorProfile.UserId` → `Course.InstructorId` และจำกัดเฉพาะ Draft/Rejected · nested resource ปลอดภัย (หา section จาก `course.Sections` ที่โหลดมาแล้ว ไม่ query แยก → section id ของคอร์สอื่นได้ 404) · concurrency ใช้ `Entry(course).Property(c => c.RowVersion).OriginalValue` + จับ `DbUpdateConcurrencyException` → 409 จริง · `Course.RemoveSection`/`RemoveEpisode` เพิ่มใหม่พร้อม guard ห้ามลบเมื่อ Published/Archived · integration test 12 ตัว (`CourseBuilderTests.cs`) ครอบคลุมคอร์สของคนอื่น, reorder ส่งไม่ครบ, rowversion เก่า, คอร์สที่ไม่ใช่ Draft · build 0/0, unit 559/559, arch 4/4, FE 176/176 (error 3 ตัวเป็น flaky เดิมของ `category-accordion-item.spec.ts` ไม่เกี่ยวกับงานนี้) · **หมายเหตุ: integration test ยังไม่เคยรันจริงจนกว่า `P0-39` จะทำให้ CI รันได้**
- **P4-03** `PART` — endpoint มีแล้วแต่ (ก) `GET /episodes/{id}/attachments` **ไม่มี enrollment check** ใครล็อกอินก็ list ไฟล์แนบของคอร์สไหนก็ได้ (ผิด LX-04 + `security.md`) (ข) ไม่มีตรวจ magic bytes/สแกนไวรัส (ค) ใช้ role string ดิบ (X-5)
- **P4-04** `TODO` — ไม่มี endpoint instructor analytics ฝั่ง BE เลย (หน้า FE จึงเป็น mock — ดู P4-23)
- **P4-20 / P4-22** `PART` — ต่อ API จริงของ P4-01 แล้ว (autosave/reorder/CRUD ทำงานจริง, จับ 409 แล้วขึ้นข้อความให้รีเฟรช, ข้อความอยู่ใน i18n) · **รูสองจุดที่ review 2026-08-25 เจอ ถูกแก้แล้วทั้งคู่ (ตรวจซ้ำ 02:10):** `/instructor/courses/new` เรียก `createCourse` จริงแล้ว → navigate ไป `/instructor/courses/{id}/builder` → ดึง rowVersion → autosave (ไม่มี fake success เหลืออยู่, `setTimeout` ที่ยังเห็นเป็นแค่ตัวซ่อน toast) และ `AutosaveCourseValidator` ใส่เพดาน section ≤ 100 / episode ≤ 200 ต่อ section แล้ว · ที่ยังเหลือของ P4-20 ตาม acceptance เดิม: **ย้ายลำดับด้วยคีย์บอร์ด** และ **undo** ยังไม่มี · code smell เล็ก ๆ: create→get→autosave เป็น nested subscribe 3 ชั้น ควรเปลี่ยนเป็น `switchMap` ตอนแตะไฟล์นี้ครั้งหน้า
- **P4-21** `TODO` — ยังไม่มี UI อัปโหลดไฟล์ใน instructor เลย (BE `MediaUploadSessionEndpoints` พร้อมแล้ว)
- **P4-23** `PART` — mock ล้วน (ดู X-3) · ต้องรอ P4-04
- **P4-25** `TODO` — ยังไม่มีหน้า onboarding ผู้สอน (BE `POST /instructors/apply` พร้อมตั้งแต่ P1-03)

### P5 — Interactive Learning

- **P5-20 / P5-21 / P5-23 / P5-24** `TODO` — BE พร้อมหมดแล้ว (quiz/assignment/discussion/attachment) แต่ยังไม่มีหน้า FE สักหน้า
- **P5-22** `PART` — หน้า verify + ปุ่มดาวน์โหลด + share LinkedIn มีจริงแล้ว เหลือแค่ข้อความยัง hardcode (X-4) และยังไม่มีปุ่มดาวน์โหลดจากหน้า my-courses

### P6 — Admin, CMS & Revenue

- **P6-02** `PART` — flash sale/bundle มีแค่ create/get/list · ยังไม่มี **ลำดับความสำคัญของส่วนลด** (promo vs flash sale vs bundle) ซึ่งเป็นหัวใจของ acceptance และเป็นสิ่งที่ P6-30 ต้องทดสอบ
- **P6-03** `BLOCK` — ⚠️ **Q4 ยังไม่ตอบ แต่โค้ดเดาไปแล้ว**: `Payout/Infrastructure/Contracts/RevenueSplitContract.cs:12` hardcode `InstructorShareRatio = 0.70m` ไม่อ่าน `InstructorProfile.RevenueSharePercent` ที่มีมาตั้งแต่ P1-03 และ `paymentFee = 0m` ทั้งที่ Q4 ถามตรง ๆ ว่าหักค่าธรรมเนียมก่อนหรือหลังแบ่ง · **ตอบ Q4 ก่อน แล้วเขียนส่วนนี้ใหม่ทั้งก้อน + reversal ตอน refund**
- **P6-04** `PART` — `PAYOUT_BATCH.AddItem()` **ไม่เคยถูกเรียกจากที่ไหนเลยในโค้ดเบส** → batch สร้างได้แต่หัวรอบ ไม่รวมยอดจาก `REVENUE_SPLITS`, ไม่คิด withholding tax (คอลัมน์มีแต่ไม่มีสูตร), ไม่มี export ไฟล์โอน, ไม่มี mark paid
- **P6-06** `PART` — มี user search/suspend/reactivate/roles/audit-logs แล้ว · ขาด course moderation + feature flag
- **P6-20** `PART` — มีหน้า banners + posts · ยังไม่มี menu editor
- **P6-21 / P6-23 / P6-26 / P6-27** `PART` — หน้าเป็น mock ล้วน (X-3) ทั้งที่ BE พร้อมแล้วทุกตัว (P6-02 / P1-05 / P6-07 / P6-06) → งานที่เหลือคือ "ต่อสาย" ไม่ใช่สร้างใหม่
- **P6-22** `PART` — ใช้ `PayoutApiService` จริงแล้ว แต่ยังไม่มีคิวอนุมัติคำขอคืนเงิน ทั้งที่ BE มี `POST /refunds/{id}/approve|reject` พร้อม
- **P6-25** `TODO` — หน้าแรกยังดึงข้อมูลจาก Catalog ตรง ยังไม่ผูกกับ CMS banner/section

### งานตามเก็บจาก audit 2026-08-21 ที่ยังไม่ถูกแตะ

รายการ High/Medium จากรอบนั้นยังไม่มีเลขงานของตัวเอง — ให้ถือเป็นส่วนหนึ่งของ task เจ้าของเรื่องข้างบน: Bunny webhook signature verification (P2-03), refund state transition ครบวง (P3-05), `AssignmentSubmissionService.GradeAsync` ยังไม่เช็คว่า instructor เป็นเจ้าของคอร์สนั้นจริง (P5-02), `QuizOptionResponse.IsCorrect` ที่ยังไม่ถูกกรองสำหรับหน้าเรียนของผู้เรียน (P5-20)

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
- **P8–P10 เพิ่ม 2026-08-19 จากสเปค SiriLearn (D-16)** — ทั้งหมดเป็นงานหลัง launch v1 ไม่กระทบ 28 สัปดาห์เดิม · ลำดับสลับได้ตามธุรกิจ · P9 รอ Q7, P10 รอ Q6 + การตัดสินใจเปิดบัตรเครดิต · **P1-10** (filter ผู้สอน) เป็น delta เดียวที่เพิ่มเข้า v1 เพราะ search API ยังแก้ต่อได้ง่ายก่อน FE catalog เริ่ม
- **Mockup v2026-08-21 เพิ่มหน้าจอใหม่ 6 หน้า** (มีจาก 10 → 16 หน้า, ไฟล์เก็บที่ `frontend/design-system/siri-upskill/mockups-2026-08-21/`) — เทียบกับ mockup 2026-08-20 เดิมด้วย `diff` แล้วยืนยันว่า 10 หน้าเดิมเนื้อหาไม่เปลี่ยน (มีแค่ปุ่ม "ชำระเงิน" ที่ผูกไปหน้าใหม่แทน) เพิ่ม task ใหม่ 6 ตัว: **P3-25** (หน้าชำระเงินสำเร็จ), **P4-26/P6-08** (หน้า+API รายได้ผู้สอน), **P4-27** (หน้าประกาศผู้สอน — ผูกกับ P6-05 เดิมที่แก้ scope ให้ชัดว่าเป็นเครื่องมือผู้สอน ไม่ใช่แอดมิน), **P6-07/P6-26** (API+หน้าแดชบอร์ดแอดมิน), **P6-27** (หน้าผู้ใช้และสิทธิ์ แยกจาก P6-23 เดิม) และ**แก้ P6-23** ให้แคบลงเหลือแค่อนุมัติคอร์ส (unblock ทันทีเพราะ backend P1-05 พร้อมแล้ว ไม่ต้องรอ P6-06) — รายละเอียดเต็มอยู่ในแต่ละแถว task ด้านบน (ค้นด้วย "mockup 2026-08-21")

### วิธีทำงานสองเอเจนต์ (บังคับ ตั้งแต่ 2026-08-24)

1. **หยิบทีละ task ตามเลข ID** — ห้ามทำข้ามลำดับโดยไม่บอก (ที่ผ่านมา P8-05 ถูกทำก่อน P4-01 ซึ่งขัดกับลำดับที่ตกลงไว้ทั้งใน `ROADMAP.md` และ `ANTIGRAVITY_HANDOFF.md`)
2. **1 task = 1 commit** ข้อความ `feat(module): สรุปสั้น [P3-09]` — ห้ามกองรวม ไม่งั้น review รายงานไม่ได้และ CI ไม่มีทางชี้ได้ว่างานไหนพัง
3. **จบ task แล้วอัปเดตคอลัมน์ Status ในไฟล์นี้ทันที** เป็น `BUILT` หรือ `PART` (+ เขียนบรรทัดใน § "สถานะจริง" ว่าขาดอะไร) — **ห้ามตั้ง `DONE` ให้ตัวเอง**
4. **`DONE` ตั้งได้โดย Claude Code เท่านั้น** หลัง review + มี integration test จริง — เกณฑ์นี้มาจากบทเรียนตรง ๆ ของ audit 2026-08-21 (7 Critical หลุดออกมาทั้งที่ unit test เขียวหมด)
5. **ห้ามเดาค่าที่เป็นการตัดสินใจธุรกิจ** (สูตรแบ่งรายได้, ราคา, สิทธิ์เข้าถึง) — ถ้า `DECISIONS.md` ยังไม่ปิดคำถามนั้น ให้หยุดแล้วถาม อย่าใส่ค่า default ลงโค้ดเงียบ ๆ (เกิดขึ้นแล้วจริงที่ P6-03)
6. **ห้าม `dotnet ef database update` กับ DB จริง** และห้าม `git push`/สร้าง PR เอง — เจ้าของโปรเจ็คสั่งเองเสมอ
