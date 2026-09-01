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
| **D-18** | **ขอบเขตรอบแก้งานหลัง audit 2026-08-28** | หลัง Claude Code ตรวจโค้ดจริงทีละไฟล์เทียบกับ mockup v2026-08-28 (18 หน้าจอ) เจ้าของโปรเจ็คตัดสิน 4 ข้อ: (1) **พักงานวิดีโอ/player/DRM และ login/auth ทั้งหมดในรอบนี้** — ไม่ใช่ยกเลิก แค่ไม่อยู่ในคิว (ยกเว้น `P2-24` ที่ยังทำ เพราะ curriculum หายบนมือถือเป็นบั๊ก layout ไม่ใช่เรื่อง player) (2) **Q-A ฟอร์ม "ติดต่อเรา" → ทำ endpoint จริง** เก็บลง `notify.ContactMessages` + แจ้งทีมผ่าน `EmailOutbox` (`P7-13`/`P7-14`) แทนที่จะเอาฟอร์มออก — ของเดิมเป็น `setTimeout` ปลอมที่ขึ้น toast ว่าส่งสำเร็จโดยไม่ส่งไปไหน (3) **Q-B รายการโปรด (wishlist) → ทำใน v1** (`P1-12`/`P1-29`) ทั้งที่ไม่มี requirement เดิมรองรับ — mockup มีไอคอนหัวใจทุกหน้าจอ (4) **Q-C แท็บล่างมือถือ → ชี้ไปหน้าที่มีอยู่แล้ว** (ค้นหา→`/courses`, ฉัน→`/my-courses`) ไม่สร้าง `/search` และ `/me` ใหม่ | ตัดสินผ่าน AskUserQuestion 2026-08-28 · ข้อ 2 และ 3 เป็นการ**เพิ่ม** scope v1 (ไม่ใช่ตัด) จึงต้องยืนยันชัดเจนไม่ใช่ตีความเอง · ข้อ 2 เลือกทำจริงเพราะฟอร์มที่โกหกผู้ใช้ว่าส่งสำเร็จมีความเสี่ยงมากกว่าการไม่มีฟอร์มเลย · แผนงานเต็มอยู่ที่ `ANTIGRAVITY_HANDOFF.md` §4 (Phase H–L) |
| **D-19** | **Endpoint routing — ยอมรับ ASP.NET Core MVC Controllers แทน Minimal API** (2026-09-01) | integrator-qa audit (`docs/TASKS.md` X-22) พบว่า backend ถูกย้าย routing layer จาก Minimal API (`MapGroup()`/`*Endpoints.cs`) ไปเป็น ASP.NET MVC Controllers (`[ApiController]`, attribute routing) ทั้งระบบโดยไม่มีบันทึกการตัดสินใจที่ไหนมาก่อน — commit `51bd85d`+ตามมา เพิ่ม `src/Siri.Api/Controllers/**` 45 ไฟล์ ครอบคลุมทุกโมดูล รวม 213 action, `Program.cs` เรียก `builder.Services.AddControllers(...)` + `app.MapControllers()` จริง (ไม่มี `app.Map*Endpoints()` เหลืออยู่เลยสักบรรทัด — ยืนยันด้วย grep) เจ้าของโปรเจ็คตัดสินใจ (2026-09-01): **ยอมรับ MVC Controllers เป็นสถาปัตยกรรม routing อย่างเป็นทางการและถาวร** ไม่ revert กลับ Minimal API — สโคปการเปลี่ยนนี้จำกัดเฉพาะ **ชั้น routing/binding เท่านั้น**: request/response DTO record และ business logic ใน `{UseCase}Handler`/`{Entity}Service` (vertical-slice กับ repository+service ตาม D-17) **ไม่ถูกแตะเลย** — controller action ทุกตัวยัง inject handler/service ตัวเดิมผ่าน `[FromServices]` แล้วเรียก `.HandleAsync()`/`{Verb}Async()` เหมือนเดิมทุกประการ, error mapping ยังเป็น `Result<T>.Error.ToProblemHttpResult(HttpContext)` ตัวเดิมจาก `Siri.SharedKernel`, FluentValidation ยังทำงานอัตโนมัติผ่าน `IValidator<T>` ที่ลงทะเบียนไว้ (เปลี่ยนจาก endpoint filter เป็น global MVC `ValidationActionFilter` ตัวใหม่ใน `Siri.SharedKernel`, ผูกครั้งเดียวใน `Program.cs`'s `AddControllers(options => options.Filters.Add<ValidationActionFilter>())`) — convention จริงของ controller (default-deny, การเรียก handler/service, error/validation mapping) อยู่ที่ `.claude/rules/backend.md`'s "## Endpoint" section (เขียนใหม่วันเดียวกันนี้จากการอ่าน controller จริงหลายไฟล์ ไม่ใช่เดา) **⚠️ ยังไม่ได้ลบ Minimal API เก่าออก แม้ยอมรับ Controllers แล้ว**: `*Endpoints.cs` เดิม 36 ไฟล์ (33 ไฟล์นอก Catalog + 3 ไฟล์ใน `Siri.Modules.Catalog`) ตายจริงในโปรดักชัน (ไม่มี `Map*Endpoints()` ของโมดูลไหนถูกเรียกจาก `Program.cs` เลยสักตัว) แต่ **ยังลบไม่ได้จริงตอนนี้** เพราะ integration test ~22 ไฟล์ + unit test 1 ไฟล์ ใน `tests/Siri.IntegrationTests`/`tests/Siri.UnitTests` ยังสร้าง `WebApplication` ของตัวเองแยกจาก `Program.cs`/`SiriApiFactory` แล้วเรียก `_app.Map<Module>Endpoints()` ตรง ๆ เพื่อทดสอบ business logic ผ่าน HTTP โดยไม่ผ่าน Controller เลยสักไฟล์ — ลบไฟล์เก่าตอนนี้จะทำ `Siri.IntegrationTests`/`Siri.UnitTests` compile ไม่ผ่านทันที (ตรงข้ามกับที่ X-22 เข้าใจว่า "ไม่มี project อื่นเรียก Map*Endpoints()" — ข้อสรุปนั้นไม่ครบถ้วน) ต้องมีคนตัดสินใจ/ทำ migration test-harness ไปใช้ `SiriApiFactory`+`MapControllers()` แทนก่อน ถึงจะลบไฟล์ `*Endpoints.cs` เก่าได้จริงโดยไม่เสีย test coverage — เป็นงานแยกที่ยังไม่มี task ID รองรับ ไม่ได้ทำในรอบนี้ (ไม่มีไฟล์ใดถูกลบในรอบทำความสะอาด 2026-09-01) | สถาปัตยกรรมนี้ถูกสร้างและใช้งานจริงมาแล้วเต็มระบบ (45 controller, 213 action) — revert กลับ Minimal API ตอนนี้คืองาน rewrite ทั้งก้อนที่ไม่มีประโยชน์เพิ่ม (pure churn) audit ยืนยันแล้วว่าไม่มี security regression จาก routing layer เอง (สแกน `[Authorize]`/`[AllowAnonymous]` ครบ 213 action ไม่มี default-deny gap โดยไม่ตั้งใจ — มีแค่ 3 controller ที่ public ทั้งคลาสโดยตั้งใจ: Stripe webhook, Bunny webhook, sitemap) — เก็บของเดิมไว้ปนกันต่อไปโดยไม่บันทึกจะยิ่งสร้างความสับสนว่า logic จริงอยู่ไฟล์ไหนทุก PR review ต่อไป |

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

### ✅ Q4 — Revenue split = **70/30 ปรับได้รายคน · หักค่าธรรมเนียมก่อนแบ่ง · จ่ายรอบเดือน ขั้นต่ำ ฿500** (ตัดสิน 2026-08-25)

> **ใครตัดสิน:** เจ้าของโปรเจ็คสั่งให้ Claude Code ตัดสินแทน (2026-08-25) · ข้อที่ยังต้องยืนยันกับข้อมูลจริงก่อนใช้จริงอยู่ท้ายหัวข้อ — **สองข้อนั้นไม่บล็อกการเขียนโค้ด** เพราะออกแบบให้เป็นค่า config ทั้งคู่

**1. สัดส่วนแบ่ง — 70/30 เป็นค่าเริ่มต้น แต่ปรับรายคนได้**
- อ่านจาก `InstructorProfile.RevenueSharePercent` เสมอ (มีอยู่แล้วตั้งแต่ P1-03 default `70.00`) — **ห้าม hardcode 70/30 ในโค้ด**
- ตอนสร้างแถว `REVENUE_SPLITS` ให้ **snapshot ค่า %** ลงไปด้วย → ปรับเรตของผู้สอนทีหลังไม่กระทบรายการที่จ่ายไปแล้ว
- การปรับเรตรายคนเป็น action ของแอดมิน ต้องเขียน audit ทุกครั้ง (ใครปรับ เมื่อไหร่ จากเท่าไรเป็นเท่าไร)

**2. ค่าธรรมเนียมการชำระเงิน — หักก่อนแบ่ง (ทั้งสองฝ่ายรับร่วมกันตามสัดส่วน)**

```
netAmount        = PAID_AMOUNT - paymentFee
instructorAmount = ROUND(netAmount * sharePercent / 100, 2)
platformAmount   = netAmount - instructorAmount      // เศษจากการปัดตกเป็นของแพลตฟอร์ม
```

- `platformAmount` คำนวณแบบ "ส่วนที่เหลือ" ไม่ใช่คูณแยก — กันยอดสองฝั่งรวมแล้วไม่เท่ากับ `netAmount` เพราะการปัดเศษ
- `paymentFee` ต้องเป็น **ค่าธรรมเนียมจริงต่อรายการ** ที่ดึงจาก Stripe balance transaction ไม่ใช่ % ประมาณเอา · ระหว่างที่ยังต่อไม่ได้ ให้ใช้ `PaymentOptions:EstimatedFeePercent` (config, ค่าเริ่มต้นตั้งตามเรตจริงที่เห็นใน dashboard) แล้วให้ job reconcile แก้ยอดทีหลัง
- **เหตุผลที่เลือกหักก่อนแบ่ง:** (ก) ส่วนแบ่งแพลตฟอร์ม 30% เป็นค่าดำเนินการ ไม่ใช่กำไรสุทธิ ถ้าแบกค่าธรรมเนียมฝ่ายเดียวมาร์จินจะเหลือ ~27% และยิ่งบางลงเมื่อขายคอร์สราคาถูกหรือมีส่วนลด (ข) เรตค่าธรรมเนียมจะเปลี่ยนเมื่อเปิดรับบัตรใน P10 (บัตรแพงกว่า PromptPay) — หักก่อนแบ่งทำให้สูตรไม่ต้องรื้อเมื่อ mix ของวิธีจ่ายเปลี่ยน (ค) ตรงกับแนวปฏิบัติของ marketplace ทั่วไป
- **ต้องบอกผู้สอนให้ชัดในหน้ารายได้** ว่ายอดที่เห็นคำนวณจากยอดหลังหักค่าธรรมเนียม พร้อมแสดงค่าธรรมเนียมแยกบรรทัด — ไม่ใช่ซ่อนแล้วให้สงสัยว่าทำไมไม่ได้ 70% เป๊ะ

**3. รอบจ่าย — ตัดยอดสิ้นเดือน จ่ายวันที่ 25 ของเดือนถัดไป**
- ขั้นต่ำ **฿500** ต่อรอบ ไม่ถึงให้ทบไปรอบถัดไป (ยอดสะสมไม่หาย)
- นับเฉพาะ `REVENUE_SPLIT` ที่ order เป็น `Paid` และ **ผ่าน hold 14 วัน** นับจากวันที่จ่ายเงินสำเร็จ ณ วันตัดยอด — กันจ่ายให้ผู้สอนแล้วผู้ซื้อขอคืนเงินทีหลัง ⚠️ เมื่อ `P7-09` กำหนดนโยบายคืนเงินอย่างเป็นทางการ **hold ต้องไม่สั้นกว่าหน้าต่างคืนเงินนั้น**
- ต้องมี `INSTRUCTOR_PAYOUT_ACCOUNTS` ที่ยืนยันแล้วถึงจะถูกดึงเข้า batch ได้
- refund ที่เกิด **หลัง** จ่าย payout ไปแล้ว → ลงเป็นรายการติดลบในรอบถัดไป (reversal) **ห้ามแก้ batch เก่าย้อนหลัง** (ข้อห้าม hard delete/แก้ข้อมูลการเงินใน `database.md`)

**4. ภาษีหัก ณ ที่จ่าย — แพลตฟอร์มเป็นผู้หักและนำส่ง 3% (ค่าบริการ)**
- เก็บประเภทผู้เสียภาษี (บุคคลธรรมดา / นิติบุคคล) ไว้ที่ `INSTRUCTOR_PAYOUT_ACCOUNTS` เพราะแบบยื่นต่างกัน (ภ.ง.ด.3 / ภ.ง.ด.53)
- เรต **เก็บเป็น config ไม่ hardcode** และ snapshot ลง `PAYOUT_BATCH_ITEMS` ตอนปิดรอบ
- ระบบต้องออกเอกสาร/ข้อมูลสำหรับหนังสือรับรองการหักภาษี ณ ที่จ่ายให้ผู้สอนดาวน์โหลดได้ (ทำใน P6-04 หรือแยกเป็น task ย่อยได้ แต่ต้องมีก่อนรอบจ่ายจริง)

**⚠️ สองข้อที่ต้องยืนยันก่อนรอบจ่ายเงินจริงรอบแรก (ไม่บล็อกการเขียนโค้ด เพราะทั้งคู่เป็น config):**
1. **เรตค่าธรรมเนียมจริงของ Stripe Thailand** (PromptPay และบัตร) จาก dashboard จริง → ใส่เป็นค่าเริ่มต้นของ `EstimatedFeePercent`
2. **สถานะทางภาษีของผู้ประกอบการ + หน้าที่หัก ณ ที่จ่าย** — ยืนยันกับนักบัญชี ว่าในรูปแบบธุรกิจจริง (บุคคลธรรมดา/นิติบุคคล) มีหน้าที่หัก 3% และนำส่งจริงหรือไม่ · **นี่เป็นเรื่องกฎหมายภาษี ผมตัดสินให้ได้แค่ในเชิงออกแบบระบบ ไม่ใช่คำแนะนำทางบัญชี**


### Q5 — ทีมและ timeline
Roadmap ใน `ROADMAP.md` ประเมินบนสมมติฐาน **BE 2 คน + FE 2 คน + QA 1 คน แบบเต็มเวลา** — ถ้าทีมเล็กกว่านี้ต้องยืด timeline ตามสัดส่วน

### Q6 — สูตรแบ่งรายได้ + ขอบเขตคอร์สใน Subscription (blocking P10-02 บางส่วน + P10-05)
รายได้บุฟเฟต์รายเดือนจะแบ่งให้ instructor อย่างไร — ไม่ใช่ 70/30 ต่อออร์เดอร์แบบซื้อขาด
→ default ที่เสนอไว้ใช้วางแผน: pool รายได้ subscription ของเดือนนั้น (หลังหัก platform fee) แบ่งตามสัดส่วน**นาทีที่คอร์สของแต่ละ instructor ถูกดูจริง**จาก `WatchEvents` · คอร์สเข้าร่วมแบบ **instructor opt-in** (default เสนอ ยังไม่ตัดสิน)
คำถามย่อยที่ต้องตอบ: (1) opt-in หรือบังคับทุกคอร์ส? — **บล็อก P10-02** (โครง entitlement) (2) สูตร split + มี minimum guarantee ไหม? — **บล็อก P10-05**

### Q7 — โมเดลการขาย/คิดเงิน B2B (blocking P9-03)
ขายที่นั่ง (per-seat) หรือเหมา org? ราคาตายตัวหรือดีลรายองค์กร? จ่ายผ่านระบบ (ต้องมี invoice/บัตร) หรือ invoice ภายนอกแล้ว Superadmin activate ให้?
→ default ที่ใช้วางแผน P9: **invoice ภายนอก + Superadmin activate สิทธิ์เอง** (ไม่ผ่าน checkout) — เลี่ยงงาน billing B2B ทั้งก้อนใน v-แรก, revenue split ฝั่ง instructor สำหรับ B2B ก็ต้องตอบพร้อมกัน (นับเป็นยอดขายเรตไหน)

### ✅ Q8 — Anti-piracy bar = **คง MediaCage Basic แล้วแก้ acceptance ให้ตรงความจริง + ชดเชยด้วย forensic watermark** (ตัดสิน 2026-08-25)

> **ใครตัดสิน:** เจ้าของโปรเจ็คสั่งให้ Claude Code ตัดสินแทน (2026-08-25) · **เลือกทางที่ 2** จาก 3 ทางที่เสนอไว้เดิม

**ปัญหาเดิม:** `TASKS.md` P2-31 + exit criteria ของ P2 เขียนว่า "IDM / extension / `yt-dlp` / copy manifest URL / devtools **ต้องล้มเหลวทุกทาง**" แต่ Q1 เลือก **MediaCage Basic** ซึ่งเป็น clear-key encryption ไม่ใช่ Widevine/FairPlay/PlayReady — กุญแจถอดรหัสถึงมือ client ในรูปแบบที่ดึงออกได้ด้วยเครื่องมือมาตรฐาน bar เดิมจึงเป็นเป้าที่ทำไม่ได้จริงไม่ว่าจะเขียนโค้ดดีแค่ไหน

**สิ่งที่ตัดสิน:**

1. **ไม่อัปเกรด tier ตอนนี้** — เก็บงบไว้ก่อน · เหตุผล: (ก) ภัยจริงของ marketplace คอร์สไทยส่วนใหญ่คือการแชร์บัญชีและอัดจอแบบง่าย ๆ ซึ่ง MediaCage Basic + concurrent-session limit (SE-03, ทำแล้วตั้งแต่ P0-17) + signed URL อายุสั้นรับมือได้ (ข) DRM จริงต้องทำ FairPlay certificate กับ Apple เพิ่ม เป็นงานและเวลาที่ไม่คุ้มก่อน launch (ค) ถ้าถึงจุดที่คุ้มค่อยอัปเกรดได้ทีหลัง — โค้ดอยู่หลัง `IVideoProvider` อยู่แล้ว ไม่ใช่ทางตัน
2. **เปลี่ยนกลยุทธ์จาก "กันไม่ให้ดึง" เป็น "ดึงได้แต่รู้ว่าใครปล่อย"** — จุดที่ต้องแข็งจริงคือ:
   - `P2-22` **dynamic watermark** ที่ระบุตัวผู้ดูได้ (ชื่อ+อีเมล+timestamp, payload มาจาก server เท่านั้น) — กลายเป็นของบังคับ ไม่ใช่ของแถม เพราะเป็นกลไกไล่ต้นตอหลัก
   - `P2-04` signed URL อายุ 2–5 นาที + entitlement check ทุกครั้ง (ทำแล้ว)
   - `P2-06` anomaly detection (ดูเกินปกติ/หลาย IP) — เลื่อนความสำคัญขึ้นจาก "ทำก็ดี" เป็น "ต้องมีก่อน launch" เพราะเป็นเซนเซอร์ตัวเดียวที่จับการปล่อยเนื้อหาได้
   - `P0-17` concurrent-session limit (ทำแล้ว) — คือด่านที่กันการแชร์บัญชีจริง ๆ
3. **แก้ acceptance ของ P2-31 ให้เป็นเป้าที่วัดได้จริง** (แทนข้อความ "ต้องล้มเหลวทุกทาง"):
   - ✅ ต้องกันได้: ดาวน์โหลดตรงจาก URL ในแท็บ network, copy manifest URL ไปเปิดเครื่องอื่น/หลังหมดอายุ, IDM และ video-downloader extension ยอดนิยม, การเข้าถึงโดยไม่มี enrollment
   - ⬜ ยอมรับว่าทำไม่ได้: ผู้โจมตีที่ดึง clear-key ออกจาก player ด้วยเครื่องมือเฉพาะทาง (เช่น `yt-dlp` + key extraction) และการอัดหน้าจอ
   - 📋 ต้องพิสูจน์เพิ่ม: วิดีโอที่หลุดออกไปต้อง **ระบุตัวผู้ดูต้นทางได้จาก watermark** (ทดสอบจริง: อัดหน้าจอ 1 คลิปแล้วอ่านย้อนว่าเป็นบัญชีไหน)
   - **ห้ามลด scope ของเทสต์เพื่อให้ผ่าน** — ถ้าเจอช่องที่อยู่ในกลุ่ม "ต้องกันได้" แล้วกันไม่ได้ ต้องรายงานตรง ๆ ไม่ใช่ย้ายมันไปกลุ่ม "ยอมรับ"
4. **เงื่อนไขที่จะกลับมาทบทวน (revisit trigger)** — ให้ยกกลับมาพิจารณาอัปเกรด tier เมื่อเข้าข้อใดข้อหนึ่ง:
   - พบการปล่อยคอร์สจริงจาก watermark forensics มากกว่า 1 ครั้ง
   - มีคอร์สราคาสูง (> ฿10,000) หรือลูกค้าองค์กร (P9) ที่ระบุ DRM เป็นเงื่อนไขในสัญญา
   - Bunny ออก tier ที่ราคาต่างไปอย่างมีนัยสำคัญ

**ผลต่อ P2-05 (DRM license proxy):** MediaCage Basic ไม่มี license server แบบ DRMจริงให้ proxy — งานของ P2-05 จึงเปลี่ยนรูปเป็น "**key delivery ต้องผ่าน endpoint ของเราที่ตรวจ entitlement ซ้ำ ห้ามให้ client คุยกับ Bunny ตรง**" ตามเจตนาเดิมของ `security.md` · ถ้าตรวจแล้วพบว่า Bunny ไม่เปิดให้ทำแบบนั้นเลยในระดับ Basic ให้รายงานกลับมาก่อน อย่าเงียบ ๆ ปล่อยให้ client คุยตรง

### Q9 — Virus Scanning Engine for Attachments (P4-03, 2026-08-27)
ระบบตรวจไฟล์แนบ (`P4-03b`) วาง interface seam `IAttachmentVirusScanner` และ `NullAttachmentVirusScanner` ใน DI เรียบร้อยแล้ว แต่ยังไม่ได้ตัดสินใจเลือก scanning engine/infrastructure จริง (เช่น ClamAV daemon ภายใน VPS, AWS GuardDuty / S3 Malware Protection, หรือ Cloud Storage Anti-malware API)
→ **สถานะปัจจุบัน:** ยังไม่ตัดสินใจเลือก engine โดยตรง (ตามกฎความปลอดภัย §2 ห้ามติดตั้ง security dependency เองโดยพลการ) และใช้งาน `NullAttachmentVirusScanner` เป็น default seam เพื่อให้ business logic ใน Catalog module ทำงานได้อย่างสมบูรณ์และพร้อมสลับ implementation เมื่อทีม infra/security ตัดสินใจ


## C. ADR log
บันทึกการตัดสินใจสถาปัตยกรรมใหม่ทุกครั้งที่ `docs/adr/NNNN-title.md` (format: Context / Decision / Consequences)
