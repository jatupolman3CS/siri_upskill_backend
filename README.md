# SIRI UpSkill — Backend

Backend ของ SIRI UpSkill (E-Learning marketplace): ASP.NET Core Web API บน .NET 10, EF Core 10 + SQL Server, Redis, Hangfire, Bunny Stream (video/DRM), Stripe (PromptPay)

Frontend อยู่คนละ repo: <https://gitlab.com/Jatuphon.khotsopha/siri_upskill_ui>

## โครงสร้าง

```
src/
  Siri.Api/                  composition root + endpoint host (Minimal API)
  Siri.SharedKernel/         primitive ที่ใช้ร่วมกันทุกโมดูล
  Siri.Persistence/          AppDbContext, migration, interceptor, Redis
  Siri.Modules.*/            Identity, Catalog, Notification (vertical slice)
                             Commerce, Media, Learning, Payout, Cms, Community, Analytics (repository + service)
  Siri.Integrations.Video/   Bunny Stream หลัง IVideoProvider
  Siri.Integrations.Payment/ Stripe
tests/
  Siri.UnitTests/            domain logic, pricing, การตัดสินสิทธิ์
  Siri.ArchitectureTests/    บังคับ module boundary
  Siri.IntegrationTests/     Testcontainers (MSSQL + Redis จริง)
scripts/migrate-bundle.sh    สร้าง migration bundle สำหรับ deploy
docs/                        เอกสารทั้งโปรเจ็ค (ต้นฉบับ — siri_upskill_ui มีสำเนา)
```

## เริ่มพัฒนา

ต้องมี .NET 10 SDK และตั้ง connection string ผ่าน user-secrets ก่อน (ห้ามเขียนลง `appsettings*.json`)

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<connection string>" --project src/Siri.Api
```

```bash
dotnet build SiriUpSkill.sln
```

```bash
dotnet test SiriUpSkill.sln
```

```bash
dotnet run --project src/Siri.Api --launch-profile http
```

API ขึ้นที่ `http://localhost:5190` (OpenAPI: `/openapi/v1.json`) — frontend dev server ต่อผ่าน proxy มาที่ port นี้

> integration test ใช้ Testcontainers จึงต้องมี Docker — เครื่องที่ไม่มี Docker จะ fail ทั้งชุดด้วย `DockerUnavailableException` เป็นเรื่องปกติ ให้พึ่ง CI แทน

## เอกสาร

เริ่มที่ [`docs/TASKS.md`](docs/TASKS.md) (คิวงาน + สถานะ) และ [`CLAUDE.md`](CLAUDE.md) (กฎการทำงานทั้งหมด) · ประวัติงานที่ทำไปแล้วอยู่ที่ [`docs/PROGRESS.md`](docs/PROGRESS.md)
