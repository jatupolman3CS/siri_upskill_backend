# Decisions & Open Questions

## A. ตัดสินใจแล้ว (locked)

| # | เรื่อง | ผลตัดสิน | เหตุผล |
|---|-------|---------|--------|
| D-01 | Frontend | **Angular 22.1.4** (standalone + signals + zoneless) | ผู้ใช้กำหนด + เป็น latest ณ วันวางแผน |
| D-02 | Backend | **.NET 10 (LTS) ASP.NET Core Web API** | ผู้ใช้กำหนด; SDK 10.0.400 ติดตั้งแล้วบนเครื่อง |
| D-03 | Database | **SQL Server (MSSQL) 2022+** + EF Core 10 | ผู้ใช้กำหนด |
| D-04 | Architecture | **Modular Monolith** (project ต่อ module) ไม่ใช่ microservices | ทีมขนาดเล็ก, deploy ง่าย, แตกเป็น service ทีหลังได้เมื่อ traffic โต |
| D-05 | Rendering | **SSR (Angular SSR + hydration)** เฉพาะหน้า public; admin/instructor เป็น CSR | SEO คือช่องทางหาลูกค้าหลักของ marketplace คอร์สออนไลน์ |
| D-06 | Mediator library | **ไม่ใช้ MediatR** — ใช้ handler ธรรมดา + DI | MediatR เปลี่ยนเป็น commercial license แล้ว; เลี่ยงหนี้ทาง license ตั้งแต่ต้น |
| D-07 | Background jobs | **Hangfire** (SQL Server storage) | ใช้ MSSQL ที่มีอยู่แล้ว ไม่เพิ่ม infra, มี dashboard |
| D-08 | Cache / session | **Redis** | ต้องใช้จริงสำหรับ concurrent-login control, rate limit, playback token |
| D-09 | Video player | **Shaka Player** | รองรับ EME ครบ Widevine / PlayReady / FairPlay + ABR |
| D-10 | Search v1 | **MSSQL Full-Text Search** | พอสำหรับ catalog < ~10k คอร์ส; ย้ายไป OpenSearch/Meilisearch เมื่อจำเป็น |
| D-11 | Money type | `decimal(18,2)` + `Currency char(3)` เก็บ THB | กันปัญหา floating point |
| D-12 | Time | เก็บ UTC ทั้งหมด (`datetime2(3)`), แปลงเป็น Asia/Bangkok ที่ UI | มาตรฐาน |
| **D-13** | **Video / DRM** | **Bunny Stream** (Q1 — ตัดสินแล้ว) | ถูกที่สุด, มี DRM + token auth + CDN ครบในตัว, ย้าย bandwidth ออกจาก VPS |
| **D-14** | **Payment** | **Stripe** (PromptPay QR ผ่าน PaymentIntent + webhook) — Q2 ตัดสินใหม่ 2026-08-18 แทน EasySlip | เจ้าของโปรเจ็คสั่งเปลี่ยน; Stripe รองรับ merchant ไทย + PromptPay จริง ยืนยันจาก webhook ได้ (ไม่ต้องตรวจสลิปเอง) — **แลกกับ onboarding/KYC ที่ต้องรอ และยังไม่มีผ่อนชำระ** ดู `PAYMENT.md` |
| **D-15** | **Hosting** | **Contabo VPS + Docker Compose** (Q3 — ตัดสินแล้ว) | มีเครื่องอยู่แล้ว, ต้นทุนคงที่ — แลกกับงาน ops ที่ต้องดูแลเอง ดู `DEPLOYMENT.md` |
| **D-16** | **SiriLearn spec reconciliation** (2026-08-19) | รับสเปค `SiriLearn — Master System Specification Document.txt` เป็นเอกสารต้นทางเพิ่ม โดย**ยึด roadmap/stack เดิมทุกจุดที่ขัดกัน**: Stripe ไม่ใช่ Omise/GB Prime Pay (ตาม D-14), ห้าม MediatR (ตาม D-06)/AutoMapper, Modular Monolith ไม่ใช่ Clean Architecture 4 ชั้น (ตาม D-04), ธีมน้ำเงิน/ทอง + Phosphor ไม่ใช่ Soft Pink + Lucide (ตาม P0-31/34), DRM จริงไม่ใช่แค่ signed URL (ตาม SE-01) · ฟีเจอร์ที่สเปคมีแต่แผนไม่มี รับเข้า backlog เป็น **P8 (social login + engagement) / P9 (B2B) / P10 (subscription)** ทั้งหมดหลัง launch · delta เล็ก 3 จุดผนวกเข้า v1: filter ผู้สอน (P1-10 ใหม่), timestamp บน watermark (P2-22), QR บน certificate (P5-03) | เจ้าของโปรเจ็คตัดสินผ่าน AskUserQuestion 2026-08-19 — สเปคภายนอกเป็นต้นทางของ requirement แต่การตัดสินใจเชิง stack/vendor ที่ล็อกแล้วมีน้ำหนักกว่า (license + งานที่สร้างเสร็จแล้ว) |
| **D-17** | **Mockup handoff — Repository+Service pattern, UPPERCASE naming, pink rebrand** (2026-08-20) | เจ้าของโปรเจ็คส่ง 10-screen UI mockup (Claude Design handoff) + สั่งให้ Claude ออกแบบ DB ทั้งหมดสำหรับ P2–P6/P8-Learning-Path ในครั้งเดียว แล้วส่งต่องานให้ **Antigravity** (coding agent อีกตัว) ทำทีละ task ต่อ: (1) **โมดูลใหม่ 7 ตัว** (Commerce/Media/Learning/Payout/Cms/Community/Analytics) ใช้ **Repository + Service pattern** แทน vertical slice — Identity/Catalog/Notification ไม่แตะ รายละเอียดที่ `.claude/rules/backend.md`/`docs/ARCHITECTURE.md` (2) โมดูลใหม่ 7 ตัวเดียวกัน ใช้ **UPPERCASE ทั้งชื่อ entity class/property C# และ DB table/column** (ยกเว้น `IAuditable`/`ISoftDelete` property ที่ต้องคง PascalCase เพราะ `AuditableEntityInterceptor` ค้นด้วยชื่อ C# ตรง ๆ — ดู `.claude/rules/database.md`) (3) checkout รับ **PromptPay อย่างเดียวใน v1 เหมือนเดิม** (D-14 ไม่เปลี่ยน) บัตร/ผ่อนชำระใน mockup ถูก render เป็น disabled "เร็ว ๆ นี้" (4) รีแบรนด์สีหลักจากน้ำเงิน `#0369A1` เป็นชมพู `#DB2777` **ทั้งระบบ** (ทับมติสี P0-31 เดิม — ดู `frontend/src/app/styles/tokens.css` และ `frontend/design-system/siri-upskill/MASTER.md`'s superseded-note) ส่วน typography/spacing ไม่เปลี่ยน mockup persist ไว้ที่ `frontend/design-system/siri-upskill/mockups-2026-08-20/` ให้ Antigravity/session ถัดไปอ่านได้ **gap ที่ mockup ต้องการแต่ไม่มี task รองรับมาก่อน**: e-Tax invoice (เพิ่มตาราง `TAX_INVOICES`, ตัดส่วน "นำส่งกรมสรรพากรอัตโนมัติ" ออกจาก scope — เป็น government e-filing integration แยกต่างหาก), refund approval workflow (`REFUNDS` เพิ่ม `DECIDED_BY_USER_ID`/`DECIDED_AT_UTC`/`DECISION_NOTE`/`STRIPE_REFUND_ID`), device limit ตอนออก playback token (P2-04's open question) — ใช้ `ISessionRegistry`/`UserSessions` เดิมจาก P0-17 ผ่าน JWT `sid` claim ซ้ำ ไม่สร้างระบบนับ device คู่ขนาน | เจ้าของโปรเจ็คตัดสินผ่าน AskUserQuestion 2026-08-20 (5 คำถาม) + plan mode ก่อนลงมือ — ขัดกับกฎ backend.md เดิม ("ห้าม god service") และ database.md เดิม (PascalCase) โดยตรง จึงต้องยืนยันชัดเจนก่อนทำ ไม่ใช่ตีความเอง; phase discipline (`workflow.md`) ก็ถูกข้ามโดยตั้งใจสำหรับขั้น scaffold เท่านั้น (schema+skeleton ยังไม่ใช่ feature ที่ใช้งานจริง) — ลำดับส่งงานให้ Antigravity ยังตาม P2→P3→P4→P5→P6→P8 ปกติ |

