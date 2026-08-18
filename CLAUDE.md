# SIRI UpSkill — Project Instructions

E-Learning marketplace (แนว SkillLane / FutureSkill): ผู้เรียนซื้อคอร์สวิดีโอเข้าถึงระยะยาว, instructor สร้างคอร์สเอง, platform หักส่วนแบ่งรายได้ ความปลอดภัยของวิดีโอ (DRM/watermark/กันแชร์บัญชี) เป็น **critical requirement** ไม่ใช่ของแถม

## Stack (ห้ามเปลี่ยนโดยไม่ถาม)

| Layer | Technology | Version |
|-------|-----------|---------|
| Frontend | Angular (standalone + signals + zoneless) + Tailwind CSS v4 | **22.1.x** |
| Backend | ASP.NET Core Web API (Minimal API, vertical slice) | **.NET 10 LTS** |
| ORM | EF Core (SQL Server provider) | **10.x** |
| Database | SQL Server | **2022+** |
| Cache/Session | Redis | 7+ |
| Jobs | Hangfire (SQL Server storage) | latest |
| Player | Shaka Player | latest |
| **Video/DRM** | **Bunny Stream** (หลัง `IVideoProvider`) | — |
| **Payment** | **Stripe** (PromptPay QR ผ่าน PaymentIntent + webhook — v1 ยังไม่เปิดบัตร, ไม่มีผ่อน) | — |
| **Hosting** | **Contabo VPS + Docker Compose + Caddy** | — |
| Test | xUnit + Testcontainers / Vitest + Playwright | — |

**ห้ามใช้:** MediatR (เปลี่ยนเป็น commercial license แล้ว), AutoMapper (ผูก license เช่นกัน — เขียน mapping เอง), NgModule, zone.js pattern, Azure Media Services (ปลดระวางแล้ว)

## เอกสารอ้างอิง — อ่านก่อนลงมือ

| ทำงานเรื่อง | อ่านไฟล์ |
|------------|---------|
| **รับ task มาทำ (เริ่มที่นี่)** | **`docs/TASKS.md`** — task ทั้งหมดมี ID เช่น `P0-11` พร้อม dependency และ acceptance |
| requirement / scope ใด ๆ | `docs/REQUIREMENTS.md` |
| ชำระเงิน, Stripe, PromptPay, webhook | `docs/PAYMENT.md` |
| deploy, Contabo, backup, docker, CI/CD | `docs/DEPLOYMENT.md` |
| โครงสร้าง module, layer, boundary | `docs/ARCHITECTURE.md` |
| ตาราง, migration, index, query | `docs/DATABASE.md` + `.claude/rules/database.md` |
| auth, payment, video, ข้อมูลส่วนบุคคล | `docs/SECURITY.md` + `.claude/rules/security.md` |
| C# / API ใด ๆ | `.claude/rules/backend.md` |
| Angular / TypeScript ใด ๆ | `.claude/rules/frontend.md` |
| หน้าจอ, component, สี, layout, a11y | `.claude/rules/ui-design.md` |
| ลำดับงาน, commit, PR, definition of done | `.claude/rules/workflow.md` |
| เลือก vendor / ตัดสินใจสถาปัตยกรรม | `docs/DECISIONS.md` |
| แผนงาน, phase, ลำดับความสำคัญ | `docs/ROADMAP.md` |

## กฎเหล็ก (ใช้กับทุกงาน)

1. **ภาษา** — คุยกับ user เป็นภาษาไทย; โค้ด, ชื่อตัวแปร, comment, commit message, ชื่อไฟล์ เป็นภาษาอังกฤษเสมอ ข้อความที่ผู้ใช้ปลายทางเห็นต้องอยู่ในไฟล์ i18n ห้าม hardcode ในโค้ด
2. **ห้ามเดา version / API** — ถ้าไม่แน่ใจว่า Angular 22 หรือ .NET 10 มี API นั้นจริงไหม ให้ค้นหาหรือเช็คจาก `package.json` / `.csproj` ก่อน **ห้ามแต่ง API ขึ้นมาเอง**
3. **Security เป็น acceptance criteria** — endpoint ที่แตะ enrollment, playback, เงิน, ข้อมูลส่วนตัว ต้องมี authorization + ownership check เสมอ ถ้างานที่ได้รับมอบหมายไม่ได้พูดถึงเรื่องนี้แต่โค้ดต้องมี ให้ใส่ให้และบอกด้วย
4. **ห้ามคิดราคา / ตัดสินสิทธิ์ที่ frontend** — server เท่านั้นที่คำนวณราคา ส่วนลด และตัดสินว่าใครดูอะไรได้
5. **ห้าม cascade delete หรือ hard delete** กับข้อมูลเงินและสิทธิ์เรียน (Orders, Payments, RevenueSplits, Enrollments, Certificates)
6. **ทำตาม scope ที่สั่ง** — เห็นปัญหานอกขอบเขตให้บอก อย่าไปแก้เอง; ห้าม refactor ข้ามโมดูลโดยไม่ถาม
7. **ห้ามสร้างไฟล์ที่ไม่จำเป็น** โดยเฉพาะ README / summary / doc ซ้ำซ้อน แก้ไฟล์เดิมก่อนเสมอ
8. **ห้าม commit / push เอง** เว้นแต่ user สั่งชัดเจน
9. **ห้ามใส่ secret ลงในโค้ดหรือ config ที่ commit** — dev ใช้ `dotnet user-secrets`, prod ใช้ env/Key Vault
10. **เขียน test มาพร้อมโค้ด** สำหรับ business logic, การคำนวณเงิน, การตัดสินสิทธิ์ (ไม่ใช่ค่อยเขียนทีหลัง)
11. **บอกความจริงเรื่องผลลัพธ์** — build ไม่ผ่าน test แดง หรือทำไม่ครบ ให้บอกตรง ๆ พร้อม output ห้ามสรุปว่า "เสร็จแล้ว" ถ้ายังไม่ได้รัน
12. **ก่อนแตะโค้ดที่มีอยู่** — อ่าน pattern รอบข้างแล้วทำตาม อย่าเอา style ใหม่มายัด

