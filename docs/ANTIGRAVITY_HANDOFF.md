# Antigravity task hand-off

คำสั่งสำหรับ Antigravity (coding agent อีกตัวที่เจ้าของโปรเจ็คใช้คู่กับ Claude Code)

> **แก้ทั้งไฟล์ 2026-08-24** — ฉบับก่อนหน้า (2026-08-20/21) บรรยายสถานะว่า "ทุกอย่างเป็น scaffold ที่ `throw NotImplementedException`" ซึ่ง**ไม่จริงอีกต่อไปแล้ว** ตั้งแต่ Antigravity ลงมือทำจริง ใครใช้ฉบับเก่าจะได้คำสั่งที่อ้างสถานะผิด

---

## 1. สถานะจริง ณ 2026-08-24 (ตรวจจากโค้ด ไม่ใช่จากรายงาน)

| เรื่อง | สถานะจริง |
|-------|-----------|
| `dotnet build backend/SiriUpSkill.sln` | ✅ เขียว 0 warning / 0 error |
| Unit test | ✅ **539/539** ผ่าน |
| Architecture test | ✅ **4/4** ผ่าน (module boundary ยังไม่หลุด) |
| Integration test ของ 7 โมดูลใหม่ | ❌ **0 ไฟล์** — `backend/tests/Siri.IntegrationTests/` มีแต่ Identity/Catalog |
| `NotImplementedException` ที่เหลือ | ❌ **ไม่เหลือแล้ว** (grep เจอแต่ใน doc comment ที่ยังเขียนค้างว่าเป็น stub — **ห้ามเชื่อ doc comment เหล่านั้น อ่าน method body จริงเสมอ**) |
| Git | ⚠️ ไม่มี commit ตั้งแต่ `40935d9` · ไฟล์ค้าง ~430 ไฟล์บน `feat/p2-p6-scaffold` → **CI ไม่เคยรันบนงาน P1–P6 เลยสักครั้ง** |
| Migration | ⚠️ ค้าง 4 ตัว และ `AddCourseFullTextIndex` บล็อกทั้งเส้น (error 7609 — ดู `P0-09`) |

**อ่านสถานะรายงานเสมอที่ `docs/TASKS.md`** — ตอนนี้มีคอลัมน์ `Status`/`Own` ทุกแถว บวก § "สถานะจริงของงานที่ยังไม่ปิด (audit 2026-08-24)" ที่ระบุว่าแต่ละ task ขาดอะไรพร้อมชื่อไฟล์

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

## 4. คิวงาน (เรียงตามผลกระทบจริง ไม่ใช่ตามเลขเฟส)

> เหตุผลที่ไม่เรียงตามเฟส: P2–P6 ถูก implement ไปแล้วแบบข้ามลำดับ สิ่งที่เหลือคือ "รูที่ทำให้ของที่สร้างไปแล้วยังใช้งานจริงไม่ได้" — ปิดรูพวกนี้ก่อนได้คุณค่ามากกว่าเริ่มเฟสใหม่

### Wave 0 — เจ้าของโปรเจ็คต้องทำก่อน (agent ทำแทนไม่ได้)
| # | งาน | ทำไมต้องก่อน |
|---|-----|--------------|
| `P0-39` | commit ของค้าง → push → CI เขียว | เป็นทางเดียวที่ integration test จะได้รันจริง (เครื่อง dev ไม่มี Docker) |
| `P0-09` | ปลด FTS blocker + apply migration ค้าง 4 ตัว | schema ของ P2–P6 ยังไม่มีอยู่บน DB จริงเลย |
| `Q4` | ตอบสูตร revenue split | P6-03 เขียนทับด้วยค่าที่เดาไว้แล้ว ยิ่งช้ายิ่งมีของสร้างทับ |
| `Q8` | ตัดสิน bar ของ anti-piracy | กำหนดว่า P2-05/P2-20/P2-31 จะมีหน้าตายังไง |

### Wave 1 — ปิดรูที่ทำให้ของที่มีอยู่ใช้งานไม่ได้ (Antigravity เริ่มที่นี่)
1. ~~**`P4-01`** Course builder API~~ ✅ **ปิดแล้ว 2026-08-25** — `DONE` หลัง Claude Code review (endpoint 10 ตัว, ownership ครบ, 409 จริง, integration test 12 ตัว)
   · เหลือรูเล็ก 2 จุดที่ review เจอ ให้เก็บพร้อม `P4-20`: (ก) `/instructor/courses/new` ยังขึ้น toast "บันทึกแล้ว" หลอก ๆ ด้วย `setTimeout` เพราะยังไม่มี `createCourse` ใน `CourseApiService` ทั้งที่ backend มี `POST /api/catalog/instructor/courses` มาตั้งแต่ P1-04 (ข) `AutosaveCourseValidator` ยังไม่จำกัดจำนวน section/episode ต่อ payload
