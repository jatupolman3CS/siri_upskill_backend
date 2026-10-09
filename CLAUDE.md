# SIRI UpSkill — Backend

E-Learning marketplace (แนว SkillLane / FutureSkill): ผู้เรียนซื้อคอร์สวิดีโอเข้าถึงระยะยาว, instructor สร้างคอร์สเอง, platform หักส่วนแบ่งรายได้ ความปลอดภัยของวิดีโอ (DRM/watermark/กันแชร์บัญชี) เป็น **critical requirement** ไม่ใช่ของแถม

## ⚠️ Repo นี้แยกจาก frontend แล้ว (2026-08-25)

| | repo | โฟลเดอร์บนเครื่อง |
|---|---|---|
| **Backend (repo นี้)** | `https://gitlab.com/Jatuphon.khotsopha/siri_upskill_backend.git` | `C:\ProjectSiriUpSkill\siri_upskill_backend` |
| Frontend | `https://gitlab.com/Jatuphon.khotsopha/siri_upskill_ui.git` | `C:\ProjectSiriUpSkill\siri_upskill_ui` |

- โค้ด backend เดิมอยู่ใต้ `backend/` ของ monorepo **ตอนนี้ย้ายมาอยู่ที่ root ของ repo นี้แล้ว** (`src/`, `tests/`, `SiriUpSkill.sln`)
- **`docs/*.md` เขียนขึ้นตอนยังเป็น monorepo** — path ที่เขียนว่า `backend/src/...` ให้อ่านเป็น `src/...` ใน repo นี้ และ path ที่ขึ้นต้นด้วย `frontend/` คือไฟล์ใน repo `siri_upskill_ui` ไม่ได้อยู่ที่นี่
- `docs/` ที่นี่คือ **ต้นฉบับ** — `siri_upskill_ui` มีสำเนาไว้อ่าน ถ้าแก้เอกสารให้แก้ที่นี่แล้ว copy ไป ห้ามแก้สองที่แล้วปล่อยให้ต่างกัน
- งานที่แตะทั้งสองฝั่ง (เปลี่ยน API contract) ต้องบอก user ให้ชัดว่าต้องไปแก้อะไรที่ repo ไหนต่อ

## Stack (ห้ามเปลี่ยนโดยไม่ถาม)

> ### ✅ ย้ายจาก SQL Server ไป PostgreSQL 17 แล้ว (`P0-41`, 2026-09-05)
> รายละเอียดครบทุกไฟล์อยู่ที่ `docs/contracts/P0-41-postgresql-migration.md`
> - **infra**: container `siri_postgres` บน Contabo · network `siri-net` · DB **`SIRIUPSKILL`** (ICU `th-TH`)
>   · role `siriupskill_app` เป็น owner ไม่ใช่ superuser · bind `127.0.0.1:5432` **ไม่เปิดออกเน็ต**
>   (ต่อจากเครื่อง dev ผ่าน SSH tunnel `-L 15432:127.0.0.1:5432`) · รหัสผ่านอยู่ที่ `/root/.siri-postgres.env` บน VPS
> - **โค้ด**: `UseNpgsql` · Hangfire `UsePostgreSqlStorage` (schema `hangfire` ตัวเล็ก) · `HasFilter` เป็น
>   identifier แบบ quote · `nvarchar(max)`→`text` · `IsRowVersion`→`ConcurrencyTokenInterceptor` (คง `byte[]`
>   ไว้ API contract ไม่เปลี่ยน) · raw SQL quote ครบทุกจุด · migration สร้างใหม่ `InitialCreatePostgres`
> - **ยืนยันแล้วจริง**: build 0 warning/0 error · unit **774/774** · architecture **5/5** · migration
>   apply ผ่านบน PostgreSQL 17 จริง (database ชั่วคราวบน VPS แล้วลบทิ้ง) ได้ครบ 65 ตาราง 10 schema
>   · **ค้นหาภาษาไทยทำงานจริงแล้วเป็นครั้งแรก** (`pg_trgm` — `tsvector` ทดสอบแล้วได้ 0 match ตามคาด)
>   → **`X-7` / `P0-40` (FTS blocker) ตกไปทั้งคู่**
> - **ยังเหลือ (เจ้าของโปรเจ็ค)**: apply migration ใส่ `SIRIUPSKILL` จริง · ตั้ง `ConnectionStrings__Default`
>   รูปแบบ Npgsql ในค่า deploy · เพิ่ม `--network siri-net` ให้ container `siri_upskill_backend`
>   · แก้ `.env.example`/`.env.production` (Claude อ่านไม่ได้ ติด deny rule ของ `.env*`)
> - MSSQL เดิม **ยังรันอยู่ ไม่ได้แตะ** — ปิดได้เมื่อยืนยันว่า PostgreSQL ใช้งานได้ครบ



