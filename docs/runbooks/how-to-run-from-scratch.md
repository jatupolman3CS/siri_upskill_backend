# คู่มือการรันระบบ SIRI UpSkill จากศูนย์ (How to Run From Scratch)

คู่มือนี้สรุปขั้นตอนทั้งหมดในการเตรียม Environment, ฐานข้อมูล, Backend (.NET 10), Frontend (Angular 22 SSR), และการทดสอบระบบทั้งหมดตั้งแต่เริ่มต้น

---

## 1. ข้อกำหนดเบื้องต้น (Prerequisites)

- **.NET SDK**: 10.0+ (`dotnet --version`)
- **Node.js**: 22+ & npm 10+ (`node -v`, `npm -v`)
- **Docker & Docker Compose** (สำหรับ MSSQL 2022 และ Redis 7)
- **Git**

---

## 2. โคลนโปรเจ็คและการเตรียม Environment

```bash
git clone <repository-url>
cd ProjectSiriUpSkill
```

### 2.1 รันบริการ Infrastructure (SQL Server 2022 + Redis)

สามารถรันผ่าน Docker Compose ในเครื่อง local:

```bash
# รัน SQL Server (port 1433) และ Redis (port 6379)
docker compose up -d mssql redis
```

> **หมายเหตุ Collation**: ฐานข้อมูลใช้ `Thai_100_CI_AS_SC_UTF8` ตามสถาปัตยกรรมระบบ

---

## 3. การเตรียมความลับและการตั้งค่า (User Secrets)

เข้าไปที่โปรเจ็ค `backend/src/Siri.Api` เพื่อตั้งค่ารหัสผ่านเริ่มต้นสำหรับระบบ Seeder:

```bash
cd backend/src/Siri.Api

# ตั้งค่ารหัสผ่านผู้ดูแลระบบ (Admin) และผู้ใช้ทดสอบ
dotnet user-secrets set "Seed:AdminPassword" "YourSecureAdminPassword123!"
dotnet user-secrets set "Seed:TestUserPassword" "YourSecureTestUserPassword123!"

# (ตัวเลือก) ตั้งค่าคีย์ Stripe และ Video Provider สำหรับทดสอบ
dotnet user-secrets set "Payment:Stripe:SecretKey" "sk_test_placeholder"
dotnet user-secrets set "Payment:Stripe:WebhookSecret" "whsec_placeholder"
dotnet user-secrets set "VideoProvider:Bunny:ApiKey" "bunny_test_api_key"
```

---

## 4. รัน Database Migration และ Seeding

### 4.1 ตรวจสอบและอัปเดต Migration

```bash
# จาก root ของ repository
dotnet ef database update --project backend/src/Siri.Persistence --startup-project backend/src/Siri.Api
```

### 4.2 ทำการ Seed ข้อมูลตัวอย่าง (Sample Catalog & Accounts)

```bash
# รัน Seeder ผ่าน CLI argument
dotnet run --project backend/src/Siri.Api -- --seed
```

> ข้อมูลที่ถูก Seed:
> - หมวดหมู่หลัก 3 หมวด (การตลาดและธุรกิจ, โปรแกรมมิ่งและเทคโนโลยี, การเงินและการลงทุน)
> - ผู้สอนตัวอย่าง 5 คน
> - คอร์สเรียนตัวอย่าง 20 คอร์ส (พร้อม Section, Episode, Free preview)
> - บัญชีทดสอบ Admin, Instructor, Student

---

## 5. การรัน Backend API

```bash
# รัน API host (รันบน https://localhost:5001 หรือ http://localhost:5000)
dotnet run --project backend/src/Siri.Api
```

- **Health Check**: `GET http://localhost:5000/health`
- **Hangfire Dashboard**: `http://localhost:5000/hangfire` (อนุญาตเฉพาะ Localhost หรือ Admin token)
- **OpenAPI / Swagger (Dev Mode)**: `http://localhost:5000/openapi/v1.json`

---

## 6. การรัน Frontend (Angular 22 SSR)

เปิด Terminal ใหม่:

```bash
cd frontend

# ติดตั้ง Dependencies
npm ci

# รัน Development Server (ต่อ Proxy ไปยัง Backend port 5000 อัตโนมัติ)
npm start
```

เปิดเว็บเบราว์เซอร์ที่: **`http://localhost:4200`**

---

## 7. คำสั่งการทดสอบ (Automated Verification)

### 7.1 Backend Unit Tests & Architecture Tests
```bash
# รัน Unit tests (703 tests)
dotnet test backend/tests/Siri.UnitTests/Siri.UnitTests.csproj

# รัน Architecture boundary tests (4 tests)
dotnet test backend/tests/Siri.ArchitectureTests/Siri.ArchitectureTests.csproj
```

### 7.2 Integration Tests (Testcontainers)
```bash
# รัน Integration tests (ต้องการ Docker ทำงานอยู่)
dotnet test backend/tests/Siri.IntegrationTests/Siri.IntegrationTests.csproj
```

### 7.3 Frontend Production Build & E2E Tests
```bash
# ตรวจสอบ Build สำหรับ Production (0 errors)
npm --prefix frontend run build

# รัน Playwright E2E suites
npx playwright test --config=frontend/playwright.config.ts
```