2. **`P3-09`** Promo code validate/apply/redeem + นับ redemption แบบ atomic ที่ระดับ SQL ← **ตัวถัดไป** (คำสั่งพร้อมใช้ใน §7)
3. **`P3-03` + `P3-06`** Order expiry job (Hangfire) + กัน race กับ webhook — ตอนนี้ไม่มี job หมดอายุเลย
4. **`P3-07`** Payment ops queue ฝั่ง admin (resolve + แจ้งผู้ซื้อ)
5. **`P4-04`** Instructor analytics API → แล้วต่อ **`P4-23`** (หน้าที่เป็น mock อยู่)
6. **ต่อสาย 6 หน้าที่เป็น mock** (X-3): `P6-26` แดชบอร์ด, `P6-27` ผู้ใช้และสิทธิ์, `P6-23` อนุมัติคอร์ส, `P6-21` marketing, `P3-25` หน้าชำระเงินสำเร็จ — BE พร้อมหมดแล้ว งานคือเรียก API จริง + i18n
7. **`P3-20` + `P3-23`** ตะกร้า + ประวัติคำสั่งซื้อ (ต้องเพิ่ม `GET /orders` ฝั่ง BE ด้วย)

### Wave 2 — เฟสที่ยังขาดจริง
8. **`P5-20` / `P5-21` / `P5-23` / `P5-24`** หน้า quiz / assignment / Q&A / attachment (BE พร้อมหมดแล้ว)
9. **`P2-20`** เขียน player ใหม่ด้วย Shaka Player จริง (ตอนนี้เป็น `<video>` ธรรมดา ไม่มี `shaka-player` ใน `package.json` เลย) — ทำได้โดยไม่ต้องรอ Q8 เพราะ Shaka รองรับ clear-key อยู่แล้ว
10. **`P2-05`** DRM license proxy — **รอ Q8 ก่อน** เพราะรูปร่างของงานขึ้นกับ tier ที่เลือก (MediaCage Basic อาจไม่มี license server ให้ proxy เลย)
11. **`P6-03` + `P6-04`** revenue split + payout batch — **รอ Q4** แล้วเขียนใหม่ทั้งก้อน (ตอนนี้ `AddItem()` ไม่เคยถูกเรียกจากที่ไหนเลย)
12. **`P4-21` / `P4-25` / `P6-20`(menu) / `P6-25`** งาน FE ที่เหลือ
13. **X-4 / X-5 / X-6** งานทำความสะอาดข้ามเฟส (i18n, RoleNames, pattern ของ Analytics)

### Wave 3 — QA ปิดเฟส
`P2-30`/`P2-31` · `P3-31` · `P4-30`/`P4-31` · `P5-30` · `P6-30` · `P1-31`/`P1-32`

---

## 5. Template คำสั่งต่อ task