| Layer | Technology | Version |
|-------|-----------|---------|
| Backend | ASP.NET Core Web API (MVC Controllers — attribute routing, ตัดสินใจแล้ว 2026-09-01 ดู `docs/DECISIONS.md` D-19; ชั้น business logic ยังแยก vertical slice + repository/service เหมือนเดิม ไม่เปลี่ยน) | **.NET 10 LTS** |
| ORM | EF Core (**Npgsql** provider) | **10.x** |
| Database | **PostgreSQL** (collation ICU `th-TH`) | **17** |
| Cache/Session | Redis | 7+ |
| Messaging (opt-in) | **Apache Kafka** (KRaft) — pipeline อีเมล/แจ้งเตือน outbox → Kafka → consumer + Redis (`D-23`, เปิดด้วย `Notification:Delivery:Transport=Kafka`, default ปิด) | 4.x (`apache/kafka:4.2.2`) |
| Jobs | Hangfire (**PostgreSQL** storage, schema `hangfire`) | latest |
| **Video/DRM** | **Bunny Stream** (หลัง `IVideoProvider`) | — |
| **Payment** | **Stripe** (PromptPay QR ผ่าน PaymentIntent + webhook — v1 ยังไม่เปิดบัตร, ไม่มีผ่อน) | — |
| **Hosting** | **Contabo VPS + Docker Compose + Caddy** | — |
| Test | xUnit + Testcontainers | — |
| _(frontend — อยู่อีก repo)_ | _Angular 22.1.x standalone + signals + zoneless, Tailwind v4, Shaka Player_ | — |

**ห้ามใช้:** MediatR (เปลี่ยนเป็น commercial license แล้ว), AutoMapper (ผูก license เช่นกัน — เขียน mapping เอง), Azure Media Services (ปลดระวางแล้ว)

## เอกสารอ้างอิง — อ่านก่อนลงมือ

| ทำงานเรื่อง | อ่านไฟล์ |
|------------|---------|
| **รับ task มาทำ (เริ่มที่นี่)** | **`docs/TASKS.md`** — task ทั้งหมดมี ID เช่น `P0-11` พร้อม dependency, acceptance และคอลัมน์ `Status` |
| ประวัติงานที่ทำไปแล้วโดยละเอียด | `docs/PROGRESS.md` |
| requirement / scope ใด ๆ | `docs/REQUIREMENTS.md` |
| ชำระเงิน, Stripe, PromptPay, webhook | `docs/PAYMENT.md` |
| deploy, Contabo, backup, docker, CI/CD | `docs/DEPLOYMENT.md` |
| โครงสร้าง module, layer, boundary | `docs/ARCHITECTURE.md` |
| ตาราง, migration, index, query | `docs/DATABASE.md` + `.claude/rules/database.md` |
| auth, payment, video, ข้อมูลส่วนบุคคล | `docs/SECURITY.md` + `.claude/rules/security.md` |
| C# / API ใด ๆ | `.claude/rules/backend.md` |
| ลำดับงาน, commit, PR, definition of done | `.claude/rules/workflow.md` |
| เลือก vendor / ตัดสินใจสถาปัตยกรรม | `docs/DECISIONS.md` |
| แผนงาน, phase, ลำดับความสำคัญ | `docs/ROADMAP.md` |
| กติกาส่งงานให้ Antigravity (coding agent อีกตัว) | `docs/ANTIGRAVITY_HANDOFF.md` |
| Angular / TypeScript / หน้าจอ | **อยู่ที่ repo `siri_upskill_ui`** (`.claude/rules/frontend.md`, `ui-design.md`) |

## กฎเหล็ก (ใช้กับทุกงาน)

