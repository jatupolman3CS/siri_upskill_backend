# SIRI UpSkill — Delivery Roadmap

**สมมติฐาน:** BE 2 + FE 2 + QA 1 เต็มเวลา (ถ้าทีมเล็กกว่านี้ ยืด timeline ตามสัดส่วน)
**รวม ~28 สัปดาห์** ถึง production launch — MVP ขายได้จริงที่ปลาย Phase 3 (~สัปดาห์ที่ 14)
task ระดับปฏิบัติทั้งหมดอยู่ที่ **`docs/TASKS.md`**

> **⚠️ อัปเดต 2026-08-24:** ลำดับเฟสในไฟล์นี้คือ *แผนตั้งต้น* — ของจริงถูกทำข้ามลำดับไปแล้ว (P2–P6 ถูก implement ไปมากตั้งแต่ 2026-08-21 โดย Antigravity ขณะที่ P4-01/P4-04 ซึ่งอยู่ต้นเฟส P4 ยังไม่มีเลย) · **สถานะจริงต่อ task ดูที่คอลัมน์ `Status` ใน `docs/TASKS.md`** และลำดับงานที่จะทำต่อดูที่ `docs/ANTIGRAVITY_HANDOFF.md` §4 · ตัวเลขสัปดาห์/คน-วันในไฟล์นี้คิดบนสมมติฐานทีมมนุษย์ ใช้เป็นลำดับความสำคัญ ไม่ใช่แผนเวลาจริง

| Phase | ชื่อ | สัปดาห์ | Requirement ที่ปิด |
|-------|-----|--------|-------------------|
| P0 | Foundation & Infrastructure | 1–3 | SE-03 |
| P1 | Catalog, Search, SEO | 4–7 | LX-01, LX-02 |
| P2 | Media Pipeline & Secure Player | 8–11 | LX-03, SE-01, SE-02 |
| P3 | Commerce & Stripe Payment | 12–14 | LX-06 (บางส่วน), AD-02 (บางส่วน) |
| P4 | Instructor Studio | 15–18 | IN-01, IN-02 |
| P5 | Interactive Learning | 19–21 | LX-04, LX-05, LX-07 |
| P6 | Admin, CMS, Revenue | 22–25 | AD-01, AD-02, AD-03, IN-03 |
| P7 | Hardening & Launch | 26–28 | — |
| P8 | Growth & Engagement | หลัง launch | LX-08, LX-09, LX-10, LX-11 |
| P9 | B2B Corporate Portal | หลัง launch | CO-01, CO-02, CO-03, CO-04 |
| P10 | Subscription | หลัง launch | LX-12 |

> P3 ใช้ **Stripe** (แก้ไข 2026-08-18 จาก EasySlip) — ต้องสมัครบัญชี Stripe Thailand ล่วงหน้าตั้งแต่ P1 (KYC ใช้เวลา); v1 เปิดเฉพาะ PromptPay QR — บัตรเปิดเพิ่มทีหลังได้จาก dashboard แต่**ผ่อนชำระยังไม่มี** (Stripe ไทยไม่รองรับ)
> P0 หนักขึ้นเพราะ self-host บน Contabo (ต้องทำ infra, backup, CI/CD เอง)
> **P8–P10 เพิ่ม 2026-08-19** จากการ reconcile สเปค SiriLearn (D-16) — เป็นงานหลัง launch ทั้งหมด ไม่กระทบ 28 สัปดาห์เดิม ลำดับสลับได้ตามธุรกิจ

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
- **LX-01** search + filter (category / level / price / rating / instructor — ตัวสุดท้ายเพิ่ม 2026-08-19 เป็น P1-10 ตาม D-16) + Mega Menu + facet count
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

## P3 — Commerce & Stripe Payment (สัปดาห์ 12–14)
> สเปคเต็ม: `PAYMENT.md` — v1 ใช้ **PromptPay QR ผ่าน Stripe** (PaymentIntent + webhook) ยังไม่เปิดบัตร/ไม่มีผ่อน

- Cart / Order / pricing engine ที่ server + `IPaymentMethod` (เผื่อเสียบ provider อื่นทีหลัง)
- Stripe PaymentIntent (thb, promptpay) → แสดง QR ที่ Stripe สร้างให้ → จ่ายในแอปธนาคาร
- Webhook (ตรวจ signature + idempotent ด้วย unique index บน event id) → auto-enroll
- Refund flow ผ่าน Stripe + ops queue กรณีผิดปกติ (จ่ายซ้ำ/จ่ายหลังหมดอายุ/refund ค้าง)
- Promo code (**AD-02** ส่วนแรก) + reconcile job รายวันเทียบ Stripe API + หน้า my-courses
- **Exit criteria:** ซื้อ→สแกน QR→จ่าย→เข้าเรียนอัตโนมัติสำเร็จ, webhook ยิงซ้ำ/ปลอมไม่มีผล 100%, ไม่มีออร์เดอร์ค้างสถานะ

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

## P8 — Growth & Engagement (หลัง launch)
> จากสเปค SiriLearn (D-16, 2026-08-19) — รายละเอียด task อยู่ `TASKS.md` §P8
- **LX-08** Social login (Google/Facebook/Apple) — เจ้าของโปรเจ็คต้องสมัคร OAuth app ก่อน
- **LX-09** Timestamp notes ผูกตำแหน่งวิดีโอ ใน player
- **LX-10** Learning Path / Bootcamp + progress รวม
- **LX-11** Gamification: XP + Leaderboard (rollup รายคืน)
- **Exit criteria:** ล็อกอิน Google ได้จริง · โน้ต seek ถูกตำแหน่ง · XP ไม่ซ้ำแม้ event ยิงซ้ำ

## P9 — B2B Corporate Portal (หลัง launch)
> จากสเปค SiriLearn (D-16) · **รอ Q7** (โมเดลขาย/คิดเงิน B2B) ก่อนเริ่มส่วน license
- **CO-01** Organization + role HR Admin / Corporate Learner (module ใหม่ `Siri.Modules.Corporate`)
- **CO-02** Excel import พนักงาน + invite + รายงาน error รายแถว
- **CO-03** Mandatory training + deadline + แจ้งเตือน
- **CO-04** HR dashboard รายคน/แผนก + export Excel/PDF
- **Exit criteria:** HR เห็นเฉพาะ org ตัวเอง (IDOR ข้าม org ล้มเหลว 100%) · import 100 คนสำเร็จ

## P10 — Subscription (หลัง launch)
> จากสเปค SiriLearn (D-16) · **รอ Q6** (สูตรแบ่งรายได้ subscription) + การตัดสินใจเปิดรับบัตร — PromptPay ผ่าน Stripe เป็น non-recurring ทำ auto-renew ไม่ได้
- **LX-12** แผนรายเดือน/ปี ผ่าน Stripe Billing (บัตรเท่านั้น) + entitlement ใน playback (enrollment **หรือ** subscription active)
- Revenue split จาก pool ตามสัดส่วนนาทีที่ถูกดู (default เสนอ — รอ Q6 ยืนยัน)
- **Exit criteria:** ต่ออายุ/ยกเลิก/บัตรตาย ครบ loop ใน Stripe test clock · subscription ขาด = playback ปฏิเสธทันที

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
