# SIRI UpSkill — Delivery Roadmap

**สมมติฐาน:** BE 2 + FE 2 + QA 1 เต็มเวลา (ถ้าทีมเล็กกว่านี้ ยืด timeline ตามสัดส่วน)
**รวม ~28 สัปดาห์** ถึง production launch — MVP ขายได้จริงที่ปลาย Phase 3 (~สัปดาห์ที่ 14)
task ระดับปฏิบัติทั้งหมดอยู่ที่ **`docs/TASKS.md`**

| Phase | ชื่อ | สัปดาห์ | Requirement ที่ปิด |
|-------|-----|--------|-------------------|
| P0 | Foundation & Infrastructure | 1–3 | SE-03 |
| P1 | Catalog, Search, SEO | 4–7 | LX-01, LX-02 |
| P2 | Media Pipeline & Secure Player | 8–11 | LX-03, SE-01, SE-02 |
| P3 | Commerce & EasySlip Payment | 12–14 | LX-06 (บางส่วน), AD-02 (บางส่วน) |
| P4 | Instructor Studio | 15–18 | IN-01, IN-02 |
| P5 | Interactive Learning | 19–21 | LX-04, LX-05, LX-07 |
| P6 | Admin, CMS, Revenue | 22–25 | AD-01, AD-02, AD-03, IN-03 |
| P7 | Hardening & Launch | 26–28 | — |

> P3 สั้นลง 1 สัปดาห์เพราะ EasySlip ไม่ต้องรอ onboarding gateway — แต่แลกกับการที่ v1 **ยังไม่มีบัตรเครดิตและผ่อนชำระ**
> P0 หนักขึ้นเพราะ self-host บน Contabo (ต้องทำ infra, backup, CI/CD เอง)

---

## P0 — Foundation & Identity (สัปดาห์ 1–3)
- Monorepo + solution structure + Aspire AppHost (MSSQL + Redis ใน docker)
- CI pipeline: build / test / architecture test / migration bundle
- `Siri.SharedKernel`: `Result<T>`, error catalog, `IClock`, `IUserContext`, audit interceptor
- Identity: register / login / email confirm / forgot password / refresh rotation
- **SE-03 Concurrent login control** + device management page
- Angular shell: layout, routing, auth interceptor, error handling, design token, Tailwind setup
- Design system เริ่มต้น (สี, typography, spacing, component หลัก) — ใช้ skill `ui-ux-pro-max`
- **Exit criteria:** login ได้ 2 อุปกรณ์ อุปกรณ์ที่ 3 เตะเครื่องเก่าออกอัตโนมัติ + CI เขียว

## P1 — Catalog, Search & SEO (สัปดาห์ 4–7)
- Catalog module: Category (tree), Course, Section, Episode, InstructorProfile
- **LX-01** search + filter (category / level / price / rating) + Mega Menu + facet count
- **LX-02** หน้า course detail: syllabus, duration รวม, free preview badge, learning outcomes
- Angular SSR + hydration + meta tag/JSON-LD (`Course`, `BreadcrumbList`) + sitemap.xml
- Seed data: 3 category tree, 20 คอร์สตัวอย่าง
- **Exit criteria:** Lighthouse SEO ≥ 95, LCP < 2.5s บน 4G, search p95 < 300ms

## P2 — Media Pipeline & Secure Player (สัปดาห์ 8–11) 🔴 ความเสี่ยงสูงสุด
> ใช้ **Bunny Stream** — แต่ต้องยืนยันกับ Bunny ตั้งแต่สัปดาห์ที่ 4 ว่าแพ็กเกจไหนเปิด DRM ได้ และมี **FairPlay สำหรับ iOS** หรือไม่ (งาน P2-01) คำตอบอาจเปลี่ยนแผนทั้ง phase

- `IVideoProvider` + Bunny Stream adapter + direct upload + webhook
- Playback session API (ตรวจ enrollment + device limit) + DRM license proxy
- **LX-03** Shaka Player: speed 0.5x–2.0x, ABR, resume playback, keyboard shortcut, caption
- **SE-02** dynamic watermark overlay (ชื่อ/อีเมล, สุ่มตำแหน่งทุก ~8 วิ)
- Progress heartbeat → `EpisodeProgress` + `WatchEvents`
- **Exit criteria:** ผ่าน pen-test เบื้องต้น — IDM / video downloader extension / `curl` manifest ดึงไฟล์ไม่ได้; ทดสอบครบ Chrome / Edge / Safari(macOS+iOS) / Firefox / Android

## P3 — Commerce & EasySlip Payment (สัปดาห์ 12–14)
> สเปคเต็ม: `PAYMENT.md` — v1 ใช้ **PromptPay QR + ตรวจสลิปอัตโนมัติ** ยังไม่มีบัตร/ผ่อน

- Cart / Order / pricing engine ที่ server + `IPaymentMethod` (เผื่อเสียบ gateway ทีหลัง)
- สร้าง PromptPay QR เอง (EMVCo + CRC16) — ไม่ต้องพึ่ง API ภายนอก
- อัปสลิป → EasySlip verify → ตรวจ 5 กฎ → auto-enroll
- Manual review queue เมื่อไม่ผ่าน / provider ล่ม / โควตาหมด
- Promo code (**AD-02** ส่วนแรก) + reconcile job รายวัน + หน้า my-courses
- **Exit criteria:** ซื้อ→โอน→อัปสลิป→เข้าเรียนอัตโนมัติสำเร็จ, อัปสลิปเดิมซ้ำถูกปฏิเสธ 100%, ไม่มีออร์เดอร์ค้างสถานะ