```
Task: {TASK_ID} — {ชื่อ task}

อ่านก่อน (เรียงตามนี้):
1. CLAUDE.md — กฎโปรเจ็ค
2. docs/TASKS.md — แถวของ {TASK_ID} (acceptance = definition of done) + § "สถานะจริงของงานที่ยังไม่ปิด"
   ซึ่งระบุว่า task นี้ขาดอะไรอยู่บ้างแล้ว
3. docs/ANTIGRAVITY_HANDOFF.md §2 (กฎ) + §3 (DoD) — ไฟล์นี้
4. .claude/rules/{backend|frontend}.md — เช็คก่อนว่าโมดูลที่จะแตะเป็น vertical slice (Identity/Catalog/
   Notification) หรือ Repository+Service (Commerce/Media/Learning/Payout/Cms/Community/Analytics)
5. .claude/rules/database.md — ถ้าแตะ schema (+ ส่วน UPPERCASE ถ้าอยู่ใน 7 โมดูลใหม่)
6. .claude/rules/security.md — ถ้าแตะ เงิน / สิทธิ์เรียน / playback / ข้อมูลส่วนบุคคล
7. งาน FE: frontend/design-system/siri-upskill/mockups-2026-08-21/ (ไฟล์ล่าสุด 16 หน้าจอ)
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

mockup ล่าสุด: `frontend/design-system/siri-upskill/mockups-2026-08-21/SIRI UpSkill Mockups.dc.html` (16 หน้าจอ — 10 หน้าแรกเนื้อหาเหมือนไฟล์ 2026-08-20 ทุกประการ)

| # | หน้าจอ mockup | Backend scope | Schema | สถานะ 2026-08-24 |
|---|---|---|---|---|
| 1 | ค้นหา & หมวดหมู่ | Catalog | `catalog` | FE/BE เสร็จ (เหลือ P1-25 search UI) |
| 2 | ชำระเงิน (Checkout) | Commerce | `COMMERCE` | `BUILT` — เหลือ promo/expiry (P3-09/03) |
| 3 | ห้องเรียน (Classroom) | Media + Learning + Community | `MEDIA`,`LEARNING`,`COMMUNITY` | `BUILT` แต่ player ยังไม่ใช่ Shaka (P2-20) |
| 4 | ความคืบหน้า & ใบรับรอง | Learning | `LEARNING` | `BUILT` |
| 5 | เส้นทางการเรียน | Catalog (LearningPaths) | `catalog` | BE `BUILT` (P8-05) · FE ยังไม่มี (P8-06) |
| 6 | สร้างคอร์ส (ผู้สอน) | Catalog + Media | `catalog`,`MEDIA` | ⚠️ UI มีแต่ **บันทึกไม่ได้** — P4-01 |
| 7 | วิเคราะห์การขาย (ผู้สอน) | Analytics/Payout | `analytics` | ⚠️ **mock ล้วน** — P4-04 + P4-23 |
| 8 | จัดการเนื้อหา CMS (แอดมิน) | Cms | `CMS` | `BUILT` (เหลือ menu editor) |
| 9 | โปรโมชัน (แอดมิน) | Commerce | `COMMERCE` | ⚠️ FE **mock ล้วน** — P6-21 |
| 10 | รายงานการเงิน (แอดมิน) | Payout | `PAYOUT` | `PART` — P6-04 ยังไม่รวมยอดจริง |
| 11 | ชำระเงินสำเร็จ | Commerce | `COMMERCE` | ⚠️ FE **mock ล้วน** — P3-25 |
| 12 | รายได้ของฉัน (ผู้สอน) | Payout | `PAYOUT` | `BUILT` (P4-26/P6-08) |
| 13 | ประกาศถึงผู้เรียน | Notification | `notify` | `BUILT` (P4-27/P6-05) — ตาราง `Announcements` สร้างแล้ว |
| 14 | แดชบอร์ดแอดมิน | Analytics + Contracts ข้ามโมดูล | หลายโมดูล | BE `BUILT` (P6-07) · FE **mock** (P6-26) |
| 15 | อนุมัติคอร์ส (แอดมิน) | Catalog (P1-05) | `catalog` | BE เสร็จนานแล้ว · FE **mock** (P6-23) |
| 16 | ผู้ใช้และสิทธิ์ (แอดมิน) | Identity | `identity` | BE `PART` (P6-06) · FE **mock** (P6-27) |

---

## 7. คำสั่งพร้อมใช้ — task ถัดไปของ Wave 1

> `P4-01` ปิดแล้ว (2026-08-25) · ตัวถัดไปคือ **`P3-09`** — คัดลอกทั้งบล็อกนี้ส่งให้ Antigravity ได้เลย

```
Task: P3-09 — Promo code validate / apply / redeem

อ่านก่อน:
1. CLAUDE.md
2. docs/TASKS.md — แถว P3-09 + section "สถานะจริงของงานที่ยังไม่ปิด" หัวข้อ P3
3. docs/ANTIGRAVITY_HANDOFF.md §2 (กฎ) และ §3 (Definition of Done)
4. .claude/rules/backend.md — Siri.Modules.Commerce เป็น **Repository+Service + UPPERCASE**
   (คนละแบบกับ Catalog ที่เพิ่งทำใน P4-01 อย่าเอา vertical slice มาใส่)
5. .claude/rules/database.md — ส่วน "นับโควตา (promo code, ที่นั่ง) ต้อง atomic ที่ระดับ SQL
   ห้ามอ่านมาเช็คใน memory แล้วค่อยเขียน" · .claude/rules/security.md (ราคาคำนวณที่ server เท่านั้น)

สถานะปัจจุบัน (ตรวจไฟล์จริงแล้ว 2026-08-25):
- `PROMO_CODE` entity ครบแล้ว: CODE (unique index), DISCOUNT_TYPE, DISCOUNT_VALUE, MAX_REDEMPTIONS,
  REDEEMED_COUNT, MAX_PER_USER, MIN_ORDER_AMOUNT, STARTS_AT_UTC, ENDS_AT_UTC, SCOPE, SCOPE_REF_ID,
  IS_ACTIVE — มี method แค่ Activate()/Deactivate() **ยังไม่มี method เพิ่ม REDEEMED_COUNT**
- `PROMO_REDEMPTION` entity + repository มีแล้ว มี unique index (PROMO_CODE_ID, ORDER_ID)
  แต่ **ยังไม่มีโค้ดไหนเขียนแถวลงตารางนี้เลยสักบรรทัด**