## คำสั่งที่ใช้บ่อย

```bash
dotnet build backend/SiriUpSkill.sln
```
```bash
dotnet test backend/SiriUpSkill.sln
```
```bash
dotnet ef migrations add <Name> --project backend/src/Siri.Persistence --startup-project backend/src/Siri.Api
```
```bash
npm --prefix frontend run start
```
```bash
npm --prefix frontend run test
```
```bash
# Seed dev/test accounts (Admin + Learner/Instructor test users) against whatever
# ConnectionStrings:Default currently points at. ต้อง dotnet user-secrets set
# "Identity:Seed:AdminPassword" / "Identity:Seed:TestUserPassword" ก่อน (ค่า default ใน
# appsettings คือ "CHANGE_ME" ตั้งใจให้ไม่ทำงานจนกว่าจะตั้งค่าจริง) — idempotent รันซ้ำได้ปลอดภัย
dotnet run --project backend/src/Siri.Api -- --seed
```

## Database — ต่อจริงแล้ว end-to-end (2026-08-17)

MSSQL รันอยู่บน Contabo VPS จริง (`217.217.253.122,1433`) database `SIRIUPSKILL` collation `Thai_100_CI_AS_SC_UTF8` login แอป `siriupskill_app` (`db_owner` จำกัดแค่ database นี้ ไม่ใช่ `sa`)
**Connection string อยู่ใน `dotnet user-secrets` ของ `Siri.Api` แล้ว** (`UserSecretsId` ผูกไว้ใน `Siri.Api.csproj`) — ดูค่าได้ด้วย `dotnet user-secrets list --project backend/src/Siri.Api` ห้ามเขียนลง `appsettings*.json` หรือไฟล์ใดใน repo เด็ดขาด
ทดสอบแล้วจริงด้วย `dotnet ef migrations add` + `dotnet ef database update` ผ่าน `siriupskill_app` สำเร็จ (มีตาราง `__EFMigrationsHistory` ใน DB แล้ว)
⚠️ server เป็น **SQL Server Developer edition** (ใช้ dev ได้ ห้ามใช้ production ตาม license) และ **port 1433 เปิดออกอินเทอร์เน็ตสาธารณะอยู่** — รายละเอียดเต็มอยู่ที่ `docs/DEPLOYMENT.md`

## สถานะโปรเจ็คปัจจุบัน

