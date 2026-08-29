# Antigravity task hand-off

คำสั่งสำหรับ Antigravity (coding agent อีกตัวที่เจ้าของโปรเจ็คใช้คู่กับ Claude Code)

> **แก้ทั้งไฟล์ 2026-08-24** — ฉบับก่อนหน้า (2026-08-20/21) บรรยายสถานะว่า "ทุกอย่างเป็น scaffold ที่ `throw NotImplementedException`" ซึ่ง**ไม่จริงอีกต่อไปแล้ว** ตั้งแต่ Antigravity ลงมือทำจริง ใครใช้ฉบับเก่าจะได้คำสั่งที่อ้างสถานะผิด

---

## 1. สถานะจริง ณ 2026-08-28 (Claude Code อ่านโค้ดจริงทีละไฟล์ — แทนตารางฉบับ 2026-08-27)

> ⚠️ ตารางฉบับ 2026-08-27 เขียนว่า **"หน้า UI ต่อ API จริงครบ 100%"** และ **"Player ใช้งานจริง"** — ตรวจแล้ว **ทั้งสองข้อไม่จริง** ดูแถวที่ทำเครื่องหมาย ❗ ข้างล่าง · บทเรียนเดิมที่ยังใช้ได้: **อย่ารายงานว่า "เสร็จ 100%" ถ้ายังไม่ได้เปิดไฟล์อ่าน method body จริง**

| เรื่อง | สถานะจริง (verify 2026-08-28) |
|-------|-----------|
| Integration test | ✅ **ครบทั้ง 7 โมดูลใหม่แล้วจริง** (`OrderEndpointsTests`, `OrderExpiryIntegrationTests`, `PromoCodeTests`, `MediaIntegrationTests`, `LearningIntegrationTests`, `PayoutIntegrationTests`, `CmsAndCommunityIntegrationTests`, `AnalyticsIntegrationTests`) — `X-1` ปิดแล้ว · แต่ **ยังไม่เคยถูกรันจริงเลยสักครั้ง** เพราะ `X-2` |
| ❗ **commit / CI** | 🔴 **ของค้างไม่ commit 365 ไฟล์** (commit ล่าสุด `46e7ea0`, 2026-08-27) → CI ไม่เคยรัน → integration test ทั้งหมดข้างบนยังไม่เคยพิสูจน์ตัวเองเลย · **คอขวดอันดับ 1 ของโปรเจ็คตอนนี้ (`P0-39` — เจ้าของโปรเจ็คต้องทำ)** |
| ❗ **หน้า UI ต่อ API จริง** | ⚠️ **ไม่ใช่ 100%** — หน้าส่วนใหญ่ต่อจริงแล้ว (finding `X-3` เดิมที่ว่า 6 หน้าเป็น mock ล้วน **ล้าสมัยแล้ว ปิดได้**) แต่ยังมี **ปุ่มที่กดแล้วไม่เกิดอะไร**: บันทึกบัญชีรับเงิน (`P4-26`), toggle banner (`P6-20`), dropdown คอร์สใน announcements (`P4-27`), checkout seed order ปลอม (`P3-21`) — ดู Phase J |
| ❗ **หน้า builder ส่งค่าปลอมขึ้น API** | 🔴 หมวดหมู่/outcomes/requirements/SEO ไม่มี UI แต่ถูกส่งเป็นค่า hardcode ทุกครั้งที่สร้างคอร์ส — **กำลังเขียนข้อมูลผิดลง DB จริงอยู่ตอนนี้** ดู `P4-22` / Phase H1 |
| ❗ **ฟอร์ม "ติดต่อเรา"** | 🔴 เป็นของปลอมทั้งอัน (`setTimeout` + toast "ส่งเรียบร้อยแล้ว" โดยไม่ส่งไปไหน, backend ไม่มี endpoint) ดู `X-15` / Phase H2 |
| ❗ Player | ⚠️ `shaka-player` ต่อจริงแล้ว **แต่มีบั๊กทำให้วิดีโอไม่เล่นตอนเปิดหน้าครั้งแรก** (`P2-20`) · **เรื่อง player/DRM ถูกพักไว้ทั้งหมดในรอบนี้ตามคำสั่งเจ้าของโปรเจ็ค** ดู §4.0 |
| Feature flag / Reviews API | ✅ มีจริงทั้งคู่ (feature flag อยู่ใน Cms, `CourseReview` มี entitlement check จริง) · แต่ **หน้า course detail ไม่เคยเรียก API รีวิวเลย** (Phase I3) และ review ยังขาด reconcile job (Phase L5) |
| i18n | 🔴 **25 จาก 38 เทมเพลตยังสลับภาษาไม่ได้** — `admin` 0/9, `legal` 0/5, `blog` 0/2, `instructor` 1/6 · แย่กว่าตัวเลขที่เคยบันทึกไว้ ดู `X-4` / Phase L2 |
| error state | 🔴 **14 หน้าที่ดึงข้อมูลไม่มี error state** — API ล่ม = หน้าว่างเปล่า ดู `X-14` / Phase L1 |
| กฎ Angular | ✅ `@for` มี `track` ครบ · ไม่มี `standalone: false` · **ไม่มี `any` เลยทั้ง codebase** · ⚠️ แต่ `@Input()/@Output()` เก่า 25 จุด, ขาด `OnPush` 4/49, `<img>` ดิบ 14 จุด ดู `X-16` |

**อ่านสถานะรายงานเสมอที่ `docs/TASKS.md`** — คอลัมน์ `Status` ถูก Claude Code สอบทานกับโค้ดจริงแล้วเมื่อ 2026-08-28 (ดู `TASKS.md` § X-10/X-11) จึงเชื่อได้แล้ว ณ วันนั้น · **ยังห้าม Antigravity ตั้ง `DONE` ให้ตัวเอง** ตาม §2 ข้อ 3

---

## 2. กฎการทำงาน (เปลี่ยนจากฉบับเดิม — อ่านให้ครบก่อนหยิบงาน)

1. **หยิบทีละ task ตาม ID จากคิวใน §4** ห้ามข้ามลำดับเอง ถ้าเห็นว่าควรสลับ ให้บอกเจ้าของโปรเจ็คก่อน
2. **1 task = 1 commit** — `feat(commerce): promo code redemption [P3-09]` (Conventional Commits, ภาษาอังกฤษ) · ห้าม `git push` เอง ห้ามสร้าง PR เอง
3. **จบงานแล้วอัปเดต `docs/TASKS.md` ทันที**: ตั้ง Status เป็น `BUILT` (ครบ acceptance) หรือ `PART` (+เขียนบรรทัดใน § "สถานะจริง" ว่าขาดอะไร) — **ห้ามตั้ง `DONE` ให้ตัวเอง** `DONE` ตั้งได้โดย Claude Code หลัง review เท่านั้น
4. **ห้ามเดาค่าที่เป็นการตัดสินใจธุรกิจ** — สูตรแบ่งรายได้, สิทธิ์เข้าถึง, ราคา, นโยบายคืนเงิน ถ้า `docs/DECISIONS.md` ยังไม่ปิดคำถามนั้น ให้**หยุดแล้วถาม** ห้ามใส่ค่า default ลงโค้ด (เกิดขึ้นมาแล้วจริงที่ P6-03: hardcode 70/30 ทั้งที่ Q4 ยังไม่ตอบ)
5. **ห้ามรัน `dotnet ef database update` กับ connection string จริงของ Contabo** — สร้าง migration ได้ แต่ให้เจ้าของโปรเจ็ค apply เอง
6. **ขอบเขตโมดูลเดิม (แก้จากฉบับเก่าที่เขียนว่า "ห้ามแตะ Identity/Catalog เด็ดขาด" ซึ่งขัดกับงานหลายตัวเอง):**
   - แตะ `Siri.Modules.Identity` / `Siri.Modules.Catalog` / `Siri.Modules.Notification` **ได้** เมื่อ task ระบุไว้ตรง ๆ (เช่น P4-01 builder API อยู่ใน Catalog, P6-27 ใช้ Identity)
   - แต่ต้อง **ทำตาม pattern ของโมดูลนั้น** (vertical slice: `Features/{UseCase}/{Command,Validator,Handler,Endpoint,Response}.cs` + PascalCase ปกติ) **ห้ามเอา Repository+Service/UPPERCASE ไปใส่**
   - โมดูลอื่นเรียกข้ามมาได้ทาง `Contracts/` เท่านั้น (ArchitectureTest บังคับอยู่ — ถ้าเทสต์แดงแปลว่าออกแบบผิด ห้ามแก้เทสต์)
   - **ห้ามแก้ handler/endpoint เดิมที่มี Status `DONE`** โดยไม่ถาม
