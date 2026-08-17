# SIRI UpSkill — Master Plan

> จุดเริ่มต้นของเอกสารทั้งหมด อ่านไฟล์นี้ก่อน แล้วค่อยเจาะไฟล์ที่เกี่ยวข้อง

## 1. สรุปโปรเจ็ค

**SIRI UpSkill** = E-Learning marketplace สำหรับตลาดไทย ผู้เรียนซื้อคอร์สวิดีโอแล้วเข้าถึงได้ระยะยาว
instructor สร้างและขายคอร์สเอง platform หักส่วนแบ่งรายได้ (default 70/30)

**สิ่งที่ทำให้โปรเจ็คนี้ไม่ใช่เว็บขายคอร์สธรรมดา** — เอกสารต้นทางระบุ security เป็น *CRITICAL INFRASTRUCTURE*:
DRM จริง + dynamic watermark + จำกัดการล็อกอินพร้อมกัน สามข้อนี้กำหนดสถาปัตยกรรมตั้งแต่วันแรก
(ต้องมี license proxy, playback session, Redis, และ provider วิดีโอที่รองรับ DRM) — ใส่ทีหลังไม่ได้โดยไม่รื้อ

## 2. Stack

```
Angular 22.1.4 (SSR + signals + zoneless) + Tailwind v4 + Shaka Player
        ↕ REST/JSON (JWT)
ASP.NET Core 10 (Modular Monolith, Minimal API, Vertical Slice)
        ↕
SQL Server 2022 (EF Core 10) + Redis + Hangfire + Blob/CDN
        ↕
External: Video DRM provider · Payment gateway (PromptPay/Card/ผ่อน) · Email
```
เหตุผลของแต่ละตัวเลือกอยู่ใน `docs/DECISIONS.md`

## 3. โครงสร้าง repo

```
ProjectSiriUpSkill/
├── CLAUDE.md               ← instruction หลักสำหรับ Claude Code
├── .claude/
│   ├── settings.json       ← permission (คำสั่งไหนรันได้เอง / ต้องถาม / ห้าม)
│   └── rules/
│       ├── backend.md      ← กฎ C# / API
│       ├── frontend.md     ← กฎ Angular / TypeScript
│       ├── database.md     ← กฎ EF Core / MSSQL / migration
│       ├── security.md     ← กฎความปลอดภัย (บังคับ)
│       ├── ui-design.md    ← กฎ UI/UX + วิธีใช้ skill ui-ux-pro-max
│       └── workflow.md     ← ลำดับงาน, definition of done, git
├── docs/
│   ├── PLAN.md             ← ไฟล์นี้
│   ├── REQUIREMENTS.md     ← requirement + traceability จาก xlsx/pdf
│   ├── ARCHITECTURE.md     ← โครงสร้างระบบ, module, video pipeline
│   ├── DATABASE.md         ← schema ทุกตาราง + index
│   ├── SECURITY.md         ← baseline ความปลอดภัย + checklist ก่อน launch
│   ├── ROADMAP.md          ← 8 phase, timeline, exit criteria, ความเสี่ยง
│   ├── TASKS.md            ← ★ backlog ระดับปฏิบัติ ~120 task พร้อม ID/dependency
│   ├── PAYMENT.md          ← PromptPay QR + EasySlip flow และความเสี่ยง
│   ├── DEPLOYMENT.md       ← Contabo, docker, backup, license MSSQL
│   ├── DECISIONS.md        ← ตัดสินแล้ว / ยังต้องตัดสิน (Q1–Q5)
│   └── adr/                ← บันทึกการตัดสินใจสถาปัตยกรรมรายเรื่อง
├── backend/                ← (ยังไม่สร้าง) SiriUpSkill.sln
├── frontend/               ← (ยังไม่สร้าง) Angular workspace
├── db/                     ← seed script, ER diagram
└── infra/                  ← Aspire AppHost, docker-compose, CI/CD
```

## 4. Module ของระบบ (11 bounded contexts)

| Module | ขอบเขต | Requirement |
|--------|--------|-------------|
| Identity | สมัคร/ล็อกอิน, role, session & device limit | SE-03 |
| Catalog | category tree, course, section, episode, instructor profile, review, search | LX-01, LX-02 |
| Media | upload, transcode, DRM, playback token, watermark payload | LX-03, SE-01, SE-02 |
| Learning | enrollment, progress, quiz, assignment, certificate | LX-04, LX-05 |
| Commerce | cart, order, payment, promo, bundle, flash sale, ผ่อนชำระ | LX-06, AD-02 |
| Payout | revenue split, payout batch, บัญชี instructor | AD-03 |
| Cms | banner, menu, blog, SEO, redirect | AD-01 |
| Community | Q&A ใต้ episode, moderation | LX-07 |
| Notification | in-app, email outbox, announcement | IN-03 |
| Analytics | ยอดขาย, นักเรียน active, drop-off | IN-02 |
| Admin | user/course moderation, refund, audit | — |

## 5. ลำดับการทำงาน

ดู `docs/ROADMAP.md` ฉบับเต็ม — สรุป:

```
P0 Foundation+Identity (wk1-3)  →  P1 Catalog+SEO (4-7)  →  P2 Media+DRM (8-11) 🔴
   →  P3 Commerce (12-15) 🔴  →  P4 Instructor Studio (16-19)
   →  P5 Interactive Learning (20-22)  →  P6 Admin/CMS/Revenue (23-26)
   →  P7 Hardening+Launch (27-29)
```
🔴 = phase ที่ต้องปิด vendor decision ก่อน (Q1 video/DRM, Q2 payment)
**MVP ขายได้จริงที่ปลาย P3 (~สัปดาห์ 15)**

## 6. สถานะการตัดสินใจ

✅ **ปิดแล้ว** — Q1 Bunny Stream · Q2 PromptPay+EasySlip · Q3 Contabo VPS
🔴 **ยังค้าง** — SQL Server edition (P0-03) · Q4 สูตร revenue split (ก่อน P6) · Q5 ขนาดทีมจริง
รายละเอียดใน `docs/DECISIONS.md`

## 7. ก้าวถัดไป

1. ตัดสิน SQL Server edition (Express มีเพดาน 10 GB/database — ดู `DEPLOYMENT.md`)
2. เริ่ม `P0-01` (เตรียม Contabo VPS) และ `P0-10` (scaffold solution) — ทำขนานกันได้
3. ลง Python เพื่อให้ skill `ui-ux-pro-max` ใช้ search ได้ แล้วทำ `P0-31` design system ให้นิ่งก่อนเริ่มหน้าจอ
4. เริ่ม `P2-01` (ถาม Bunny เรื่อง DRM/FairPlay) และ `P3-04` (สมัคร EasySlip + ทดสอบด้วยสลิปจริง) ล่วงหน้าตั้งแต่ตอนนี้ — คำตอบมีผลกับแผน