> **Phase 0 — กำลังทำทีละ task ตาม `docs/TASKS.md`, ตรวจสอบซ้ำทุก task ก่อนขึ้น DB จริง**
> - Backend: `backend/SiriUpSkill.sln` 21 projects, build สะอาด 0 warning/error ตลอด, unit test 198/198, architecture test 4/4 (integration test 48 ตัวข้ามเพราะเครื่อง dev ไม่มี Docker — ปกติ ตรวจแล้วว่าทุกตัว fail ด้วย `DockerUnavailableException` จริง ไม่มี assertion fail แอบซ่อนอยู่)
> - Frontend: `frontend/` Angular 22.1 zoneless + SSR + Tailwind v4 build/lint สะอาด ยืนยัน SSR render จริงแล้ว
> - **P0-14 เสร็จ:** Identity domain (User/Role/UserSession/RefreshToken/SecurityAudit) — apply ขึ้น DB จริงแล้ว (6 ตาราง schema `identity`, seed 4 role)
> - **P0-15 เสร็จ:** สมัคร + ยืนยันอีเมล (`POST /api/identity/register`, `/confirm-email`) — password policy, anti-enumeration, rate limit "auth" (ไม่มี migration ใหม่) — บล็อกนี้ตกหล่นไม่ได้อัปเดตตอน P0-15 จบจริง แก้ไขให้ตรงตอนจบ P0-16
> - **P0-16 เสร็จ:** ล็อกอิน + refresh-token rotation (`POST /api/identity/login`, `/refresh`) — JWT access token 15 นาที (claims: user id, role) validate ผ่าน `AddJwtBearer`, refresh token 30 วัน หมุนเวียนทุกครั้ง + reuse detection (เจอ token เก่าถูกใช้ซ้ำ → revoke ทั้ง session/family, มี test พิสูจน์ตรง ๆ) เก็บใน httpOnly+Secure+SameSite=Strict cookie, `IUserContext` ตัวจริงอ่านจาก JWT claims แทน `AnonymousUserContext` แล้ว (ไม่มี migration ใหม่ ใช้ตาราง UserSessions/RefreshTokens/SecurityAudits จาก P0-14)
> - **แก้เพิ่มหลัง P0-16:** `UserPasswordHasher` ตั้ง `IterationCount = 600_000` แล้วตรงตาม security.md (เดิมใช้ default 100,000) — คำนวณ `DummyPasswordHash` ใน Login handler ใหม่ให้ตรงกันด้วย (ไม่งั้น timing parity จะพัง) ยังไม่มี user จริงบน DB จึงไม่ต้อง migrate hash เก่า
> - **P0-19 + P0-23 เสร็จ:** Email outbox (`notify.EmailOutbox`) + Hangfire wiring (`/hangfire` dashboard, ตอนแรกจำกัดแค่ localhost ชั่วคราวเพราะยังไม่มี AdminOnly policy จริง — P0-22 เพิ่ม role check แล้ว ดูด้านล่าง) + SMTP adapter (MailKit, ยังไม่ตัดสินใจ vendor จริง ใช้ LoggingEmailSender เป็น default) — apply ขึ้น DB จริงแล้ว
> - **P0-17 เสร็จ (โค้ด+migration พร้อม แต่ยังไม่ apply ขึ้น DB จริง — ดูด้านล่าง):** SE-03 concurrent login control — `Users.MaxConcurrentSessionsOverride int NULL` (migration `AddUserMaxConcurrentSessionsOverride`), system default `Identity:Security:MaxConcurrentSessions=2` (Options+ValidateOnStart), Redis session mirror (`ISessionRegistry`/`RedisSessionRegistry`, key `session:{userId}:{sessionId}` TTL=refresh token 30 วัน, fail-open ตาม ARCHITECTURE.md §5's phased design — MSSQL ยังเป็น source of truth ของการ evict ในเฟสนี้ ไม่ใช่ Redis) enforcement ต่อท้าย Login handler (evict เก่าสุดเกิน limit + revoke refresh token + audit `session.evicted_concurrent_limit` + อีเมลแจ้งเตือนผ่าน `notify.EmailOutbox` ทั้งหมดใน `SaveChangesAsync` เดียวกับ login) Refresh's reuse-detection (P0-16) ก็ลบ Redis key ตามด้วยแล้ว unit test เพิ่ม 18 ตัว (`User.SetMaxConcurrentSessionsOverride` + `ConcurrentSessionEvictionPolicy` pure logic) integration test เพิ่ม 3 ตัว (`ConcurrentSessionLimitTests.cs`, ข้ามเพราะไม่มี Docker เหมือน integration test อื่น)
> - **P0-22 เสร็จ:** Authorization policies — `AdminOnly` (Admin หรือ SuperAdmin) และ `InstructorOnly` (Instructor, Admin หรือ SuperAdmin — admin ทำหน้าที่แทน instructor ได้เพื่อ support/moderation, ตัดสินใจแบบนี้ชัดเจนแล้วเขียนเหตุผลไว้ที่ `AuthorizationPolicyExtensions`) ผ่าน `services.AddSiriAuthorizationPolicies()` (`Siri.Api/Authorization/AuthorizationPolicyExtensions.cs`) — `RequireRole(...)` อ่าน `ClaimTypes.Role` claim ตรงกับที่ `AccessTokenGenerator` ออกให้อยู่แล้ว ไม่ต้อง migration ใหม่ `CourseOwner`/`EnrolledInCourse` ยังไม่สร้างเพราะ Catalog/Learning ยังเป็น stub ว่างอยู่ (คอมเมนต์ไว้ในไฟล์เดียวกันว่าต้องเพิ่มยังไงตอน module พร้อม) retrofit default-deny ที่ระดับ group แล้ว: `IdentityModule.MapIdentityEndpoints` เรียก `.RequireAuthorization()` ที่ group `/api/identity` แล้ว Register/ConfirmEmail/Login/Refresh ใส่ `.AllowAnonymous()` รายตัว (ยืนยันแล้วว่าไม่มี endpoint อื่นในโมดูลนี้ที่จะพังจากการเปลี่ยนนี้) Hangfire dashboard filter (`LocalhostOnlyDashboardAuthorizationFilter`) เพิ่มทางเลือกที่สอง: localhost **หรือ** JWT ที่มี role Admin/SuperAdmin (ยังไม่มีทางให้ browser ทั่วไปที่ไม่ใช่ localhost ส่ง bearer token ได้ — เป็น known gap รอ admin frontend/session mechanism จริงในอนาคต) unit test เพิ่ม 16 ตัว (`AuthorizationPolicyTests` ผ่าน `IAuthorizationService` จริง + `IdentityEndpointAuthorizationTests` ตรวจ endpoint metadata ตรง ๆ ไม่ผ่าน HTTP) integration test เพิ่ม 14 ตัว (`AuthorizationPolicyHttpTests`, ข้ามเพราะไม่มี Docker เหมือน integration test อื่น — สร้าง `WebApplication` ของตัวเองแยกจาก `Program.cs` จริงเพื่อไม่เสี่ยงต่อ DB จริงจากปัญหา config-override timing ของ `WebApplicationFactory`)
> - **P0-21 เสร็จ:** ลืมรหัสผ่าน / รีเซ็ต (`POST /api/identity/forgot-password`, `/reset-password`) — ใช้กลไก `UserSecurityToken`/`ISecurityTokenGenerator` เดิมจาก P0-15 (ไม่มี migration ใหม่, `Purpose = PasswordReset` เป็นค่าจริงแล้ว) token หมดอายุ **1 ชม.** ตาม `docs/TASKS.md`'s acceptance criterion (สั้นกว่า email confirmation 24 ชม. เพราะเสี่ยงกว่า) คำขอใหม่ invalidate token `PasswordReset` เก่าที่ยังไม่หมดอายุของ user เดียวกันเสมอ (ตัดสินใจแล้วเขียนเหตุผลไว้ที่ `ForgotPasswordHandler`) — เพิ่ม `User.ChangePassword(newPasswordHash)` (domain method ใหม่, private setter ปกติ, ปฏิเสธถ้า user status = Deleted) reset สำเร็จ **revoke ทุก session/refresh token ที่ active ของ user นั้น** (การตัดสินใจด้านความปลอดภัยจริง — คนที่ขอ reset มักสงสัยว่าบัญชีถูกบุกรุก, เขียนเหตุผลไว้ที่ `ResetPasswordHandler`) reuse revoke method + Redis mirror cleanup (`ISessionRegistry.RemoveAsync`) แบบเดียวกับ Login/Refresh's eviction/reuse-detection path เขียน `SecurityAudit` event type ใหม่ `password.reset_succeeded` + ส่งอีเมลแจ้งเตือน "รหัสผ่านถูกเปลี่ยนแล้ว" (`identity-password-changed` template) แยกจากอีเมลลิงก์ reset (`identity-password-reset`) ForgotPassword's anti-enumeration ไม่ copy Login's `DummyPasswordHash` ตรง ๆ เพราะ flow นี้ไม่ hash รหัสผ่านเลย (เหตุผลเต็มอยู่ใน `ForgotPasswordHandler`'s doc comment) unit test เพิ่ม 34 ตัว (`User.ChangePassword` + `ForgotPasswordValidator`/`ResetPasswordValidator`, validator ตัวหลัง reference `RegisterValidator`'s constants/denylist ตรง ๆ ไม่ copy กฎซ้ำ) integration test เพิ่ม 7 ตัว (`ForgotPasswordAndResetPasswordTests.cs`, ข้ามเพราะไม่มี Docker เหมือน integration test อื่น)
> - **P0-18 เสร็จ + verify ผ่านแล้ว (IDOR-focused fresh-eyes pass):** API จัดการอุปกรณ์ (`GET /api/identity/sessions`, `DELETE /sessions/{sessionId}`, `POST /sessions/revoke-others`, `POST /sessions/revoke-all`) — ไม่มี migration ใหม่ (เพิ่มแค่ claim `"sid"` = `UserSession.Id` ใน access token ทุกใบ ผ่าน `AccessTokenGenerator.Generate(user, sessionId)`, `Login`/`Refresh` handler ส่ง session id เข้าไปแล้ว) **ownership check ของ `RevokeSession` ยืนยันแล้วว่าปลอดภัยจริง**: query กรอง `s.Id == sessionId && s.UserId == callerUserId` ในเงื่อนไขเดียวกัน ไม่ query แยกแล้วเช็คทีหลัง — id ไม่มีจริง/เป็นของคนอื่น/ถูก revoke ไปแล้ว ตอบ 404 เดียวกันทุกทาง (พิสูจน์ด้วย integration test ที่ diff response body หลังตัด `traceId` ออกจริง ไม่ใช่แค่เทียบ status code) `ListSessions`/`RevokeOtherSessions`/`RevokeAllSessions` กรองด้วย `command.UserId` (มาจาก `IUserContext` เท่านั้น) ทุก query เช่นกัน ไม่มีทางรับ `userId` จาก client เลย (ไม่มี `[FromQuery]`/`[FromBody]` ที่ชื่อคล้าย userId ที่ไหนในโมดูลนี้) ทั้ง 4 endpoint ไม่มี `.AllowAnonymous()` (default-deny ที่ระดับ group คุมอยู่จริง มี test ยืนยันตรง metadata) unit test ใหม่ 8 ตัว + integration test ใหม่ 13 ตัว (`DeviceManagementTests.cs`, ข้ามเพราะไม่มี Docker เหมือน integration test อื่น) — verify pass เปิด build สะอาดจาก clean state (ลบ bin/obj ทั้งหมดก่อน) ยืนยัน 0 warning/0 error, unit 184/184 ผ่าน, architecture 4/4 ผ่าน, integration 46/46 fail เพราะไม่มี Docker เหมือนเดิม (เช็คแล้วว่าทุก failure root exception เป็น `DockerUnavailableException` จริง ไม่มีอันไหน fail เพราะ assertion) ไม่พบ IDOR bug ไม่ได้แก้โค้ดอะไรเพิ่ม
> - **P0-37 เสร็จ + verify ผ่านแล้ว (fresh-eyes pass เน้น hard constraint ห้ามต่อ DB จริง):** `dotnet run --project backend/src/Siri.Api -- --seed` — seed 1 Admin + 5 test user (3 Learner + 2 Instructor, ทั้งหมด `@example.test`/`.seed@` + ชื่อมี "(Seed)" กำกับ ปนกับข้อมูลจริงไม่ได้) ผ่าน `IdentitySeeder` ใน `Siri.Modules.Identity/Infrastructure/Seeding/` (ไม่สร้าง project ใหม่ ไม่มี migration ใหม่ — role ใช้ของเดิมจาก P0-14) idempotent จริง: เช็คทีละ account ด้วย `NormalizedEmail` ก่อน insert (ไม่ enumerate/wipe ตารางทั้งก้อน) มี integration test เรียก `SeedAsync` สองครั้งในเทสต์เดียวแล้วยืนยัน count ไม่ขึ้น + อีกตัวพิสูจน์ว่าไม่แตะ user จริงที่ไม่เกี่ยวข้อง `AdminPassword`/`TestUserPassword` ใน appsettings เป็น literal `"CHANGE_ME"` (`SeedOptionsGuard` ปฏิเสธรันถ้ายังเป็นค่านี้ — เช็คก่อนแตะ DB บรรทัดแรกของ `SeedAsync`) ต้อง `dotnet user-secrets set` ค่าจริงก่อนถึงจะรันได้ — **verify แล้วว่า agent ไม่เคยต่อ/รัน seed ใส่ DB จริงเลยตลอด session นี้**: ไล่ transcript ทุกคำสั่ง Bash ที่ agent เรียกจริง (29 คำสั่ง) ไม่มี `dotnet run`/`dotnet ef database update`/`dotnet user-secrets` เลยสักครั้ง, ไม่มีการ `Read` ไฟล์ user-secrets เลย, ไฟล์ user-secrets (`secrets.json`) มีแค่ key `ConnectionStrings:Default` เดิม ไม่มี `Identity:Seed:*` เพิ่ม และ mtime ไม่ขยับเลยตลอดช่วงที่ทำ P0-37 build สะอาดจาก clean state ยืนยัน 0 warning/0 error, unit 198/198 ผ่าน (184 เดิม + 14 ใหม่), architecture 4/4 ผ่าน, integration 48/48 fail เพราะไม่มี Docker เหมือนเดิม (46 เดิม + 2 ใหม่ `IdentitySeederTests`, เช็คแล้วว่าทุกตัว root exception เป็น `DockerUnavailableException` จริง) — **ค้าง (ตามคำสั่งงาน ห้าม agent ทำเอง):** ยังไม่มีใคร `dotnet user-secrets set "Identity:Seed:AdminPassword"`/`TestUserPassword` + รัน `--seed` จริงบน DB จริง ต้องให้คนรันเองตอนพร้อมใช้
> - **P0-31 เสร็จ:** Design system จริง — ค้นด้วย skill `ui-ux-pro-max` (ลง Python สำเร็จแล้ว) รอบ auto `--design-system` แรก ๆ เดาผิดทาง (เลือกสไตล์เด็ก/Claymorphism เพราะ keyword ชนฐานข้อมูล) เลยประกอบเองจากการค้นแยก domain: สีน้ำเงิน trust `#0369A1` + ทอง achievement `#B45309`, ฟอนต์ IBM Plex Sans Thai ตัวเดียวทั้งระบบ (รองรับไทย+อังกฤษเต็ม), สไตล์ Flat Design + วินัย spacing แบบ Minimalism เหตุผลเต็มที่ `frontend/design-system/siri-upskill/MASTER.md`, ใช้จริงใน `tokens.css` + โหลดฟอนต์ใน `index.html`
> - **P0-34 เสร็จ + verify ผ่านแล้ว (a11y-focused fresh-eyes pass พร้อม live browser testing จริง):** UI kit 10 ตัว (`shared/`: button, input, select, checkbox, modal, toast, skeleton, empty-state, pagination, breadcrumb) — standalone/OnPush/signal-based ทั้งหมด, icon ผ่าน `@ng-icons/core`+`@ng-icons/phosphor-icons`, Modal focus trap ผ่าน `@angular/cdk`'s `CdkTrapFocus` (พิสูจน์ด้วย live browser: focus เข้า/ออก/wrap ถูกต้อง, Escape/backdrop-click คืน focus ไปจุดเดิมจริง), Select ใช้ native `<select>` (ตัดสินใจแล้วเพราะ a11y ดีกว่า custom combobox โดยไม่มีความจำเป็นต้องทำเอง) — **เจอบั๊กจริงระหว่างทำ**: `placeholder="null"` หลุดออกมาจริงในหน้าเว็บ (property binding ผิด แก้เป็น attr binding) และที่สำคัญกว่าคือ **`strict: true` หายไปจาก `tsconfig.json` ทั้งที่กฎ frontend.md บังคับไว้เป็น hard rule** (verify agent เช็คก่อนว่าเปิดได้จริงไม่มี error ทั้งโปรเจ็คแล้วค่อยเปิด) — 47/47 test ผ่าน (รวม ControlValueAccessor round-trip ที่ verify agent เพิ่มเข้ามาเพราะของเดิมไม่มี test คลุม)
> - **P0-35 เสร็จ + verify ผ่านแล้ว (ไม่พบบั๊กแม้แต่จุดเดียว หลังตรวจ anti-enumeration + auth guard + API contract 9 endpoint):** หน้า auth 6 หน้า (`/register`, `/confirm-email`, `/login`, `/forgot-password`, `/reset-password`, `/account/devices`) ต่อ backend API จริงทั้งหมด ไม่มี mock ในโค้ดจริง — SSR สำหรับหน้า public (noindex สำหรับ confirm-email/reset-password เพราะ URL มี token), `/account/devices` เป็น CSR-only ผ่าน `authGuard` (access token in-memory เท่านั้น ไม่แตะ localStorage เลย — grep ยืนยันแล้ว) `ConfirmEmailPage` เรียก API ผ่าน `afterNextRender()` เท่านั้น กัน SSR/crawler เผลอเบิร์น token ทิ้ง — anti-enumeration integrity ยืนยันทั้ง 5 endpoint ว่าไม่มี branch/field-error ไหนหลุดข้อมูลเพิ่มจากที่ backend ตั้งใจซ่อน revoke-session modal ยืนยันว่า gate จริง (test ใช้ `httpMock.verify()` กัน request หลุด) — 58/58 test ผ่าน
> - **แก้เพิ่มหลัง P0-35:** พบว่า **CORS ฝั่ง backend ยังไม่รองรับ credentialed request** (ไม่มี `AllowCredentials()`, origin เป็น wildcard) ทำให้ browser จะปฏิเสธ refresh cookie จริงถ้าทดสอบ end-to-end — แก้แล้ว: เพิ่ม `AllowCredentials()` เฉพาะ branch ที่มี origin ระบุชัด (ไม่รวมกับ wildcard เพราะ CORS spec ห้ามรวมกัน) + ตั้ง `Cors:AllowedOrigins: ["http://localhost:4200"]` ใน `appsettings.Development.json` (ตรงกับ Angular dev port และ `ConfirmEmailUrl`/`ResetPasswordUrl` ที่ตั้งไว้อยู่แล้ว) production origin ยังไม่ตั้ง (ยังไม่มี domain จริง) — จะยัง wildcard-ไม่มี-credential จนกว่าจะตั้งค่า
> - **P0-33 เสร็จ + verify ผ่านแล้ว (live browser จริงทุกจุด ไม่ใช่แค่ unit test):** Layouts จริงสำหรับ `PublicLayout` (ตัวเดียวที่มีเนื้อหาจริงตอนนี้ — home + auth 6 หน้า) — header (sticky, wordmark, nav, auth-aware Login/Register ↔ Devices/Sign-out, ปุ่ม TH/EN), footer, mobile nav (drawer เลื่อนจากขวา), skip-to-content, focus-on-route-change ครบตาม acceptance `P0-33` `learn`/`instructor`/`admin` layout **ยังเป็น placeholder เหมือนเดิมตั้งใจ** (ยังไม่มี feature ให้ใส่ nav จริง จะเป็นของปลอมถ้าทำตอนนี้) แก้ landmark `<main>` ซ้อนกันด้วย: ทั้ง 7 หน้าเปลี่ยน root จาก `<main>` → `<div>` เพราะ `PublicLayout` เป็นเจ้าของ `<main id="main-content">` จุดเดียวแล้ว (เช็ค 3 spec ที่มีอยู่แล้วว่าไม่ query ด้วย `role="main"` เลย ไม่กระทบ) `MobileNav` mirror pattern `CdkTrapFocus`+scroll-lock+Escape ของ `Modal` ตรง ๆ แต่แยกคอมโพเนนต์ ไม่แก้ `Modal` เดิม — **เป็นจุดแรกที่ผูก `AuthService.clearSession()` เข้ากับปุ่ม "ออกจากระบบ" จริง** (มีอยู่แล้วแต่ไม่เคยมี UI เรียก) และเป็นจุดแรกที่มี UI เรียก `TranslationService.setLocale()` (จำกัดแค่ session เดียวตาม TODO เดิมที่ยังไม่ตัดสินใจเรื่อง cookie persistence) หน้าแรกเปลี่ยนจาก scaffold placeholder เป็น Hero section จริง (heading/subheading/ปุ่ม CTA สมัคร-ล็อกอิน) **ไม่ใส่คอร์สแนะนำ/social proof** เพราะเป็น P1-23 ที่ต้องรอ Catalog API จริง (`MASTER.md` เขียนกำกับไว้เองว่า pattern เต็มเป็นจุดเริ่มของ P1 เท่านั้น) — unit test เพิ่ม 12 ตัว (`mobile-nav.spec.ts` 9 + `site-header.spec.ts` 3) รวม 70/70 ผ่าน, lint สะอาด, build สะอาด (initial bundle 98.86KB transfer) — verify เพิ่มด้วย live browser จริง: skip-link (Tab แล้วโผล่ กด/คลิกแล้ว focus ไป `#main-content` จริง), in-app navigate แล้ว focus ย้ายตาม (focus-on-route-change), drawer เปิด/focus-trap เข้า/Escape ปิด+คืน focus ให้ปุ่ม hamburger จริง (ทดสอบซ้ำหลัง fresh restart dev server เพราะรอบแรกเจอ `NG0203` ที่เป็น Vite HMR artifact ชั่วคราว ไม่ใช่บั๊กจริง — build จาก clean state ไม่มี error ยืนยันแล้ว), dark mode ทุกสี (header/footer/ปุ่ม) ตรงกับ `tokens.css` เป๊ะ, SSR raw HTML (curl ตรงจาก `serve:ssr:frontend`) มี header+hero+footer+skip-link จริง ไม่ใช่แค่ hydrate หลังบ้าน
> - **P0-20 + P0-07 เสร็จ (2026-08-18):** Test harness ครบ — `SiriApiFactory` (`WebApplicationFactory<Program>` boot `Program.cs` จริงทั้งใบ) แก้ข้อกังวล config-override timing ที่บันทึกไว้ตอน P0-22 แล้วจริง: override ทุกค่าผ่าน `UseSetting` เท่านั้น (Mvc.Testing ส่งเป็น command-line args → precedence สูงสุด มองเห็นตั้งแต่บรรทัดแรกของ Program.cs — ห้ามเปลี่ยนไปใช้ `ConfigureAppConfiguration` ซึ่ง replay หลัง top-level reads), บังคับ environment `IntegrationTest` (user-secrets โหลดเฉพาะ Development → connection string จริงไม่มีทางอยู่ใน config stack) + fail-fast guard ใน `ConfigureServices` ที่ throw ก่อน ServiceProvider ถูกสร้าง (ก่อนทุก DB connection) ถ้าค่าไม่ตรง Testcontainers เป๊ะ · `TestData/TestUserBuilder` fluent data builder (default: อีเมล unique `@example.test`, confirmed, ไม่มี role — expose `Email`/`Password` ให้เทสต์ HTTP login ต่อได้) · `ApiHostSmokeTests` 5 ตัว (env guard, config guard, `/health`, default-deny 401 ผ่าน pipeline จริง, login ผ่าน HTTP จริง + httpOnly cookie) — integration รวม 53 ตัว บนเครื่องนี้ fail ด้วย `DockerUnavailableException` ล้วนเหมือนเดิม (เช็คแล้ว 53/53 ไม่มี assertion fail แอบ) **แต่ body ของ 5 ตัวใหม่ยังไม่เคยรันจริงจนกว่า CI จะรัน** · CI `.github/workflows/ci.yml` (P0-07) 3 jobs: backend (restore→build Release→unit→arch→integration บน ubuntu ที่มี Docker จริง→`dotnet list package --vulnerable` fail บน High/Critical), frontend (`npm ci`→lint→test→build→`npm audit --audit-level=high`), secret-scan (gitleaks 8.18.4 pinned binary + `fetch-depth: 0` สแกนทั้ง history + `.gitleaks.toml` allowlist เฉพาะ string `CHANGE_ME*` และรหัสผ่านเทสต์ `Correct-Horse-Battery-Staple` — จงใจไม่ allowlist path เทสต์ทั้งโฟลเดอร์ กัน secret จริงหลุดในเทสต์) — pre-flight จากเครื่องนี้ยืนยัน day-one green แล้ว: npm audit 0, NuGet vulnerable 0, gitleaks no leaks (ทั้ง history และ `--no-git` ทั้ง tree), frontend test 70/70 จบเองใน non-TTY, YAML parse ผ่าน — **CI จริงยังไม่เคยรันจนกว่าจะ commit + push (ห้าม agent ทำเอง)**
> - Database ต่อจริงแล้ว (ดูหัวข้อบนนี้) — ยังไม่ rotate รหัสผ่านตามที่คุณเลือกไว้ — **ค้าง:** migration `AddUserMaxConcurrentSessionsOverride` (P0-17) ยัง**ไม่ได้** `dotnet ef database update` ขึ้น DB จริงตามคำสั่งงาน (ห้าม agent รันเอง/ต่อ DB จริง) ต้องรันเองก่อนใช้ per-account override จริง
> - Vendor ปิดครบแล้ว: Bunny Stream · Stripe · Contabo (email ยังไม่ตัดสิน แต่ไม่บล็อกงานเพราะออกแบบเป็น SMTP มาตรฐาน)
> - **Q2/D-14 แก้ไข (2026-08-18):** Payment เปลี่ยนจาก EasySlip เป็น **Stripe เพียงเจ้าเดียว** ตามคำสั่งเจ้าของโปรเจ็ค (ยังไม่มีโค้ด payment จริง จึงแก้แค่เอกสาร+comment ใน stub) — ยืนยันจาก docs จริงแล้วว่า Stripe รองรับ merchant ไทย + PromptPay (THB เท่านั้น, เพดาน 2 ล้านบาท/รายการ, non-recurring, refund เป็น async) เอกสารอัปเดตครบ: `PAYMENT.md` (rewrite), `DECISIONS.md`, `TASKS.md` (P3 ทั้ง phase, est ~52→~38), `ROADMAP.md`, `REQUIREMENTS.md` (LX-06), `DATABASE.md` (ตาราง commerce: ตัด PaymentSlips/MerchantAccounts/PaymentReviewQueue เพิ่ม StripeWebhookEvents/PaymentOpsQueue), `ARCHITECTURE.md`, `PLAN.md`, `DEPLOYMENT.md`
>
> **งานถัดไป:** ที่เหลือใน Phase 0: `P0-01`/`P0-02`/`P0-04`/`P0-05`/`P0-06` (infra บน VPS — ผู้ใช้จัดการเอง), `P0-08` (CD pipeline — dep กับ P0-07 ที่เสร็จแล้ว แต่รอ infra พร้อม + ผู้ใช้ต้องตั้ง GitHub secrets/SSH เอง), `P0-03` (สถานะพิเศษ — ใช้ MSSQL ที่มีอยู่แล้วบน Contabo ไม่ใช่ของที่ติดตั้งใหม่ตามสเปค), `P0-13`'s OpenTelemetry (ส่วนอื่นทำแล้ว), `P0-12`'s migration bundle script, `P0-36` (E2E/Playwright) — เช็ค `docs/TASKS.md` ให้ตรงก่อนเริ่ม
>
> **ยังค้าง — ถามก่อนถ้างานแตะเรื่องนี้**
> - SQL Server edition สำหรับ production (ตอนนี้เป็น Developer, ใช้ dev ได้แต่ต้องเปลี่ยนก่อน launch) — ต้องตัดสินก่อนเข้า P7 ดู `DEPLOYMENT.md`
> - จำกัด firewall port 1433 ของ VPS — ต้องมีสิทธิ์ SSH ก่อนถึงจะทำได้
> - Rotate รหัสผ่าน `siriupskill_app` — เสนอไปแล้ว คุณเลือกข้ามก่อน ยังไม่ทำ
> - Hangfire dashboard: P0-22 เพิ่ม role check (Admin/SuperAdmin ผ่าน JWT) เป็นทางเลือกที่สองต่อจาก localhost-only แล้ว — **ยังไม่ปิดสนิท:** คนที่เข้าผ่าน browser ธรรมดาจากเครื่องที่ไม่ใช่ localhost ยังไม่มีทางแนบ bearer token ได้ (ไม่มี admin frontend/login-session UI) ต้องรอกลไก session จริงสำหรับ dashboard ในอนาคต
> - Refresh endpoint ยังไม่มี optimistic-concurrency guard กัน 2 request แข่งกันใช้ refresh token เดิมพร้อมกัน (TOCTOU) — ความเสี่ยงต่ำ, พบระหว่าง verify P0-16, ยังไม่แก้เพราะนอกขอบเขต
> - Redis session-mirror key TTL (P0-17) ตั้งครั้งเดียวตอน login เท่านั้น ไม่ได้ต่ออายุทุกครั้งที่ refresh หมุน token ใหม่ (30 วัน sliding) — ไม่กระทบอะไรตอนนี้เพราะยังไม่มีใครอ่าน Redis กลับมาตัดสินสิทธิ์ (รอ Media/Phase 2 playback token) แต่ต้องแก้ก่อน Phase 2 เริ่มพึ่ง key นี้จริง ดู `ISessionRegistry`'s doc comment
> - สมัครบัญชี **Stripe Thailand** + เปิด PromptPay ใน dashboard + เก็บ key เข้า user-secrets — เจ้าของโปรเจ็คต้องทำเอง ควรเริ่มตั้งแต่ P1 (KYC ใช้เวลา) ต้องเสร็จก่อน P3
> - `Q4` สูตร revenue split + รอบ payout — ต้องตอบก่อน `P6-03` (ต้องรวมค่าธรรมเนียม Stripe ในสูตรด้วย — เช็คเรตจริงตอนเปิดบัญชี)
> - `Q5` ขนาดทีมจริง — timeline 28 สัปดาห์คิดจาก BE2+FE2+QA1
> - `P2-01` Bunny รองรับ FairPlay (iOS) หรือไม่ — ต้องเช็คตั้งแต่สัปดาห์ที่ 4
> - Angular เวอร์ชันที่ลงจริงคือ core `22.1.2` (CLI `22.1.4`) ต่างจากตัวเลขเดิมในเอกสารเล็กน้อย ไม่กระทบอะไร
>
> _อัปเดตบล็อกนี้ทุกครั้งที่จบ phase_