7. **ห้ามใช้ role name เป็น string ดิบ** (`IsInRole("Admin")`) — ปัญหานี้มีอยู่แล้ว 3 จุด (X-5 ใน TASKS.md) ถ้า task ของคุณต้องใช้ ให้สร้าง `RoleNames` ใน `Siri.SharedKernel` (แบบเดียวกับ `AuthorizationPolicyNames`) พร้อม unit test ยืนยันว่าค่าตรงกับ `Identity/Domain/Role.cs` แล้วใช้ตัวนั้น
8. **ข้อความ user-facing ต้องอยู่ใน i18n เสมอ** (`core/i18n/th.json` + `en.json`) — ห้าม hardcode ไทย/อังกฤษในเทมเพลต หน้าเก่าหลายหน้าทำผิดข้อนี้อยู่ (X-4) อย่าเพิ่มหน้าใหม่ที่ผิดซ้ำ
9. **ห้ามส่งหน้า UI ที่ใส่ข้อมูลปลอม** — ถ้า API ยังไม่มี ให้ทำ task ฝั่ง API ก่อน หรือรายงานว่าติด อย่าใส่ตัวเลข/ชื่อคนปลอมลง component (เกิดขึ้นแล้ว 6 หน้า — X-3)
10. **ถ้า task มีไฟล์ contract ใน `docs/contracts/<TASK-ID>-*.md` ให้อ่านก่อนเป็นอันดับแรก** — ไฟล์นั้นคือข้อผูกพัน (path, DTO shape, status code, auth policy, rate limit, schema delta) **ชนะคำบรรยายในบล็อกคำสั่งถ้าขัดกัน** · เริ่มลงมือได้เฉพาะเมื่อบรรทัด `Status:` เป็น `FROZEN` — ถ้ายังเป็น `DRAFT` ให้หยุดแล้วรายงาน · **ถ้าพบว่า contract ทำไม่ได้จริง ให้หยุดแล้วรายงาน ห้ามแก้ไฟล์ contract เอง ห้าม deviate เงียบ ๆ** (ฝั่งตรงข้าม BE/FE กำลังทำจาก contract ฉบับเดียวกันคู่ขนานอยู่) · ถ้ามี `## Changelog` เพิ่มเข้ามาระหว่างที่คุณทำอยู่ ให้ถือเป็น diff ทางการแล้วไล่ reconcile โค้ดที่เขียนไปแล้วให้ครบทุกข้อ

> **ทีมฝั่ง Claude Code (อัปเดต 2026-08-28):** ตอนนี้ Claude มี subagent เฉพาะทาง 4 ตัวที่ `.claude/agents/` — `system-architect` (ออกแบบ schema + contract + บล็อกคำสั่งให้คุณ), `backend-developer`/`frontend-developer` (ลงมือเมื่องานแตะเงิน/สิทธิ์/ความปลอดภัย หรือแก้ `Contracts/` ข้ามโมดูล), `integrator-qa` (review งานของคุณ + เป็นคนตั้ง Status `DONE`) · **การแบ่งงานโดยปริยาย:** งาน feature/UI/integration test ทั่วไปในคิว §4 เป็นของคุณ · งานแตะเงิน สิทธิ์เข้าถึง playback entitlement auth ข้อมูลอ่อนไหว และ `Contracts/` interface ข้ามโมดูล เป็นของฝั่ง Claude — ถ้า task ที่คุณหยิบกลายเป็นแบบหลังกลางทาง ให้หยุดแล้วรายงาน

---

## 3. Definition of Done (บังคับทุก task — เปลี่ยนจากเดิม)

- [ ] `dotnet build backend/SiriUpSkill.sln` — 0 warning / 0 error (repo ตั้ง `TreatWarningsAsErrors`)
- [ ] `dotnet test backend/tests/Siri.UnitTests/Siri.UnitTests.csproj` + `Siri.ArchitectureTests` — เขียวทั้งหมด
- [ ] **มี integration test ใหม่ใน `backend/tests/Siri.IntegrationTests/` สำหรับ endpoint/flow ที่ task นี้แตะ** (Testcontainers MSSQL+Redis จริง ห้าม InMemory) — เขียนตาม pattern ของ `CourseManagementTests.cs`
      · เครื่อง dev ไม่มี Docker → เทสต์จะขึ้น `DockerUnavailableException` เป็นเรื่องปกติ **แต่ต้องเขียนให้ครบ** แล้วรายงานว่ายังไม่เคยรันจริง จนกว่า CI จะรันให้ (ดู `P0-39`)
- [ ] ฝั่ง FE: `npm --prefix frontend run lint` + `test` + `build` เขียว
- [ ] ทุก endpoint ที่แตะข้อมูลผู้ใช้มี authorization + **ownership check** จริง (`security.md` ไม่มีข้อยกเว้น) และ `userId` มาจาก `IUserContext` เท่านั้น ห้ามรับจาก body/query
- [ ] endpoint กลุ่มเสี่ยง (playback, checkout, webhook, auth) มี rate limit policy ระบุชัด
- [ ] ไม่มี secret ในไฟล์ที่ commit — ค่าจริงผ่าน `dotnet user-secrets` เท่านั้น
- [ ] เงินเป็น `decimal` + `.HasPrecision(18,2)` · ห้าม cascade delete/hard delete บนตารางเงินและสิทธิ์เรียน
- [ ] entity ใหม่ในโมดูล UPPERCASE: property ของ `IAuditable`/`ISoftDelete` (`CreatedAtUtc`/`CreatedBy`/`UpdatedAtUtc`/`UpdatedBy`/`IsDeleted`/`DeletedAtUtc`) **ต้องเป็น PascalCase** เสมอ ไม่งั้น `AuditableEntityInterceptor` พังตอน runtime (ไม่ใช่ตอน compile) — เหตุผลเต็มใน `.claude/rules/database.md`
- [ ] อัปเดต Status ใน `docs/TASKS.md` + บรรทัดใน § "สถานะจริง" ถ้าเป็น `PART`
- [ ] รายงานตรง ๆ ว่าอะไรยังไม่ได้ทำและเพราะอะไร — ห้ามสรุปว่า "เสร็จ" ถ้ายังไม่ได้รัน build/test จริง

---

## 4. คิวงาน — ฉบับ 2026-08-28 (แทนคิว Phase A–G ฉบับ 2026-08-27 ทั้งหมด)

> **ทำไมต้องเขียนใหม่:** Claude Code ตรวจโค้ดจริงทีละไฟล์เมื่อ 2026-08-28 แล้วพบว่าคิวเดิมล้าสมัยเกือบทั้งหมด — Phase A/B ปิดไปแล้ว, Phase C (integration test 6 โมดูล) **ครบทั้ง 7 โมดูลแล้ว**, Phase D/E ส่วนใหญ่ทำไปแล้ว · และพบของใหม่ที่หนักกว่าทุกอย่างในคิวเดิมซึ่งไม่เคยมีใครบันทึก (ดู H1/H2)
>
> **ขอบเขตของแผนนี้ (เจ้าของโปรเจ็คสั่ง 2026-08-28):** ตัด **วิดีโอ/player/DRM** และ **login/auth** ออกทั้งหมด — ไม่ใช่ยกเลิก แค่ยังไม่ทำรอบนี้ · ตัดงานที่ต้องพึ่งของนอกระบบออกเหมือนเดิม (VPS, บัญชี vendor, คนจริง)
>
> **วิธีใช้:** ทำจากบนลงล่างทีละ task · จบแล้ว commit (1 task = 1 commit) → อัปเดต Status ใน `docs/TASKS.md` เป็น `BUILT`/`PART` (**ห้ามตั้ง `DONE` ให้ตัวเอง** ตาม §2 ข้อ 3) → รายงานกลับก่อนหยิบตัวถัดไป
> **ขนาด:** S = ครึ่งวัน · M = 1–2 วัน · L = 3+ วัน