1. **ภาษา** — คุยกับ user เป็นภาษาไทย; โค้ด, ชื่อตัวแปร, comment, commit message, ชื่อไฟล์ เป็นภาษาอังกฤษเสมอ ข้อความที่ผู้ใช้ปลายทางเห็นต้องอยู่ในไฟล์ i18n ห้าม hardcode ในโค้ด
2. **ห้ามเดา version / API** — ถ้าไม่แน่ใจว่า .NET 10 มี API นั้นจริงไหม ให้ค้นหาหรือเช็คจาก `.csproj` / `Directory.Packages.props` ก่อน **ห้ามแต่ง API ขึ้นมาเอง**
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
dotnet build SiriUpSkill.sln
```
```bash
dotnet test SiriUpSkill.sln
```
```bash
dotnet ef migrations add <Name> --project src/Siri.Persistence --startup-project src/Siri.Api
```
```bash
# สร้าง self-contained migration bundle สำหรับ deploy จริง (P0-12) — ไม่ฝัง connection string,
# ส่งตอนรันจริงผ่าน --connection หรือ env ConnectionStrings__Default (ดู comment ในสคริปต์)
scripts/migrate-bundle.sh
```
```bash
# Seed dev/test accounts (Admin + Learner/Instructor test users) against whatever
# ConnectionStrings:Default currently points at. ต้อง dotnet user-secrets set
# "Identity:Seed:AdminPassword" / "Identity:Seed:TestUserPassword" ก่อน (ค่า default ใน
# appsettings คือ "CHANGE_ME" ตั้งใจให้ไม่ทำงานจนกว่าจะตั้งค่าจริง) — idempotent รันซ้ำได้ปลอดภัย
dotnet run --project src/Siri.Api -- --seed
```

## Database — ต่อจริงแล้ว end-to-end (2026-08-17)

MSSQL รันอยู่บน Contabo VPS จริง (`217.217.253.122,1433`) database `SIRIUPSKILL` collation `Thai_100_CI_AS_SC_UTF8` login แอป `siriupskill_app` (`db_owner` จำกัดแค่ database นี้ ไม่ใช่ `sa`)
**Connection string อยู่ใน `dotnet user-secrets` ของ `Siri.Api` แล้ว** (`UserSecretsId` ผูกไว้ใน `Siri.Api.csproj`) — ดูค่าได้ด้วย `dotnet user-secrets list --project src/Siri.Api` ห้ามเขียนลง `appsettings*.json` หรือไฟล์ใดใน repo เด็ดขาด
⚠️ user-secrets ผูกกับ `UserSecretsId` ไม่ใช่ path ของโฟลเดอร์ — ย้าย repo มาอยู่ที่ `C:\ProjectSiriUpSkill\siri_upskill_backend` แล้ว **ค่าเดิมยังใช้ได้ตามปกติ ไม่ต้องตั้งใหม่**
⚠️ server เป็น **SQL Server Developer edition** (ใช้ dev ได้ ห้ามใช้ production ตาม license) และ **port 1433 เปิดออกอินเทอร์เน็ตสาธารณะอยู่** — รายละเอียดเต็มอยู่ที่ `docs/DEPLOYMENT.md`

## สถานะโปรเจ็คปัจจุบัน

> **แหล่งความจริงเรื่อง "task ไหนเสร็จแล้ว" คือคอลัมน์ `Status` ใน `docs/TASKS.md` เท่านั้น** · บันทึกรายละเอียดของงานที่ทำไปแล้วทั้งหมดอยู่ที่ `docs/PROGRESS.md` (ยกออกมาจากไฟล์นี้ตอนแยก repo 2026-08-25)
>
> สถานะจาก audit 2026-08-24: build เขียว 0 warning/error · unit **539/539** · architecture **4/4** · **integration test ของ 7 โมดูลใหม่ยังเป็น 0 ไฟล์** · integration test ทั้งหมดบนเครื่อง dev นี้ fail ด้วย `DockerUnavailableException` เพราะไม่มี Docker (ปกติ ต้องพึ่ง CI)
> - ไม่เหลือ `NotImplementedException` ในโมดูลใหม่แล้ว — **ห้ามเชื่อ doc comment ที่ยังเขียนว่าตัวเองเป็น stub อ่าน method body จริงเสมอ**
> - รูใหญ่ที่รู้แล้ว: `P2-20` (ยังไม่มี Shaka Player — งานฝั่ง UI), `P6-03` (hardcode 70/30 ทั้งที่ Q4 ยังไม่ตอบ), `P6-04` (payout batch ไม่เคยรวมยอด) — รายละเอียดครบใน `docs/TASKS.md` § "สถานะจริงของงานที่ยังไม่ปิด"
> - กติกาสองเอเจนต์ (1 task = 1 commit · `DONE` ตั้งได้โดย Claude Code เท่านั้น · ห้ามเดาค่าที่ `DECISIONS.md` ยังไม่ปิด) อยู่ที่ `docs/ANTIGRAVITY_HANDOFF.md` §2 · คิวงานถัดไปอยู่ §4

**ค้างอยู่ ต้องถามก่อนถ้างานแตะเรื่องนี้** (รายละเอียดเต็มท้าย `docs/PROGRESS.md`)
- **FTS** — SQL Server บน Contabo ไม่ได้ติดตั้ง Full-Text Search component (error 7609) migration `AddCourseFullTextIndex` ยัง pending และอยู่ **ก่อน** migration ของ P2–P6 ทำให้ schema ทั้งชุดลง DB จริงไม่ได้เลย เจ้าของโปรเจ็คเลือกว่าจะติดตั้งเอง
- **Q4** สูตร revenue split + รอบ payout — ต้องตอบก่อน `P6-03`
- **Q8** bar ของ anti-piracy เทียบกับ MediaCage Basic
- `DataProtection:EncryptionKeyBase64` ยังเป็น dev placeholder — ต้องตั้งค่าจริงผ่าน user-secrets/env ก่อน production
- SQL Server edition สำหรับ production, จำกัด firewall port 1433, rotate รหัสผ่าน `siriupskill_app` — งาน infra ของเจ้าของโปรเจ็ค
