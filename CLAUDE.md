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
| **Payment** | **PromptPay QR + EasySlip** (ตรวจสลิป — ยังไม่มีบัตร/ผ่อน) | — |
| **Hosting** | **Contabo VPS + Docker Compose + Caddy** | — |
| Test | xUnit + Testcontainers / Vitest + Playwright | — |

**ห้ามใช้:** MediatR (เปลี่ยนเป็น commercial license แล้ว), AutoMapper (ผูก license เช่นกัน — เขียน mapping เอง), NgModule, zone.js pattern, Azure Media Services (ปลดระวางแล้ว)

## เอกสารอ้างอิง — อ่านก่อนลงมือ

| ทำงานเรื่อง | อ่านไฟล์ |
|------------|---------|
| **รับ task มาทำ (เริ่มที่นี่)** | **`docs/TASKS.md`** — task ทั้งหมดมี ID เช่น `P0-11` พร้อม dependency และ acceptance |
| requirement / scope ใด ๆ | `docs/REQUIREMENTS.md` |
| ชำระเงิน, สลิป, EasySlip, PromptPay | `docs/PAYMENT.md` |
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

## Database — ต่อจริงแล้ว end-to-end (2026-08-17)

MSSQL รันอยู่บน Contabo VPS จริง (`217.217.253.122,1433`) database `SIRIUPSKILL` collation `Thai_100_CI_AS_SC_UTF8` login แอป `siriupskill_app` (`db_owner` จำกัดแค่ database นี้ ไม่ใช่ `sa`)
**Connection string อยู่ใน `dotnet user-secrets` ของ `Siri.Api` แล้ว** (`UserSecretsId` ผูกไว้ใน `Siri.Api.csproj`) — ดูค่าได้ด้วย `dotnet user-secrets list --project backend/src/Siri.Api` ห้ามเขียนลง `appsettings*.json` หรือไฟล์ใดใน repo เด็ดขาด
ทดสอบแล้วจริงด้วย `dotnet ef migrations add` + `dotnet ef database update` ผ่าน `siriupskill_app` สำเร็จ (มีตาราง `__EFMigrationsHistory` ใน DB แล้ว)
⚠️ server เป็น **SQL Server Developer edition** (ใช้ dev ได้ ห้ามใช้ production ตาม license) และ **port 1433 เปิดออกอินเทอร์เน็ตสาธารณะอยู่** — รายละเอียดเต็มอยู่ที่ `docs/DEPLOYMENT.md`

## สถานะโปรเจ็คปัจจุบัน

> **Phase 0 — scaffold หลักเสร็จแล้ว ตรวจสอบซ้ำผ่านแล้ว**
> - Backend: `backend/SiriUpSkill.sln` 21 projects (SharedKernel, Persistence, Api, Workers, module stub 10 ตัว, integration interface 4 ตัว, test project 3 ตัว) build สะอาด 0 warning/error, unit+architecture test ผ่าน 9/9 (integration test ข้ามเพราะเครื่อง dev ไม่มี Docker)
> - Frontend: `frontend/` Angular 22.1 zoneless + SSR + Tailwind v4 build/lint สะอาด ยืนยัน SSR render จริงแล้ว (curl เห็น HTML ภาษาไทยจริง)
> - Database ต่อจริงแล้ว (ดูหัวข้อบนนี้)
> - Vendor ปิดครบแล้ว: Bunny Stream · EasySlip · Contabo
> - ยังไม่มี entity/migration จริง, ยังไม่มี business logic ใด ๆ — เป็นแค่ skeleton
>
> **งานถัดไปที่สมเหตุสมผล:** เริ่ม Identity domain จริง (`P0-14` เป็นต้นไปใน `docs/TASKS.md`) เพื่อได้ entity ตัวแรกและ migration ที่มีความหมายจริง
>
> **ยังค้าง — ถามก่อนถ้างานแตะเรื่องนี้**
> - SQL Server edition สำหรับ production (ตอนนี้เป็น Developer, ใช้ dev ได้แต่ต้องเปลี่ยนก่อน launch) — ต้องตัดสินก่อนเข้า P7 ดู `DEPLOYMENT.md`
> - จำกัด firewall port 1433 ของ VPS — ต้องมีสิทธิ์ SSH ก่อนถึงจะทำได้
> - `Q4` สูตร revenue split + รอบ payout — ต้องตอบก่อน `P6-03`
> - `Q5` ขนาดทีมจริง — timeline 28 สัปดาห์คิดจาก BE2+FE2+QA1
> - `P2-01` Bunny รองรับ FairPlay (iOS) หรือไม่ — ต้องเช็คตั้งแต่สัปดาห์ที่ 4
> - Angular เวอร์ชันที่ลงจริงคือ core `22.1.2` (CLI `22.1.4`) ต่างจากตัวเลขเดิมในเอกสารเล็กน้อย ไม่กระทบอะไร
>
> _อัปเดตบล็อกนี้ทุกครั้งที่จบ phase_