### 4.0 ตัดออกจากแผนนี้

| กลุ่ม | Task | ติดที่ |
|-------|------|--------|
| **วิดีโอ/player/DRM** (เจ้าของสั่งพักรอบนี้) | `P2-05` `P2-06` `P2-20` `P2-21` `P2-22` `P2-23` `P2-25` `P2-30` `P2-31` | พักไว้ตามคำสั่ง — ⚠️ **ยกเว้น `P2-24` ที่ยังอยู่ในคิว (I2)** เพราะเป็นบั๊ก layout ไม่ใช่เรื่องวิดีโอ |
| **login/auth** (เจ้าของสั่งพักรอบนี้) | `P8-01` `P8-02` + งานที่แตะ auth flow เดิม | พักไว้ตามคำสั่ง |
| VPS / infra | `P0-01` `P0-02` `P0-03` `P0-04` `P0-05` `P0-06` `P0-08` · `P0-09` · `P0-40` · **`P0-39`** | ต้องมีสิทธิ์ SSH / ต่อ DB จริง — **`P0-39` (commit + push ให้ CI รัน) เป็นคอขวดอันดับ 1 ของโปรเจ็คตอนนี้ ต้องให้เจ้าของทำ** |
| บัญชี vendor | `P2-01` · คีย์ Stripe ตัวจริง | ต้องล็อกอินบัญชี vendor |
| ทดสอบที่ต้องใช้คน/เครื่องจริง | `P4-30` `P7-03` `P7-07` `P7-10` `P7-11` | ต้องมีคน/อุปกรณ์/ระบบจริง |

> ⚠️ **integration test เขียนได้เสมอ ให้เขียนทุก task** แม้เครื่อง dev ไม่มี Docker — จะถูกรันจริงครั้งแรกบน CI

### 4.1 ปิดจากคิวเดิมแล้ว — ห้ามหยิบซ้ำ (verify 2026-08-28)

`X-9` `P4-03a` `P4-03b` `X-5` (Phase A ทั้งหมด) · `P3-03` `P3-06` `P3-07` (มี `OrderExpiryJob` + `PaymentOpsEndpoints` จริงแล้ว) · `P6-02` · **`X-1` integration test ครบทั้ง 7 โมดูลแล้ว** (`OrderEndpointsTests`, `OrderExpiryIntegrationTests`, `PromoCodeTests`, `MediaIntegrationTests`, `LearningIntegrationTests`, `PayoutIntegrationTests`, `CmsAndCommunityIntegrationTests`, `AnalyticsIntegrationTests`) · `P4-04` `P3-25` `P3-20` `P5-23` `P1-08`(โค้ดหลัก) `P1-25` `P4-21` `P4-25` `P4-20` · **feature flag ของ `P6-06` มีแล้ว** (อยู่ใน Cms) · `P6-04` config 3 ข้อแก้ครบแล้ว

### 4.2 ✅ คำตอบจากเจ้าของโปรเจ็ค (ตัดสินแล้ว 2026-08-28 — บันทึกเป็น D-18 ใน `DECISIONS.md`)

| # | คำถาม | **คำตอบ** | ผลต่อคิว |
|---|-------|----------|---------|
| Q-A | ฟอร์ม "ติดต่อเรา" | ✅ **ทำ endpoint จริง เก็บลง DB + แจ้งทีม** | H2 ปลดบล็อกแล้ว → แตกเป็น `P7-13` (BE) + `P7-14` (FE) |
| Q-B | รายการโปรด (wishlist) | ✅ **ทำใน v1** | I1 ทำไอคอนตะกร้าอย่างเดียวเหมือนเดิม · wishlist แยกเป็น `P1-12` (BE) + `P1-29` (FE) ที่ Phase K |
| Q-C | แท็บ `/search` และ `/me` | ✅ **ชี้ไปหน้าที่มีอยู่แล้ว** — ค้นหา → `/courses` · ฉัน → `/my-courses` | K9 ปลดบล็อกแล้ว **ไม่ต้องสร้างหน้าใหม่** |

> **ไม่มีคำถามค้างแล้ว** — ทุก task ในคิวนี้เริ่มได้ทันที · ถ้าเจอการตัดสินใจธุรกิจใหม่ระหว่างทาง ให้หยุดแล้วถามตาม §2 ข้อ 4 เหมือนเดิม

---

### Phase H — 🔴 ของปลอมที่กำลังไหลลงข้อมูลจริง (ทำก่อนทุกอย่าง)

> ทั้งสองข้อนี้ไม่ใช่ "ฟีเจอร์ที่ยังไม่เสร็จ" แต่เป็น**ระบบที่กำลังโกหกผู้ใช้/เขียนข้อมูลผิดลงฐานข้อมูลอยู่ทุกวัน** ยิ่งช้ายิ่งต้องตามล้างข้อมูลทีหลัง

| # | Task | ทำอะไร | ขนาด |
|---|------|--------|------|
| H1 | `P4-22` | **หน้า builder ส่งค่าปลอมขึ้น API ทุกครั้งที่ผู้สอนสร้างคอร์ส** — (1) ไม่มี `<select>` หมวดหมู่ในเทมเพลตเลย (`<select>` ตัวเดียวที่มีคือ *ระดับ* ที่ `course-builder-page.html:272`) แต่ `categoryId` ถูกเซ็ตเป็น `flat[0].id` เงียบ ๆ ที่ `ts:219` → ทุกคอร์สถูกยัดเข้าหมวดหมู่แรกสุดเสมอ · (2) `outcomes`/`requirements` ไม่มี UI แต่ signal ถูก seed ด้วยข้อความไทย hardcode ที่ `ts:170-175` แล้วส่งขึ้น API จริงที่ `ts:758-759` → ไปโผล่เป็น "สิ่งที่คุณจะได้เรียน" จริงบนหน้าคอร์สสาธารณะ · (3) `seoTitle`/`seoDescription` ถูก mirror จาก title/subtitle เงียบ ๆ ที่ `ts:755-756` · **ต้องทำ:** เพิ่ม UI ครบทั้ง 4 (หมวดหมู่เป็น select จาก tree จริง, outcomes/requirements เป็น list เพิ่ม-ลบ-เรียงได้, SEO เป็นช่องกรอกแยกพร้อม fallback ที่บอกผู้ใช้ชัดว่ากำลัง fallback), **ลบค่า seed hardcode ทิ้งทั้งหมด**, validate ว่าต้องเลือกหมวดหมู่ก่อนถึง submit ได้ · ข้อความทุกจุดเข้า i18n · **ห้ามส่งค่า default ที่ผู้ใช้ไม่ได้เลือกขึ้น API เด็ดขาด** | M |
| H2 | `P7-13` → `P7-14` | **ฟอร์ม "ติดต่อเรา" เป็นของปลอมทั้งอัน** — `features/legal/contact-page/contact-page.ts:39` มีคอมเมนต์ `// Simulate contact message dispatch` แล้ว `setTimeout(…, 600)` → ขึ้น toast "ข้อความของคุณถูกส่งเรียบร้อยแล้ว ทีมงานจะติดต่อกลับโดยเร็วที่สุด" ทั้งที่ไม่ส่งไปไหนเลย · backend ไม่มี contact endpoint สักตัว · **✅ ตัดสินแล้ว (D-18 Q-A): ทำ endpoint จริง** — BE (`P7-13`): ตาราง `notify.ContactMessages` + `POST /api/contact` สาธารณะ + rate limit policy `default` + honeypot + จำกัดความยาว + แจ้งทีมผ่าน `notify.EmailOutbox` (ห้ามส่ง SMTP ตรงใน request) + endpoint admin list/mark-resolved ใต้ `AdminOnly` · **ห้าม log เนื้อความหรืออีเมลผู้ส่ง** · FE (`P7-14`): เรียก API จริง + loading/error/success state จริง (**ห้ามขึ้น toast สำเร็จถ้า request ยังไม่สำเร็จ**) + หน้าแอดมินอ่านข้อความ + i18n | M |

### Phase I — ทางเข้าที่หายไป (ผู้ใช้ทำงานไม่จบ)