## P4 — Instructor Studio (สัปดาห์ 16–19)
- **IN-01** drag-and-drop course builder (Angular CDK DragDrop), reorder section/episode, ตั้ง free preview, บันทึกแบบ optimistic + autosave
- Upload video พร้อม progress + resumable
- Course submit → review → publish workflow
- **IN-02** analytics: ยอดขาย, นักเรียน active, drop-off chart (จาก `analytics.EpisodeDropOff`)
- **Exit criteria:** instructor สร้างคอร์ส 10 ตอนจนขึ้นขายได้ โดยไม่ต้องให้ dev ช่วย

## P5 — Interactive Learning (สัปดาห์ 20–22)
- **LX-04** attachment (PDF/zip) + quiz ท้ายบท + assignment submission/grading
- **LX-05** progress % + certificate PDF (QuestPDF) + หน้า verify certificate สาธารณะ
- **LX-07** Q&A ใต้ episode + instructor answer badge + report/moderation
- **Exit criteria:** เรียนจบคอร์สแล้วได้ certificate อัตโนมัติ ตรวจสอบย้อนกลับได้ด้วย verify code

## P6 — Admin, CMS & Revenue (สัปดาห์ 23–26)
- **AD-01** CMS: banner, menu, blog + SEO field + redirect (ไม่ต้อง deploy)
- **AD-02** ครบ: Flash Sale ตั้งเวลา + Bundle
- **AD-03** revenue split + payout batch + รายงาน + export
- **IN-03** announcement + bulk email (outbox + retry)
- Admin: user management, course moderation, refund, audit log
- **Exit criteria:** ทีมการตลาดจัดโปรหน้าแรกได้เองทั้งหมด, ปิดยอด payout เดือนแรกได้

## P7 — Hardening & Launch (สัปดาห์ 27–29)
- Load test (k6): 5,000 concurrent viewer, 500 checkout/นาที
- Security review: OWASP Top 10, dependency scan, secret scan, pen-test ภายนอก
- PDPA: consent, privacy policy, ขอ export/ลบข้อมูล, data retention
- Observability: dashboard, alert, runbook, on-call
- Backup / restore drill (ซ้อมกู้จริง 1 ครั้ง), DR plan
- Beta ปิดกับผู้ใช้จริง 50 คน → แก้ไข → launch

---

## เส้นทางเร่ง (ถ้าต้องออกตลาดเร็ว)
ตัดเหลือเฉพาะ Must-Have: **P0 → P1 → P2 → P3** = ~14 สัปดาห์ ขายคอร์สได้จริง
เลื่อน P4 ออกไปโดยให้ทีมงานภายในเป็นคนอัปโหลดคอร์สให้ instructor ชั่วคราว (admin-assisted onboarding)

## ความเสี่ยงหลัก
| ความเสี่ยง | ผลกระทบ | การรับมือ |
|-----------|---------|----------|
| **Bunny ไม่มี FairPlay ในแพ็กเกจที่ซื้อ** | ผู้ใช้ iOS/Safari ไม่ได้ DRM (สัดส่วนผู้ใช้ iOS ไทยสูง) | เช็คตั้งแต่สัปดาห์ 4 (P2-01) · fallback = token-auth HLS + watermark + จำกัด session แล้วสื่อสารความเสี่ยงให้ชัด |
| **ไม่มีบัตรเครดิต/ผ่อนชำระใน v1** | คอร์สราคาสูงขายยาก, conversion ต่ำกว่าคู่แข่ง | ตั้งเกณฑ์ชัดว่าเมื่อไหร่จะเปิด gateway (ดู `PAYMENT.md`) · ระหว่างนี้ตั้งราคาคอร์สไม่เกิน ~5,000 บาท |
| **สลิปปลอม / ตรวจพลาด** | สูญรายได้ + งาน admin บานปลาย | unique `TransRef` ที่ระดับ DB + ตรวจยอด/ผู้รับ/เวลา + reconcile รายวัน + monitor อัตรา manual review |
| **Self-host ล่ม / ข้อมูลหาย** | ระบบดับ ไม่มีใครกู้ให้ | backup off-site + ซ้อมกู้ทุกไตรมาส + monitoring + runbook (`DEPLOYMENT.md`) |
| **MSSQL Express ชนเพดาน 10 GB** | เขียนข้อมูลไม่ได้กะทันหัน | แยก `WatchEvents` เป็น DB ต่างหาก + purge 90 วัน + alert ที่ 7 GB |
| ค่า bandwidth วิดีโอบานปลาย | ต้นทุนต่อผู้ใช้สูง | ตั้ง budget alert ที่ Bunny, จำกัด bitrate สูงสุด 1080p |
| Instructor ไม่ใช้ builder เอง | IN-01 เสียเปล่า | ทดสอบ usability กับ instructor จริง 3 คนกลาง P4 (P4-30) |
