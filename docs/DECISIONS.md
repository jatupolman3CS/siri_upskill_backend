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
| **D-14** | **Payment** | **PromptPay QR + ตรวจสลิปผ่าน EasySlip** (Q2 — ตัดสินแล้ว) | เริ่มได้ทันทีไม่ต้องรอ onboarding gateway — **แลกกับการยังไม่มีบัตรเครดิตและผ่อนชำระ** ดู `PAYMENT.md` |
| **D-15** | **Hosting** | **Contabo VPS + Docker Compose** (Q3 — ตัดสินแล้ว) | มีเครื่องอยู่แล้ว, ต้นทุนคงที่ — แลกกับงาน ops ที่ต้องดูแลเอง ดู `DEPLOYMENT.md` |

## B. ปิดแล้ว — Q1 / Q2 / Q3 (2026-08-17)

### ✅ Q1 — Video/DRM = **Bunny Stream**
implement หลัง `IVideoProvider` → สลับเจ้าได้ถ้าจำเป็น
**ต้องเช็คกับ Bunny ก่อนเริ่ม P2 (งาน P2-01):**
- แพ็กเกจไหนเปิด DRM (MediaCage) ได้ และคิดเงินอย่างไร — DRM ไม่ได้อยู่ในแพ็กเกจพื้นฐานทุกระดับ
- รองรับ **FairPlay** (Safari/iOS) ด้วยหรือมีแค่ Widevine — ถ้ามีแค่ Widevine ผู้ใช้ iOS จะไม่ได้ DRM
  → fallback: token-authenticated HLS + watermark + จำกัด session สำหรับ iOS แล้วสื่อสารความเสี่ยงนี้ให้ชัด
- Token authentication key, allowed referrer, geo-blocking, direct-play ปิดหรือยัง

### ✅ Q2 — Payment = **PromptPay QR + EasySlip** (ไปก่อน)
ดูรายละเอียดเต็มที่ `PAYMENT.md`
⚠️ **ผลกระทบที่ต้องรับรู้:** LX-06 ระบุว่าต้องมีบัตรเครดิต + ผ่อนชำระด้วย — v1 จะยังไม่มีทั้งสองอย่าง
ออกแบบ `IPaymentMethod` ให้เสียบ gateway ทีหลังได้ **เกณฑ์ย้าย:** ออร์เดอร์ > 300/เดือน หรือขายคอร์ส > 5,000 บาท หรือ manual review > 10%

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

## C. ADR log
บันทึกการตัดสินใจสถาปัตยกรรมใหม่ทุกครั้งที่ `docs/adr/NNNN-title.md` (format: Context / Decision / Consequences)