| # | Task | ทำอะไร | ขนาด |
|---|------|--------|------|
| I1 | `P1-28` | **ตะกร้าเข้าไม่ถึงจากเมนูหลักเลย** — หน้า `/cart` มีจริง `addToCart()` ทำงานจริง แต่ `site-header.html` ไม่มีไอคอนตะกร้าและไม่มี badge จำนวน (มีแค่ blog/learning-paths/my-courses/orders/instructor/devices) → ผู้ใช้กด "เพิ่มลงตะกร้า" แล้วหาทางกลับไม่เจอ · เพิ่มไอคอน + badge จาก `CartStore` ทั้ง desktop และ drawer มือถือ · **งานนี้ทำเฉพาะตะกร้า** — ไอคอนหัวใจ (wishlist) เจ้าของตัดสินแล้วว่าทำใน v1 แต่แยกเป็น `P1-12`/`P1-29` ที่ Phase K (ต้องมี API ก่อน) อย่าใส่หัวใจที่ยังกดไม่ได้จริงมาในงานนี้ | S |
| I2 | `P2-24` | **curriculum เข้าไม่ถึงเลยบนมือถือ** — `learn-page.html:352` เป็น `<aside class="w-80 shrink-0 hidden lg:block">` ซ่อนตัวเองต่ำกว่า `lg` โดยไม่มี fallback ใด ๆ และทั้งหน้าเป็น `h-screen overflow-hidden` → บนมือถือมองไม่เห็นรายการบทเรียนและสลับตอนไม่ได้เลย ขัด acceptance ข้อ "responsive มือถือ" ของ task นี้ตรง ๆ · ทำเป็น drawer หรือเพิ่มแท็บ "บทเรียน" ในแถบแท็บที่มีอยู่แล้ว (ตอนนี้มี 5 แท็บ: overview/quiz/assignment/resources/qa) · **แตะเฉพาะ layout ห้ามแตะ `video-player` component** (อยู่นอกขอบเขตรอบนี้) | M |
| I3 | `P1-26a` | **หน้า course detail ไม่แสดงรีวิวเลย** ทั้งที่ `GetCourseReviews` (BE) และ `CourseApiService.getCourseReviews()` (FE) พร้อมทั้งคู่ — หน้านี้ไม่เคยเรียก method นั้น · แสดงรายการรีวิว + แท่งกระจายดาว 5/4/3/1-2 (ต้องเพิ่ม bucket count ที่ endpoint เพราะตอนนี้คืนแค่ rating รายรายการ) + paginate · i18n ครบ | M |

### Phase J — ปุ่มที่กดแล้วไม่เกิดอะไร (ต่อสายไม่ครบ · แก้เร็ว ผลชัด)

| # | Task | ทำอะไร | ขนาด |
|---|------|--------|------|
| J1 | `P4-26` | หน้ารายได้ผู้สอน — `instructor-earnings-page.ts:113` `saveBankAccount()` **ไม่เรียก API เลย** (`PayoutApiService.upsertPayoutAccount` มีอยู่แต่ไม่ถูกใช้) → แก้เลขบัญชีแล้วไม่ถูกเซฟ · และ `error: () => {}` ที่บรรทัด 98/109 กลืน error เงียบ ๆ ทำให้ API ล้มเหลวแล้วยังเห็นตัวเลขเก่าค้าง · ต่อ API จริง + error state จริง + เลขบัญชีต้อง mask เสมอ | S |
| J2 | `P6-20` | CMS — (1) `admin-banners-page.ts:73` `toggleActive()` แก้แค่ signal ในเครื่อง ไม่เคยเรียก `updateBanner` → สลับสถานะแล้วรีเฟรชย้อนกลับ (2) **ยังไม่มี nav menu editor เลย** (grep `NavMenu`/`menuItem` ในโมดูล admin = 0 hit) ทั้งที่ `P6-01` มี menu API ฝั่ง BE แล้ว | M |
| J3 | `P4-27` | หน้าประกาศผู้สอน — `instructor-announcements-page.ts:51-54` dropdown เลือกคอร์สเป็น array hardcode 2 รายการ ไม่ได้ดึงคอร์สจริงของผู้สอนที่ login → ผู้สอนที่มีคอร์สอื่นส่งประกาศไม่ได้เลย · ดึงจาก `GET /api/catalog/instructor/courses` ที่มีอยู่แล้ว | S |
| J4 | `P3-21` | checkout — `checkout-page.ts:167-193` seed order ปลอมขึ้นมาเองเมื่อเข้า `/checkout` โดยไม่มี `orderId` (คอมเมนต์เขียนเองว่า "mock or fallback order") → ผู้ใช้เห็นหน้าจ่ายเงินของ order ที่ไม่มีจริง · เปลี่ยนเป็น redirect ไป `/cart` หรือขึ้น error state ที่บอกทางไปต่อ | S |

### Phase K — ฟีเจอร์ที่ยังไม่มีเลย (เรียงตามผลเชิงธุรกิจ)