- `PromoCodeService` มีแค่ CreateAsync / GetByIdAsync / ListAsync (ฝั่ง admin) — ไม่มี validate/apply
- `OrderService.cs:53` เขียนไว้ตรง ๆ ว่า `var discount = 0m;` แล้ว `total = subtotal - discount`
  → ทุกออร์เดอร์ตอนนี้ส่วนลด 0 เสมอ · `ORDER` มีคอลัมน์ DISCOUNT_AMOUNT และ PROMO_CODE_ID รออยู่แล้ว
- หน้า checkout ฝั่ง FE มีช่องกรอกโค้ดส่วนลดอยู่แล้ว แต่ยังไม่มี endpoint ให้เรียก

สิ่งที่ต้องทำ:
1. `POST /api/commerce/promo-codes/validate` (ผู้ซื้อที่ล็อกอินแล้ว) — รับ code + รายการคอร์สในตะกร้า
   คืนว่าใช้ได้ไหมและลดเท่าไร **ห้ามให้ client ส่งยอดส่วนลดมาเอง** (security.md: ราคาคิดที่ server)
   ตรวจครบ: IS_ACTIVE, ช่วงเวลา (ใช้ IClock ห้าม DateTime.UtcNow), MIN_ORDER_AMOUNT, SCOPE/SCOPE_REF_ID,
   MAX_REDEMPTIONS, MAX_PER_USER
2. ต่อเข้ากับ `OrderService.CreateAsync` — แทนที่ `var discount = 0m;` ด้วยการคิดส่วนลดจริงจาก
   promo code ที่ส่งมาพร้อมคำสั่งซื้อ แล้วบันทึก `ORDER.DISCOUNT_AMOUNT` + `ORDER.PROMO_CODE_ID`
   (คิด VAT จากยอดหลังหักส่วนลด — ยืนยันลำดับกับ docs/PAYMENT.md ก่อนเขียน)
3. **นับโควตาแบบ atomic ที่ระดับ SQL** — ห้ามอ่าน REDEEMED_COUNT มาเทียบใน memory แล้วค่อย SaveChanges
   ใช้ UPDATE แบบมีเงื่อนไขในคำสั่งเดียว (`ExecuteSqlInterpolated`: SET REDEEMED_COUNT = REDEEMED_COUNT + 1
   WHERE PROMO_CODE_ID = ... AND REDEEMED_COUNT < MAX_REDEMPTIONS) แล้วเช็คจำนวนแถวที่ถูกแก้
   ถ้าเป็น 0 แปลว่าโควตาเต็มพอดีตอนนั้น ต้องปฏิเสธ ไม่ใช่ปล่อยผ่าน
   · MAX_PER_USER จะกันด้วยวิธีไหนให้ตัดสินแล้วเขียนเหตุผลไว้ในโค้ด (unique index (PROMO_CODE_ID, USER_ID)
     ใช้ไม่ได้ตรง ๆ ถ้า MAX_PER_USER > 1 — ถ้าเลือกเพิ่ม index/constraint ต้องมี migration)
4. เขียน `PROMO_REDEMPTION` ลงจริงในธุรกรรมเดียวกับการสร้าง/ยืนยันออร์เดอร์ + reversal เมื่อ refund
   (ถ้า reversal เกินขอบเขต ให้บอกตรง ๆ แล้วบันทึกไว้ใน § "สถานะจริง" อย่าทำครึ่งเดียวเงียบ ๆ)
5. ต่อช่องกรอกโค้ดในหน้า checkout เข้ากับ endpoint จริง + i18n ทุกข้อความ (ผลลัพธ์: ใช้ได้/หมดอายุ/
   โควตาเต็ม/ยอดไม่ถึงขั้นต่ำ/ใช้ไปแล้ว)

Definition of done: ตาม §3 ทุกข้อ โดยเฉพาะ integration test ที่พิสูจน์:
- โค้ดหมดอายุ / โควตาเต็ม / ยอดไม่ถึงขั้นต่ำ / ใช้เกิน MAX_PER_USER → ถูกปฏิเสธ
- **แข่งกันใช้โค้ดใบสุดท้ายพร้อมกัน 2 คำขอ → สำเร็จได้ใบเดียวเท่านั้น** (พิสูจน์ที่ระดับ DB จริง
  ไม่ใช่ mock — นี่คือเหตุผลที่ task นี้ต้องมี integration test)
- ยอดใน ORDER ตรงกับส่วนลดที่คำนวณ และ client ส่งยอดเองไม่ได้

นอกขอบเขต: flash sale / bundle / ลำดับความสำคัญของส่วนลด = P6-02 (task แยก),
ห้ามแตะ StripeWebhookHandler, ห้ามแก้ Catalog
```