## B. ปิดแล้ว — Q1 / Q2 / Q3 (2026-08-17)

### ✅ Q1 — Video/DRM = **Bunny Stream**, DRM tier = **MediaCage Basic** (v1, ตัดสินใจ 2026-08-21)
implement หลัง `IVideoProvider` → สลับเจ้าได้ถ้าจำเป็น — **สมัครบัญชีแล้ว**, key จริงตั้งเข้า `dotnet user-secrets` ของ `Siri.Api` แล้ว (`VideoProvider:LibraryId`/`PullZone`/`ApiKey`/`ReadOnlyApiKey`, 2026-08-21) รอ P2-02 สร้าง `VideoProviderOptions` มา bind ⚠️ **`VideoProvider:CdnHostname` ยังไม่ได้ตั้ง** — ค่าที่ได้รับมาเป็น placeholder ไม่ใช่ hostname จริง (ปกติ Bunny ออกให้เป็นรูปแบบ `{pull-zone}.b-cdn.net`) ต้องกลับไปดู dashboard อีกรอบก่อนตั้งจริง

**ตัดสินใจ (เช็คกับเอกสารจริงของ Bunny แล้ว ไม่ได้เดา — https://bunny.net/docs/stream-understanding-mediacage-basic-drm, https://bunny.net/docs/stream-mediacage-enterprise-drm): ใช้ MediaCage Basic ต่อใน v1 โดยรู้และยอมรับข้อแลกเปลี่ยนต่อไปนี้แล้ว:**
- **ไม่ใช่ Widevine/FairPlay/PlayReady เลยทั้ง 3 ระบบ** (ไม่ใช่แค่ iOS ไม่ได้ FairPlay ตามที่เคยกังวลไว้ — Basic ไม่รองรับสักระบบ ทุก platform อยู่ในสถานการณ์เดียวกันหมด) ใช้ **clear-key encryption** แทน — key ส่งไปที่ client แบบไม่เข้ารหัส เอกสาร Bunny เขียนเองตรง ๆ ว่า *"potentially vulnerable to a sophisticated attack"* และ *"not suitable for premium, industry, or pay-per-view platforms"*
- **นี่คือการปรับ SE-01/D-16 ("DRM จริงไม่ใช่แค่ signed URL") ลงจริง ไม่ใช่แค่รายละเอียดย่อย** — ให้ `docs/SECURITY.md`'s SE-01 และ `docs/TASKS.md`'s P2-31 (pen-test bar "ต้องล้มเหลวทุกทาง") สะท้อนความเสี่ยงนี้ตรง ๆ แทนที่จะเขียนเป็น absolute bar ที่ทำไม่ได้จริงกับ tier นี้ — **ชดเชยด้วย watermark (SE-02) + concurrent-session limit (SE-03) ที่ยังทำเต็มที่เหมือนเดิม** เป็นเกราะป้องกันหลักแทน DRM ระดับ hardware
- **"Embed View only" — ยังไม่ยืนยันว่ากระทบแผน custom Shaka Player (D-09) แค่ไหน** เอกสาร Bunny ระบุว่า MediaCage Basic จำกัดให้เล่นผ่าน Bunny's Embed View เท่านั้น (ปิด MP4 fallback + Early-Play) ไม่มีรายละเอียดพอจะฟันธงว่า custom player UI (ปุ่มควบคุมเอง, watermark overlay เอง ตามที่ P2-20~23 วางแผนไว้) ยังทำได้ไหม — **ต้องทดสอบจริงกับบัญชีจริงก่อนเริ่ม P2-20** ถ้าทำไม่ได้ต้องกลับมาคุยเรื่อง scope ของ P2-20~23 ใหม่
- อัปเกรดเป็น MediaCage Enterprise ทำได้ทีหลังถ้าจำเป็น (ต้องติดต่อ Bunny sales + Apple FairPlay deployment credentials แยกต่างหาก ไม่ใช่ self-service) — เก็บไว้เป็นทางเลือกเปิดสำหรับ v2/หลัง launch ถ้าพบว่า Basic ไม่พอจริงตอนใช้งานจริง

**ยังต้องเช็คกับ Bunny เพิ่มก่อนเริ่ม P2 จริง (P2-01):** token authentication key / allowed referrer / geo-blocking / direct-play ปิดหรือยัง — MediaCage Basic ไม่ได้แทนที่ชั้นป้องกันพวกนี้ ยังต้องตั้งค่าคู่กันเสมอ

### ✅ Q2 — Payment = **Stripe** (แก้ไข 2026-08-18 — เดิม PromptPay QR + EasySlip)
เจ้าของโปรเจ็คสั่งเปลี่ยนเป็น **Stripe เพียงเจ้าเดียว ตัด EasySlip ออกทั้งหมด** ก่อนเริ่มงาน P3 (ยังไม่มีโค้ด payment จริง จึงไม่ต้อง migrate อะไร)
ดูรายละเอียดเต็มที่ `PAYMENT.md` — ยืนยันแล้วว่า Stripe รองรับ merchant ไทย + PromptPay (THB เท่านั้น, เพดาน 2 ล้านบาท/รายการ, non-recurring)
⚠️ **ผลกระทบที่ต้องรับรู้:** LX-06 ระบุว่าต้องมีบัตรเครดิต + ผ่อนชำระด้วย — บัตร (Visa/MC) เปิดเพิ่มจาก Stripe Dashboard ได้ภายหลัง แต่ scope v1 เปิดเฉพาะ PromptPay QR; **ผ่อนชำระ Stripe ไทยไม่รองรับ** ต้องเพิ่ม gateway ไทย (Opn/2C2P) ในอนาคต
ออกแบบ `IPaymentMethod` ให้เสียบ provider อื่นเพิ่มทีหลังได้
**ต้องทำก่อน P3 (เจ้าของโปรเจ็คทำเอง เริ่มได้ตั้งแต่ P1):** สมัครบัญชี Stripe Thailand (KYC ใช้เวลา) + เปิด PromptPay ใน dashboard + เก็บ key ทั้ง 3 ตัวเข้า user-secrets/env
**Q4 (revenue split) ต้องรวมค่าธรรมเนียม Stripe เข้าไปในสูตรด้วย** — เช็คเรตจริงจาก dashboard ตอนเปิดบัญชี

### ✅ Q3 — Hosting = **Contabo VPS (self-host, Docker Compose)**
ดูรายละเอียดเต็มที่ `DEPLOYMENT.md`

**อัปเดต 2026-08-17 — พบว่ามี MSSQL รันอยู่บน Contabo แล้วจริง** (`217.217.253.122,1433` เครื่อง `vmi3262952`) ใช้ร่วมกับโปรเจ็คอื่นของเจ้าของระบบ (`MRMOOMOVIE`, `SIRIEDUMARKET`, `SIRISTUDIOPHOTO`) ดำเนินการแล้ว:
- ใช้ database `SIRIUPSKILL` ที่เตรียมไว้แล้ว (เดิมว่างเปล่า)
- แก้ collation เป็น `Thai_100_CI_AS_SC_UTF8` ตรงตาม `DATABASE.md` (ยืนยันแล้วว่ามีจริงบนเครื่องนี้)
- สร้าง login เฉพาะแอป `siriupskill_app` สิทธิ์ `db_owner` **เฉพาะภายใน `SIRIUPSKILL`** เท่านั้น ตรวจสอบแล้วว่าเข้า database อื่นบนเครื่องเดียวกันไม่ได้ — ไม่ใช้ `sa` ในแอปจริง
- connection string เก็บไว้ใน scratchpad ของ session นี้ชั่วคราว รอย้ายเข้า `dotnet user-secrets` ทันทีที่ `Siri.Api` project ถูกสร้าง (P0-13 กำลังทำอยู่)

🔴 **ยังไม่ปิด — 2 เรื่อง:**
1. **SQL Server edition = Developer** — ตรวจสอบจริงแล้วจาก `SERVERPROPERTY('Edition')` **ใช้ production ไม่ได้ตาม license ของ Microsoft** ใช้ dev/staging ได้สบาย แต่ก่อน launch จริงต้องอัปเกรดเป็น Standard (มีค่า license) หรือย้ายไปเครื่อง/instance ที่ใช้ Express/Standard ที่ถูก license — **ต้องตัดสินก่อนเข้า P7 (hardening & launch)** ไม่ใช่ P0-03 อีกต่อไปเพราะ dev ใช้ตัวนี้ไปพลางก่อนได้
2. **Port 1433 เปิดออกอินเทอร์เน็ตสาธารณะ** — ขัดกับ `DEPLOYMENT.md` ที่ระบุว่าห้ามเปิด 1433 ออกเน็ต เครื่องนี้ใช้ร่วมกับโปรเจ็คอื่นด้วย หาก `sa` รั่ว = เสี่ยงทุกโปรเจ็คบนเครื่องพร้อมกัน แนะนำจำกัด firewall เหลือเฉพาะ IP ที่รู้จัก (เช่น IP เครื่อง dev + IP เครื่องที่จะรัน API จริง) โดยเร็ว — ต้องมีสิทธิ์เข้า VPS (SSH) ถึงจะช่วยตั้งให้ได้

## B2. ยังต้องตัดสิน

### Q4 — Revenue split model (blocking Phase 6)
- 70/30 (instructor/platform) แบบตายตัว หรือ ต่อ instructor ได้?
- หัก payment fee ก่อนหรือหลังแบ่ง?
- รอบ payout: รายเดือน? มี minimum payout ไหม? ใครออก withholding tax?
→ default ที่ใช้วางแผน: 70/30 ตั้งค่าได้ต่อ instructor, หัก payment fee ก่อนแบ่ง, payout รายเดือน, snapshot % ไว้ที่ order item

### Q5 — ทีมและ timeline
Roadmap ใน `ROADMAP.md` ประเมินบนสมมติฐาน **BE 2 คน + FE 2 คน + QA 1 คน แบบเต็มเวลา** — ถ้าทีมเล็กกว่านี้ต้องยืด timeline ตามสัดส่วน

### Q6 — สูตรแบ่งรายได้ + ขอบเขตคอร์สใน Subscription (blocking P10-02 บางส่วน + P10-05)
รายได้บุฟเฟต์รายเดือนจะแบ่งให้ instructor อย่างไร — ไม่ใช่ 70/30 ต่อออร์เดอร์แบบซื้อขาด
→ default ที่เสนอไว้ใช้วางแผน: pool รายได้ subscription ของเดือนนั้น (หลังหัก platform fee) แบ่งตามสัดส่วน**นาทีที่คอร์สของแต่ละ instructor ถูกดูจริง**จาก `WatchEvents` · คอร์สเข้าร่วมแบบ **instructor opt-in** (default เสนอ ยังไม่ตัดสิน)
คำถามย่อยที่ต้องตอบ: (1) opt-in หรือบังคับทุกคอร์ส? — **บล็อก P10-02** (โครง entitlement) (2) สูตร split + มี minimum guarantee ไหม? — **บล็อก P10-05**

### Q7 — โมเดลการขาย/คิดเงิน B2B (blocking P9-03)
ขายที่นั่ง (per-seat) หรือเหมา org? ราคาตายตัวหรือดีลรายองค์กร? จ่ายผ่านระบบ (ต้องมี invoice/บัตร) หรือ invoice ภายนอกแล้ว Superadmin activate ให้?
→ default ที่ใช้วางแผน P9: **invoice ภายนอก + Superadmin activate สิทธิ์เอง** (ไม่ผ่าน checkout) — เลี่ยงงาน billing B2B ทั้งก้อนใน v-แรก, revenue split ฝั่ง instructor สำหรับ B2B ก็ต้องตอบพร้อมกัน (นับเป็นยอดขายเรตไหน)

### Q8 — ระดับการกันดาวน์โหลดที่ยอมรับได้จริง (blocking P2-31, กระทบ exit criteria ของ P2 ทั้งเฟส) 🆕 2026-08-24

`TASKS.md` P2-31 + exit criteria ของ P2 เขียนว่า "IDM / video downloader extension / `yt-dlp` / copy manifest URL / devtools **ต้องล้มเหลวทุกทาง**" แต่ Q1 ตัดสินไปแล้วว่าใช้ **MediaCage Basic** ซึ่งเป็น clear-key encryption ไม่ใช่ Widevine/FairPlay/PlayReady จริง — กุญแจถอดรหัสถึงมือ client แบบที่ดึงออกได้ด้วยเครื่องมือมาตรฐาน แปลว่า bar ที่เขียนไว้มีโอกาสสูงที่จะทำไม่ได้ ไม่ว่าจะเขียนโค้ดดีแค่ไหน

**ต้องเลือกก่อนปิด P2 (ห้าม agent เลือกเอง — เป็นเรื่องความเสี่ยงธุรกิจ + ค่าใช้จ่าย):**
1. อัปเกรด Bunny tier ที่มี DRM จริง แล้วคง bar เดิมไว้ (มีค่าใช้จ่ายเพิ่ม + ต้องยืนยันว่ารองรับ FairPlay สำหรับ Safari/iOS)
2. คง MediaCage Basic แล้ว **แก้ acceptance ของ P2-31 ให้ตรงความจริง** (เช่น "ต้องกันผู้ใช้ทั่วไปและ extension ยอดนิยมได้ · ยอมรับว่าผู้โจมตีที่มีความรู้เชิงเทคนิคยังดึงได้") + ชดเชยด้วย watermark ที่ระบุตัวผู้ดูได้ (P2-22) + rate limit/anomaly detection (P2-06) เพื่อ**ไล่หาต้นตอคนปล่อย** แทนการกันไม่ให้ดึง
3. เลื่อนการตัดสินไปหลังทดสอบจริง — รัน P2-31 เต็มรูปแบบก่อน แล้วค่อยเลือกจากผลจริง (⚠️ ถ้าเลือกข้อนี้ ห้ามปิด P2 จนกว่าจะตัดสินใจ)

**ห้ามปิดช่องนี้ด้วยการลด scope ของเทสต์เอง** — `TASKS.md` P2-31 เขียนกำกับไว้แล้วว่าถ้าทางใดทางหนึ่งสำเร็จต้องรายงานตรง ๆ

> **หมายเหตุเร่งด่วนเพิ่มเติมสำหรับ Q4 (2026-08-24):** Q4 ยังไม่ตอบ แต่โค้ด P6-03 ถูกเขียนไปแล้วโดยเดา 70/30 ตายตัวและตั้งค่าธรรมเนียมการชำระเงินเป็น 0 (`Siri.Modules.Payout/Infrastructure/Contracts/RevenueSplitContract.cs:12`) ทั้งที่ `InstructorProfile.RevenueSharePercent` มีอยู่จริงตั้งแต่ P1-03 — ยิ่งตอบช้า ยิ่งมีโค้ดสร้างทับบนสมมติฐานที่อาจผิด

## C. ADR log
บันทึกการตัดสินใจสถาปัตยกรรมใหม่ทุกครั้งที่ `docs/adr/NNNN-title.md` (format: Context / Decision / Consequences)