| # | Task | ทำอะไร | ขนาด |
|---|------|--------|------|
| K1 | `P6-28` | **flash sale ผู้เรียนไม่เห็นเลย** — domain/pricing engine (`P6-02`) และหน้าจัดการฝั่งแอดมิน (`P6-21`) พร้อมหมด แต่ grep `flashSale`/`salePrice` ใน `features/catalog` + `features/home` = 0 hit → แคมเปญที่ตั้งไว้ไม่มีใครเห็น **รายได้หายตรง ๆ** · ทำ: แบนเนอร์แคมเปญ + นับถอยหลังบนหน้าแรก, ราคาลดบนการ์ดคอร์สใน catalog, ป้าย "ราคาโปรโมชันเหลืออีก N วัน" บนหน้าคอร์ส · **ราคาที่แสดงต้องมาจาก server (`IPricingEngine`) เท่านั้น ห้าม FE คำนวณส่วนลดเอง** (CLAUDE.md ข้อ 4) · ยังไม่มี countdown component ในโค้ดเบสเลย ต้องสร้าง + รองรับ `prefers-reduced-motion` | M |
| K2 | `P1-11` → `P1-26b` | เติมฟิลด์ที่หน้า course detail ต้องใช้แต่ `GetCourseDetail` ยังไม่มี (BE ก่อน แล้วค่อย FE): instructor social proof (จำนวนคอร์ส + ผู้เรียนรวม), `HasCertificate`, `LastUpdatedAtUtc`, จำนวน free-preview + trailer, อันดับขายดีในหมวด (อ่าน `analytics.DailyCourseStats` **ผ่าน Contracts เท่านั้น**) → แล้ว FE ทำแถบป้าย, การ์ดดูตัวอย่าง, รายการการันตี, ป้ายส่วนลด `-N%`, หัวข้อสรุป "N เซกชัน · N บทเรียน · รวม N ชม.", ย้าย requirements ไป sidebar · ⚠️ ตัวเลขทุกตัวต้องเป็น aggregate จาก DB จริง **ห้าม hardcode** | M |
| K3 | `P6-09` → `P6-29` | "คอร์สที่มักซื้อร่วมกัน" — BE: คืนคอร์สที่ถูกซื้อในออร์เดอร์เดียวกันบ่อยสุด (อ่าน `ORDER_ITEMS` ผ่าน Contracts) + ราคาชุดถ้ามี bundle คลุมอยู่ (**ราคาต้องมาจาก `IPricingEngine` ตัวเดียวกับตอน checkout ห้ามคำนวณซ้ำ**) + fallback เป็นคอร์สหมวดเดียวกันเมื่อข้อมูลยังน้อย · FE: sidebar บนหน้าคอร์ส + ปุ่มใส่ตะกร้าทั้งชุด | M |
| K4 | `P6-06` | **admin ถอนคอร์สที่เผยแพร่แล้วไม่ได้เลย** — ต้องแก้ DB ตรง · `CatalogModule.cs:236,249` มีแต่ admin group ของ category กับ instructor application · `UpdateCourse/Handler.cs:21` และ `DeleteCourse/Handler.cs:16` เขียนกำกับไว้เองว่าไม่มี admin bypass เพราะ "course moderation เป็นของ P6-06" · ทำ endpoint unpublish/suspend/takedown + เหตุผล + audit + แจ้งผู้สอน · **feature flag ไม่ต้องทำแล้ว มีอยู่ใน Cms แล้ว** | M |
| K5 | `P6-22` | หน้ารายงานการเงินแอดมิน — คิว refund และ payout batch ต่อ API จริงแล้ว แต่**ยังไม่มี UI รายงาน revenue split ต่อ instructor** ที่ mockup ระบุไว้ (`admin-payouts-page.html` ไม่มีคำว่า revenue/split เลย) · BE `P6-03` พร้อมแล้ว | M |
| K6 | `P6-27` | หน้าผู้ใช้และสิทธิ์ — role change/suspend/reactivate/audit-log ต่อ API จริงครบแล้ว แต่**ไม่มีปุ่ม "เชิญผู้ใช้"** (grep `invite` ในโมดูลนี้ = 0 hit) · ⚠️ การเชิญผู้ใช้แตะ Identity + การออกสิทธิ์ → **ถ้ากลายเป็นงานสร้างบัญชี/ออก token ให้หยุดแล้วรายงาน** (ดู §2 ย่อหน้าการแบ่งงาน) | M |
| K7 | `P2-26` | การ์ด "เรียนต่อ" บนหน้าแรก (ตอนนี้ไม่มีเลย) — thumbnail + ชื่อคอร์ส + **ตอนถัดไป + เวลาที่เหลือของตอนนั้น** + progress bar + ปุ่มเล่นที่ deep-link ไปตอนและตำแหน่งล่าสุดจริง · ปุ่ม "เรียนต่อ" ที่มีใน `my-courses-page.html` ตอนนี้ชี้แค่ `/learn/:slug` ไม่พาไปตอนที่ค้าง · ข้อมูล resume มีอยู่แล้วจาก `P2-07`/`P2-23` — **ใช้ข้อมูลที่มี ห้ามแตะ player component** | M |
| K8 | `P3-26` | checkout บนมือถือ — (1) stepper 3 ขั้น (grep `stepper` ใน `features/commerce` = 0 hit) (2) แถบสรุปยอด + ปุ่มจ่าย sticky ที่ขอบล่างจอมือถือ (ตอนนี้เป็น `sticky top-24` ในคอลัมน์ `lg:col-span-5` พอจอแคบจะไหลลงใต้ fold) · **ไม่ต้องแตะเรื่องวิธีจ่าย** — บัตร/ผ่อน 0% ถูก render เป็น disabled "เร็ว ๆ นี้" ถูกต้องตาม D-14/D-17 อยู่แล้ว | S |
| K9 | `P1-27` | แถบแท็บล่างมือถือ 4 แท็บ ตาม `ui-design.md` (≤5 รายการ, safe area iOS, touch target ≥44px) — ตอนนี้ไม่มีเลย (`MobileNav` ที่มีคือ drawer คนละอย่าง) · **✅ ตัดสินแล้ว (D-18 Q-C): ชี้ไปหน้าที่มีอยู่ ไม่ต้องสร้างหน้าใหม่** → หน้าแรก `/` · ค้นหา `/courses` · เรียน `/my-courses` · ฉัน `/account/orders` · ซ่อนบนจอ `lg` ขึ้นไป · active state ตาม route ปัจจุบัน | M |
| K10 | `P1-12` → `P1-29` | **รายการโปรด (wishlist)** — ✅ เจ้าของตัดสินแล้วว่าทำใน v1 (D-18 Q-B) · BE: ตาราง `catalog.Wishlists` + `UQ(UserId, CourseId)` + `GET/POST/DELETE /api/catalog/wishlist` · **ทุก query กรองด้วย `IUserContext.UserId` เท่านั้น ห้ามรับ userId จาก request** · เพิ่ม `isWishlisted` เข้า `GetCourseDetail`/`SearchCourses` แบบ join ครั้งเดียว **ห้ามเกิด N+1** · FE: หัวใจบนการ์ดคอร์ส/หน้าคอร์ส/header + หน้า `/wishlist` · ยังไม่ล็อกอินกดหัวใจ → พาไป login แล้วกลับที่เดิม (**ห้ามเก็บลง localStorage แล้ว sync ทีหลัง** — จะได้ state สองแหล่งที่ขัดกัน) · optimistic update ที่ rollback เมื่อ fail | M |
| K11 | `P8-06` | Learning Path — หน้า list/detail ต่อ API จริงและได้ SSR แล้ว · ที่ขาด: **JSON-LD** (grep `jsonLd`/`schema` ในสองหน้านี้ = 0 hit ทั้งที่ `SeoService` รองรับอยู่แล้ว) และ **สถานะ "คอร์สถัดไปที่ควรเรียน"** | S |

### Phase L — คุณภาพ / DoD ที่ค้าง

| # | Task | ทำอะไร | ขนาด |
|---|------|--------|------|
| L1 | `X-14` | **14 หน้าที่ดึงข้อมูลแต่ไม่มี error state** → API ล่ม = หน้าว่างเปล่าไม่มีปุ่มลองใหม่ (ขัด `ui-design.md` ตรง ๆ) · หนักสุด ไม่มีทั้ง loading/error/empty: `admin/banners`, `admin/posts`, `instructor/dashboard` (มี `isLoading` ใน .ts แต่เทมเพลตไม่เคยใช้) · ขาด error branch: `admin/users`, `admin/marketing`, `admin/payouts`, `admin/course-moderation`, `admin/menus` · ใช้ pattern เดียวกับ `courses-page` ที่ทำถูกอยู่แล้ว (guard `hasValue()` + error state + ปุ่ม retry) | M |
| L2 | `X-4` | **กวาด i18n** — วัดใหม่ 2026-08-28 แย่กว่าที่เคยบันทึก: `admin` **0/9**, `legal` **0/5**, `blog` **0/2**, `instructor` **1/6**, `catalog` **2/5**, `commerce` **2/4**, `learning` **5/6** → 25 จาก 38 เทมเพลตสลับภาษาไม่ได้ · หนักสุด: `admin/payouts` 73 บรรทัด, `admin/payments` 72, `admin/marketing` 72, `admin/users` 56, `admin/dashboard` 45, `instructor/onboarding` 39 · ทำทีละโฟลเดอร์ 1 commit ต่อโฟลเดอร์ | L |
| L3 | `X-16` | ผิดกฎ `frontend.md`: `NgOptimizedImage` ไม่ถูกใช้ 14 จุด (กระทบ LCP จริงบน catalog/cart/home — **ทำก่อน**) · `@Input()`/`@Output()` decorator เก่า 25 จุดใน 6 component · ขาด `OnPush` 4/49 · `*ngIf` เหลือ 3 จุด (`admin/payments`) · ✅ ที่สะอาดอยู่แล้ว: `@for` มี `track` ครบ, ไม่มี `standalone: false`, **ไม่มี `any` เลย** | M |
| L4 | `X-7` | search fallback — `SearchCourses/Handler.cs:72` ใช้ `catch (Exception)` คลุมทุกชนิด (รวม timeout/cancellation) ทำให้ DB ล่มชั่วคราวกลายเป็นผลค้นหาผิดแบบเงียบ ๆ · ไม่มี `ILogger` ใน handler เลย · ยิง FTS ให้พังใหม่ทุก request แทนที่จะ cache ผลตรวจความสามารถ · fallback ยัง materialize ทุกแถวที่ LIKE ตรงเข้า memory | S |
| L5 | `P1-08` | เก็บงานค้างของรีวิว: (1) `CreateCourseReviewHandler.cs:76,89` มี `SaveChangesAsync` สองครั้ง **คนละ transaction** → ถ้าตัวที่สองล้ม rating จะ drift ถาวร (2) **ไม่มี job reconcile รายคืน** (`RecurringJobsRegistration.cs` มี 7 job ไม่มีของ rating) (3) logic recompute ฝังใน handler ไม่ได้แยกเป็น `CourseStatsUpdater` ตามที่ acceptance เขียน — ตอนนี้ single-writer เป็นแค่ธรรมเนียม ไม่ใช่โครงสร้าง | M |
| L6 | `X-13` | (1) **ลบ doc comment ที่โกหก** — `Cms/Domain/POST.cs:56` และ `Infrastructure/PostConfiguration.cs:34` ยังเขียนว่า "Not implemented in this scaffold pass — this field is NOT safe to store or render as-is yet" ทั้งที่ `PostService.cs:18,46` เรียก `HtmlSanitizerHelper.Sanitize()` จริงแล้ว (agent ที่ตรวจรอบนี้เกือบรายงานเป็นช่องโหว่วิกฤตเพราะเชื่อคอมเมนต์นี้) (2) เพิ่ม sanitize **ตอน render** ด้วย — `PostService.ToResponse` คืน `CONTENT_HTML` ดิบ ขณะที่ `security.md` บังคับว่าต้องทำทั้งก่อนเก็บและก่อนแสดง (ความเสี่ยงจริงต่ำเพราะ FE ใช้ `[innerHTML]` ที่ Angular sanitize ให้ และไม่มี `bypassSecurityTrustHtml` ที่ไหนเลย แต่ยังไม่ตรงกฎ) | S |
| L7 | `X-6` | `Siri.Modules.Analytics` ผสม 2 pattern ในโมดูลเดียว (`Application/I*Repository` + `Infrastructure/*Repository` **และ** `Features/AdminDashboardSummary/`, `Features/InstructorAnalytics/`) — `backend.md` ห้ามผสม · เลือกทางเดียวแล้วบันทึกเหตุผล | S |
| L8 | `P5-22` | หน้า verify ใบรับรอง **ไม่มีปุ่มดาวน์โหลด PDF** (มีแค่คัดลอกลิงก์ + LinkedIn) — ปุ่มในหน้า my-courses มีแล้วจริง · + i18n 5 จุดที่ยัง hardcode (`certificate-verify-page.html:35,123,128,140,150,157`) | S |
| L9 | `P6-04` | `PayerAddress` ยังเป็น placeholder `"Bangkok, Thailand"` ที่ guard ไม่ได้ตรวจ (ตรวจแค่ชื่อบริษัท + เลขผู้เสียภาษี) → production บูตผ่านแล้วออกหนังสือรับรองหัก ณ ที่จ่ายพร้อมที่อยู่ปลอมได้ · เพิ่มเข้า `PayoutOptionsGuard` | S |
| L10 | `P1-31` `P1-32` `P5-30` `P4-31` | e2e + performance budget ที่ไม่ต้องพึ่งของนอกระบบ: Lighthouse CI + bundle budget (initial ≤ 300KB gz) · หน้าแรก→หมวด→คอร์ส · เรียนจบ→certificate→verify · สร้างคอร์สจนขึ้นขาย | L |

**ลำดับที่แนะนำ:** H → I → J → K → L
· **H1 ทำก่อนสุด** เพราะข้อมูลปลอมกำลังไหลลง DB ทุกครั้งที่มีคนสร้างคอร์ส
· J ทั้งเฟสเป็นงาน S เกือบหมด ทำรวดเดียวได้ผลเร็ว
· L2 (i18n) ใหญ่แต่แตกเป็น commit ย่อยต่อโฟลเดอร์ได้ แทรกระหว่างเฟสอื่นได้
· **ห้ามเริ่ม H2 / I1(ส่วนหัวใจ) / K9 ก่อนได้คำตอบใน §4.2**

---

## 5. Template คำสั่งต่อ task

```
Task: {TASK_ID} — {ชื่อ task}

อ่านก่อน (เรียงตามนี้):
0. docs/contracts/{TASK_ID}-*.md ถ้ามี — **contract คือข้อผูกพัน ชนะบล็อกนี้ถ้าขัดกัน** เริ่มได้เมื่อ Status: FROZEN
1. CLAUDE.md — กฎโปรเจ็ค
2. docs/TASKS.md — แถวของ {TASK_ID} (acceptance = definition of done) + § "สถานะจริงของงานที่ยังไม่ปิด"
   ซึ่งระบุว่า task นี้ขาดอะไรอยู่บ้างแล้ว
3. docs/ANTIGRAVITY_HANDOFF.md §2 (กฎ) + §3 (DoD) — ไฟล์นี้
4. .claude/rules/{backend|frontend}.md — เช็คก่อนว่าโมดูลที่จะแตะเป็น vertical slice (Identity/Catalog/
   Notification) หรือ Repository+Service (Commerce/Media/Learning/Payout/Cms/Community/Analytics)
5. .claude/rules/database.md — ถ้าแตะ schema (+ ส่วน UPPERCASE ถ้าอยู่ใน 7 โมดูลใหม่)
6. .claude/rules/security.md — ถ้าแตะ เงิน / สิทธิ์เรียน / playback / ข้อมูลส่วนบุคคล
7. งาน FE: frontend/design-system/siri-upskill/mockups-2026-08-28/ (ไฟล์ล่าสุด 18 หน้าจอ)
   + .claude/rules/ui-design.md

สถานะปัจจุบันของโค้ดที่เกี่ยวข้อง:
{คัดจาก § "สถานะจริง" ของ TASKS.md — ห้ามเขียนว่า "ยังเป็น scaffold" ถ้าไม่ได้เช็คไฟล์จริง}

สิ่งที่ต้องทำ:
{acceptance ของ task นั้นจาก TASKS.md ตรง ๆ}

Definition of done: ตาม §3 ของ docs/ANTIGRAVITY_HANDOFF.md ทุกข้อ รวม integration test
นอกขอบเขต: {ระบุเจาะจง — อย่าใช้คำว่า "ห้ามแตะโมดูล X" ถ้า task ต้องแตะจริง ให้ระบุว่าแตะได้แค่ส่วนไหน}
```

---

## 6. Mapping หน้าจอ mockup → module/schema

mockup ล่าสุด: `frontend/design-system/siri-upskill/mockups-2026-08-28/SIRI UpSkill Mockups.dc.html` (**18 หน้าจอ**) — เทียบกับ 2026-08-21 แล้ว 16 หน้าเดิมและ design token ทุกไฟล์เหมือนเดิมทุกประการ เพิ่มมา 2 หน้า: **รายละเอียดคอร์ส** และ **มือถือ** (3 จอในกรอบ iOS) · ไฟล์ `ios-frame.jsx` เป็นกรอบเครื่องสำหรับ mockup เท่านั้น ไม่ต้อง implement

> ⚠️ คอลัมน์สถานะข้างล่าง **แก้ไข 2026-08-28** จากการอ่านโค้ดจริงทีละไฟล์ — ของเดิม (2026-08-24) บอกว่า 6 หน้าเป็น "mock ล้วน" ซึ่งไม่จริงอีกต่อไปแล้ว (ต่อ API จริงหมดแล้ว) ดู `TASKS.md` § X-10/X-11

| # | หน้าจอ mockup | Backend scope | Schema | สถานะ 2026-08-28 |
|---|---|---|---|---|
| 1 | ค้นหา & หมวดหมู่ | Catalog | `catalog` | ✅ เสร็จ (เหลือ P1-25 search UI) |
| 2 | ชำระเงิน (Checkout) | Commerce | `COMMERCE` | `PART` — seed order ปลอมเมื่อไม่มี orderId + ไม่มี stepper/sticky bar มือถือ (P3-21/P3-26) |
| 3 | ห้องเรียน (Classroom) | Media + Learning + Community | `MEDIA`,`LEARNING`,`COMMUNITY` | `PART` — 🔴 วิดีโอไม่เล่นตอนเปิดครั้งแรก (P2-20) + curriculum หายบนมือถือ (P2-24) |
| 4 | ความคืบหน้า & ใบรับรอง | Learning | `LEARNING` | ✅ เสร็จ |
| 5 | เส้นทางการเรียน | Catalog (LearningPaths) | `catalog` | BE ✅ (P8-05) · FE `PART` — มีหน้าแล้วแต่ไม่มี JSON-LD + ไม่มีสถานะคอร์สถัดไป (P8-06) |
| 6 | สร้างคอร์ส (ผู้สอน) | Catalog + Media | `catalog`,`MEDIA` | ✅ เสร็จ — บันทึกได้จริง + drag/keyboard reorder + undo ครบ (P4-01/P4-20) |
| 7 | วิเคราะห์การขาย (ผู้สอน) | Analytics/Payout | `analytics` | ✅ เสร็จ — ต่อ API จริงแล้ว (โน้ต "mock ล้วน" เดิมล้าสมัย) |
| 8 | จัดการเนื้อหา CMS (แอดมิน) | Cms | `CMS` | `PART` — toggle banner ไม่เซฟจริง + ยังไม่มี menu editor (P6-20) |
| 9 | โปรโมชัน (แอดมิน) | Commerce | `COMMERCE` | ✅ เสร็จ — ต่อ API จริงแล้ว (P6-21) |
| 10 | รายงานการเงิน (แอดมิน) | Payout | `PAYOUT` | `PART` — คิว refund ต่อจริงแล้ว · ยังไม่มีรายงาน revenue split (P6-22) |
| 11 | ชำระเงินสำเร็จ | Commerce | `COMMERCE` | ✅ เสร็จ — ต่อ API จริงแล้ว (P3-25) |
| 12 | รายได้ของฉัน (ผู้สอน) | Payout | `PAYOUT` | `PART` — ปุ่มบันทึกบัญชีรับเงินไม่เรียก API (P4-26) |
| 13 | ประกาศถึงผู้เรียน | Notification | `notify` | `PART` — dropdown เลือกคอร์ส hardcode 2 รายการ (P4-27) |
| 14 | แดชบอร์ดแอดมิน | Analytics + Contracts ข้ามโมดูล | หลายโมดูล | ✅ เสร็จทั้ง BE/FE (P6-07/P6-26) |
| 15 | อนุมัติคอร์ส (แอดมิน) | Catalog (P1-05) | `catalog` | ✅ เสร็จทั้ง BE/FE (P6-23) |
| 16 | ผู้ใช้และสิทธิ์ (แอดมิน) | Identity | `identity` | `PART` — ไม่มีปุ่มเชิญผู้ใช้ (P6-27) · BE ขาด course moderation/feature flag (P6-06) |
| **17** | **รายละเอียดคอร์ส** *(ใหม่ 2026-08-28)* | Catalog (+Commerce, Analytics ผ่าน Contracts) | `catalog` | `PART` — ขาดรีวิวจริง/การ์ดตัวอย่าง/ป้าย/การันตี/cross-sell (P1-11, P1-26, P6-09, P6-29) |
| **18** | **มือถือ (3 จอ)** *(ใหม่ 2026-08-28)* | — (FE ล้วน) | — | `TODO` — ไม่มี bottom tab bar, ไม่มีการ์ดเรียนต่อ, ไม่มี flash sale ฝั่งผู้เรียน, ไม่มี stepper (P1-27, P2-26, P3-26, P6-28) · AI assistant = เลื่อนไป Phase 2 โดยตั้งใจ |

---

## 7. คำสั่งพร้อมใช้ — Phase H / I / J (อัปเดต 2026-08-28 · แทนบล็อก Phase A/B/C ฉบับ 2026-08-27 ทั้งหมด)

> บล็อก A1–C5 ฉบับเดิมถูกลบทิ้งแล้วเพราะงานทั้งหมดนั้นปิดไปแล้ว (ดู §4.1) · คัดลอกบล็อกข้างล่างไปวางให้ Antigravity ได้ตรง ๆ ทีละอัน

### H1 — `P4-22` หน้า builder หยุดส่งค่าปลอมขึ้น API (M) 🔴 ทำก่อนทุกอย่าง

```
Task: P4-22 — Builder UI: ตั้งค่าคอร์สให้ครบ และหยุดส่งค่าปลอมขึ้น API

อ่านก่อน: CLAUDE.md · docs/TASKS.md แถว P4-22 + § "สถานะจริง" หัวข้อ P4-22 ·
docs/ANTIGRAVITY_HANDOFF.md §2 §3 · .claude/rules/frontend.md · .claude/rules/ui-design.md ·
frontend/design-system/siri-upskill/mockups-2026-08-28/ (18 หน้าจอ)

ปัญหาจริงที่ตรวจพบ 2026-08-28 (อ่านโค้ดจริง ไม่ใช่รายงาน):
ไฟล์ frontend/src/app/features/instructor/course-builder/course-builder-page.{ts,html}
1. ไม่มี <select> หมวดหมู่ในเทมเพลตเลย — <select> ตัวเดียวที่มีคือ "ระดับ" ที่ .html:272
   แต่ .ts:219 เซ็ต categoryId = flat[0].id เงียบ ๆ แล้วส่งขึ้น API ที่ .ts:749
   → ทุกคอร์สที่ผู้สอนสร้างถูกยัดเข้าหมวดหมู่แรกสุดของ tree เสมอ ไม่ว่าเนื้อหาเป็นเรื่องอะไร
2. outcomes / requirements ไม่มี UI เลย (grep "outcome|requirement" ใน .html = 0 hit)
   แต่ .ts:170-175 seed ข้อความไทย hardcode ("เข้าใจพื้นฐานและโครงสร้าง",
   "สามารถนำไปประยุกต์ใช้งานจริง", "คอมพิวเตอร์และอินเทอร์เน็ต") แล้วส่งขึ้น API ที่ .ts:758-759
   → ข้อความ placeholder ไปโผล่เป็น "สิ่งที่คุณจะได้เรียน" จริงบนหน้าคอร์สสาธารณะ
3. seoTitle/seoDescription ไม่มี UI — .ts:755-756 mirror จาก title/subtitle เงียบ ๆ

สิ่งที่ต้องทำ:
- หมวดหมู่: <select> จาก category tree จริง (ดึงอยู่แล้วที่ .ts:202) + validate ว่าต้องเลือกก่อน
  submit ได้ + แสดง path เต็มถ้าเป็นหมวดย่อย
- outcomes / requirements: list เพิ่ม-ลบ-เรียงลำดับได้ (ใช้ CDK drag-drop ที่หน้านี้มีอยู่แล้ว) +
  เริ่มต้นเป็น list ว่าง ไม่ใช่ค่า seed
- SEO: ช่องกรอก seoTitle / seoDescription แยก + ถ้าผู้ใช้เว้นว่างให้ fallback จาก title/subtitle
  แต่ต้องบอกผู้ใช้บนหน้าจอชัดว่ากำลัง fallback (placeholder หรือ helper text)
- ลบค่า seed hardcode ที่ .ts:170-175 ทิ้งทั้งหมด
- ข้อความ user-facing ทุกจุดเข้า i18n (th.json + en.json) — หน้านี้ใช้ i18n อยู่แล้ว 63 key ทำตาม pattern เดิม
- unit test: ยืนยันว่า payload ที่ส่งขึ้น API ไม่มีค่า default ที่ผู้ใช้ไม่ได้เลือก และ submit ไม่ผ่าน
  ถ้ายังไม่เลือกหมวดหมู่

ข้อห้ามเฉพาะงานนี้:
- ห้ามส่งค่า default ที่ผู้ใช้ไม่ได้เลือกขึ้น API เด็ดขาด — ถ้าฟิลด์ไหน backend บังคับแต่ UI ยังไม่มี
  ให้หยุดแล้วรายงาน อย่า seed ค่าปลอมแทน
- ห้ามแตะ tab "curriculum" และ logic autosave/undo/reorder ที่ทำงานถูกอยู่แล้ว

Definition of done: ตาม §3 ทุกข้อ · นอกขอบเขต: backend (P4-01 ปิดแล้ว รับฟิลด์เหล่านี้ได้อยู่แล้ว)
```

### I1 — `P1-28` ไอคอนตะกร้าบน header (S)

```
Task: P1-28 — ไอคอนตะกร้า + badge จำนวนบน header

อ่านก่อน: CLAUDE.md · docs/TASKS.md แถว P1-28 · docs/ANTIGRAVITY_HANDOFF.md §2 §3 ·
.claude/rules/frontend.md · .claude/rules/ui-design.md ·
frontend/design-system/siri-upskill/mockups-2026-08-28/ (header ทุกหน้าจอ)

ปัญหาจริง: หน้า /cart มีจริง, addToCart() ทำงานจริง, buyNow() navigate ไป /cart จริง
แต่ frontend/src/app/layouts/public-layout/site-header/site-header.html ไม่มีไอคอนตะกร้าเลย
(มีแค่ลิงก์ blog / learning-paths / my-courses / account/orders / instructor / account/devices)
→ ผู้ใช้ที่กด "เพิ่มลงตะกร้า" ไม่มีทางกลับไปที่ตะกร้าจากเมนูหลัก และไม่เห็นจำนวนของในตะกร้า

สิ่งที่ต้องทำ:
- ไอคอนตะกร้า (Phosphor shopping-cart-simple ตาม mockup) + badge จำนวนจาก CartStore
  ทั้งบน desktop header และใน MobileNav drawer
- badge ต้อง reactive กับ CartStore จริง (signal) ไม่ใช่ค่าคงที่ · ซ่อน badge เมื่อเป็น 0
- aria-label + touch target ≥44px ตาม ui-design.md · ข้อความเข้า i18n
- unit test: เพิ่มของลงตะกร้าแล้ว badge เปลี่ยนจริง

⛔ อย่าเพิ่งทำไอคอนหัวใจ (รายการโปรด) ที่เห็นใน mockup — ยังไม่มีทั้งตาราง/API/UI และเจ้าของ
โปรเจ็คยังไม่ตัดสินว่าทำใน v1 ไหม (§4.2 Q-B) ถ้าจะทำต้องถามก่อน

Definition of done: ตาม §3 ทุกข้อ (ฝั่ง FE: lint + test + build เขียว)
```

### I2 — `P2-24` curriculum บนมือถือ (M)

```
Task: P2-24 — ทำให้ curriculum เข้าถึงได้บนมือถือในหน้า /learn

อ่านก่อน: CLAUDE.md · docs/TASKS.md แถว P2-24 + § "สถานะจริง" หัวข้อ P2-24 ·
docs/ANTIGRAVITY_HANDOFF.md §2 §3 · .claude/rules/frontend.md · .claude/rules/ui-design.md
(ตาราง "หน้าจอที่ต้องพิถีพิถัน" แถว Video player) ·
frontend/design-system/siri-upskill/mockups-2026-08-28/ (หน้าจอ "มือถือ" จอที่ 2)

ปัญหาจริง: frontend/src/app/features/learning/learn-page/learn-page.html:352 เป็น
<aside class="w-80 shrink-0 hidden lg:block h-full"> — ซ่อนตัวเองต่ำกว่า breakpoint lg
โดยไม่มี fallback ใด ๆ (ไม่มี drawer ไม่มีแท็บ) และทั้งหน้าเป็น h-screen overflow-hidden
→ ผู้เรียนบนมือถือมองไม่เห็นรายการบทเรียนและสลับตอนไม่ได้เลย
ขัด acceptance ข้อ "responsive มือถือ" ของ task นี้ตรง ๆ

สิ่งที่ต้องทำ (เลือกทางใดทางหนึ่ง แล้วอธิบายเหตุผลสั้น ๆ ในคอมเมนต์):
ก) เพิ่มแท็บ "บทเรียน" เข้าไปในแถบแท็บที่มีอยู่แล้ว (ตอนนี้มี 5 แท็บที่ .html:148-223:
   overview / quiz / assignment / resources / qa) แสดงเฉพาะจอเล็ก
ข) ทำเป็น drawer เลื่อนออกมา — mirror pattern CdkTrapFocus + scroll-lock + Escape ของ
   MobileNav ที่มีอยู่แล้ว (อย่าแก้ MobileNav เดิม สร้างของตัวเอง)
ต้องมี: สถานะดูแล้ว/กำลังดู/ควิซ ครบเหมือน sidebar เดสก์ท็อป · touch target ≥44px ·
กดเลือกตอนแล้วปิดเองแล้วเล่นตอนนั้น · ข้อความเข้า i18n · unit test คลุมการเปิด/ปิด/เลือกตอน

ข้อห้ามเฉพาะงานนี้ (สำคัญ):
- ห้ามแตะ features/learning/video-player/** — เรื่อง player/DRM ถูกพักไว้รอบนี้ตามคำสั่งเจ้าของโปรเจ็ค
  งานนี้เป็นบั๊ก layout ล้วน ๆ
- ถ้าพบว่าต้องแก้ player เพื่อให้งานนี้จบ ให้หยุดแล้วรายงาน

Definition of done: ตาม §3 ทุกข้อ
```

### J1–J4 — ปุ่มที่กดแล้วไม่เกิดอะไร (S ทั้งชุด · 4 commit แยกกัน)

```
Task: J-series — ต่อสายปุ่มที่กดแล้วไม่เกิดอะไร (ทำทีละตัว 1 task = 1 commit)

อ่านก่อน: CLAUDE.md · docs/TASKS.md แถวของแต่ละ task · docs/ANTIGRAVITY_HANDOFF.md §2 §3 ·
.claude/rules/frontend.md

J1 = P4-26 (หน้ารายได้ผู้สอน)
  features/instructor/earnings/instructor-earnings-page.ts:113 saveBankAccount() ไม่เรียก API เลย
  ทั้งที่ PayoutApiService.upsertPayoutAccount มีอยู่แล้วแต่ไม่ถูกใช้ → แก้เลขบัญชีแล้วไม่ถูกเซฟ
  และ error: () => {} ที่บรรทัด 98/109 กลืน error เงียบ ๆ → API ล้มเหลวแล้วยังเห็นตัวเลขเก่าค้าง
  ทำ: ต่อ upsertPayoutAccount จริง + error state จริง (ไม่กลืน) + เลขบัญชีต้อง mask เสมอทั้งตอน
  แสดงและตอนส่งกลับ + i18n + unit test ยืนยันว่ากดบันทึกแล้วยิง request จริง

J2 = P6-20 (CMS)
  (1) features/admin/banners/admin-banners-page.ts:73 toggleActive() แก้แค่ signal ในเครื่อง
      ไม่เคยเรียก updateBanner → สลับสถานะแล้วรีเฟรชย้อนกลับ · ต่อ API จริง + optimistic update
      ที่ rollback เมื่อ fail
  (2) ยังไม่มี nav menu editor เลย (grep "NavMenu|menuItem" ในโมดูล admin = 0 hit)
      ทั้งที่ P6-01 มี menu API ฝั่ง BE แล้ว — ทำหน้าจัดการเมนูนำทาง (เพิ่ม/ลบ/เรียงลำดับ/ซ้อนชั้น)
      ตาม mockup หน้า "จัดการเนื้อหา CMS" ส่วน "เมนูนำทาง"

J3 = P4-27 (ประกาศผู้สอน)
  features/instructor/announcements/instructor-announcements-page.ts:51-54 dropdown เลือกคอร์ส
  เป็น array hardcode 2 รายการ → ผู้สอนที่มีคอร์สอื่นส่งประกาศให้คอร์สตัวเองไม่ได้เลย
  ทำ: ดึงจาก GET /api/catalog/instructor/courses ที่มีอยู่แล้ว + loading/error/empty state + i18n

J4 = P3-21 (checkout)
  features/commerce/checkout-page/checkout-page.ts:167-193 seed order ปลอมขึ้นมาเองเมื่อเข้า
  /checkout โดยไม่มี orderId (คอมเมนต์เขียนเองว่า "mock or fallback order")
  → ผู้ใช้เห็นหน้าจ่ายเงินของ order ที่ไม่มีอยู่จริง
  ทำ: ลบ fallback ทิ้ง เปลี่ยนเป็น redirect ไป /cart หรือ error state ที่บอกทางไปต่อ + unit test

Definition of done: ตาม §3 ทุกข้อ ต่อ task · ห้ามรวมหลาย task เป็น commit เดียว
```

---

## 8. ประวัติการแก้แผน

| วันที่ | เปลี่ยนอะไร |
|-------|------------|
| 2026-08-28 | เขียน §4 ใหม่ทั้งหมด (Phase H–L) หลัง Claude Code ตรวจโค้ดจริงทีละไฟล์ — คิว Phase A–G เดิมล้าสมัยเกือบหมด · §7 แทนบล็อก A/B/C ด้วย H/I/J · §6 ขยาย mapping เป็น 18 หน้าจอตาม mockup v2026-08-28 · เจ้าของโปรเจ็คสั่งตัดวิดีโอ/player/DRM และ login/auth ออกจากรอบนี้ |
| 2026-08-27 | คิว Phase A–G (แทนคิว 2026-08-25) |
