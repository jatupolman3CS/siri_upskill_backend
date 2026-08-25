# SIRI UpSkill — Requirements Traceability

รวม requirement จาก 3 แหล่ง:
- `Siri_E_Learning_Requirements.xlsx` (sheet: Platform Requirements, 20 รายการ)
- `Siri_E_Learning_Prompt_Requirements.pdf` (3 หน้า, รายละเอียดขยายความ)
- `SiriLearn — Master System Specification Document.txt` + `Master System Architecture & Development Prompt for Claude.txt` (สเปคขยาย — reconcile เข้า backlog 2026-08-19: ฟีเจอร์ใหม่อยู่ใน §5, ความขัดแย้งด้าน stack ตัดสินตาม `DECISIONS.md` D-16)

**Product name (ตัดสินใจแล้ว):** SIRI UpSkill
**Positioning:** long-term E-Learning marketplace (แนว SkillLane / FutureSkill) — ผู้เรียนซื้อคอร์สแล้วเข้าถึงได้ระยะยาว, มี instructor เป็น content creator, platform หักส่วนแบ่งรายได้

---

## 1. Learner Experience (Frontend / User Facing)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| LX-01 | Smart Search & Deep Categorization | ค้นหา + filter (category, difficulty, price, rating, instructor) + Mega Menu | Must | P1 |
| LX-02 | Course Detail & Syllabus Preview | แสดงจำนวน episode, duration รวม, free preview, learning outcomes | Must | P1 |
| LX-03 | Advanced Video Player | ปรับความเร็ว 0.5x–2.0x, resume playback, auto-resolution ตาม bandwidth | Must | P2 |
| LX-04 | Interactive Learning per Episode | video + attachment (PDF/Source Code) + quiz ท้ายบท + assignment submission | Must | P5 |
| LX-05 | Progress Tracking & Gamification | progress % + auto-generate Certificate of Completion (PDF + QR code ชี้หน้า verify) | Nice | P5 |
| LX-06 | Seamless Checkout & Payment | PromptPay QR, Credit/Debit Card, ผ่อนชำระสำหรับคอร์สราคาสูง | Must | P3 ⚠️ **ทำได้บางส่วน** |
| LX-07 | Q&A & Community | discussion board / comment ใต้แต่ละ episode คุยกับ instructor ตรง | Nice | P5 |

## 2. Instructor Dashboard (Content Creator Facing)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| IN-01 | Drag-and-Drop Course Builder | สร้างคอร์ส, upload video, จัดเรียง/reorder episode, ตั้ง free preview vs paid | Must | P4 |
| IN-02 | Instructor Analytics | ยอดขาย, จำนวนนักเรียน active, drop-off point (episode ที่คนเลิกดู) | Nice | P4 |
| IN-03 | Announcement System | ส่ง notification / bulk email หานักเรียนที่ลงทะเบียน | Nice | P6 |

## 3. Admin & Platform Management (Superadmin Facing)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| AD-01 | Dynamic CMS | จัด banner หน้าแรก, เมนู, เขียน blog/article ที่ทำ SEO ได้ โดยไม่ต้อง deploy code | Must | P6 |
| AD-02 | Marketing & Promotion Engine | Promo Code, Flash Sale (ตั้งเวลา), Course Bundle (ซื้อหลายคอร์สลด) | Must | P3/P6 |
| AD-03 | Revenue Split System | คำนวณ + รายงานส่วนแบ่งอัตโนมัติ platform/instructor (default 70/30), รองรับ payout | Nice | P6 |

## 4. Security & Anti-Piracy (CRITICAL INFRASTRUCTURE)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| SE-01 | Video DRM | กัน download ผ่าน third-party tool (IDM, browser extension) — ต้องเป็น DRM จริง (Widevine/PlayReady/FairPlay) ไม่ใช่แค่ signed URL | Must | P2 |
| SE-02 | Dynamic Watermark | watermark เคลื่อนไหวแบบสุ่ม แสดงชื่อ/อีเมล + timestamp ของผู้เรียนทับวิดีโอ เพื่อป้องปรามและสืบหาต้นตอการอัดจอ | Must | P2 |
| SE-03 | Concurrent Login Control | จำกัด session พร้อมกัน (default สูงสุด 2 devices ต่อ account) กันแชร์รหัส | Must | P0 |

## 5. ส่วนขยายหลัง v1 launch (จากสเปค SiriLearn — เจ้าของโปรเจ็ครับเข้า backlog 2026-08-19)

> ทั้งหมดเป็นงาน **หลัง launch v1** (Phase P8–P10 ใน `ROADMAP.md`/`TASKS.md`) ไม่กระทบ timeline 28 สัปดาห์เดิม
> ลำดับ phase ปรับได้ตามสถานการณ์ธุรกิจ · P9 ติดการตัดสินใจ Q7, P10 ติด Q6 (ดู `DECISIONS.md`)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| LX-08 | Social Login | สมัคร/ล็อกอินผ่าน Google, Facebook, Apple ID — ผูกกับบัญชีอีเมลเดิมได้เมื่อ email ยืนยันแล้วเท่านั้น (กัน account takeover) | Nice | P8 |
| LX-09 | Timestamp Notes | จดโน้ตผูกกับตำแหน่งเวลาในวิดีโอ คลิกโน้ตแล้ว seek ไปตำแหน่งนั้น | Nice | P8 |
| LX-10 | Learning Path / Bootcamp | จัดกลุ่มคอร์สเรียนต่อเนื่องเพื่อเป้าหมายอาชีพ + progress รวมทั้ง path | Nice | P8 |
| LX-11 | Gamification | สะสมแต้ม XP (จบ episode/quiz/คอร์ส) + Leaderboard (rollup รายวัน, opt-out ได้) | Nice | P8 |
| LX-12 | Subscription | สมาชิกรายเดือน/รายปีแบบบุฟเฟต์ — ต้องเปิดรับบัตรเครดิตก่อน (PromptPay ผ่าน Stripe เป็น non-recurring ทำ auto-renew ไม่ได้) | Nice | P10 |
| CO-01 | B2B Roles & Organization | องค์กร (Organization) + บทบาท HR Admin และ Corporate Learner | Nice | P9 |
| CO-02 | Excel Employee Import | HR นำเข้ารายชื่อพนักงานจากไฟล์ Excel + invite ผ่านอีเมล + รายงาน error รายแถว | Nice | P9 |
| CO-03 | Mandatory Training | มอบหมายคอร์สบังคับให้พนักงาน/กลุ่ม + deadline + แจ้งเตือนอัตโนมัติ | Nice | P9 |
| CO-04 | Corporate Progress Reporting | dashboard ความคืบหน้ารายคน/แผนก/คอร์ส + export Excel/PDF | Nice | P9 |

---

## ⚠️ Requirement ที่ v1 จะยังทำไม่ครบ (จากการตัดสินใจของเจ้าของโปรเจ็ค 2026-08-17)

| ID | ส่วนที่ขาด | สาเหตุ | จะได้เมื่อไหร่ |
|----|-----------|--------|--------------|
| LX-06 | บัตรเครดิต/เดบิต และ **ผ่อนชำระ** | ใช้ Stripe (ตัดสินใหม่ 2026-08-18 แทน EasySlip) — v1 เปิดเฉพาะ PromptPay QR; บัตร Visa/MC เปิดเพิ่มจาก Stripe Dashboard ได้ภายหลัง แต่ผ่อนชำระ Stripe ไทยไม่รองรับ | บัตร: เมื่อตัดสินใจเปิด · ผ่อน: ต้องเพิ่ม gateway ไทย (Opn/2C2P) — ดู `PAYMENT.md` |
| SE-01 | DRM บน **Safari/iOS** อาจไม่ได้ | ขึ้นกับว่า Bunny Stream รองรับ FairPlay ในแพ็กเกจที่ซื้อหรือไม่ (ต้องเช็คใน P2-01) | ถ้าไม่รองรับ ต้อง fallback เป็น token-auth HLS + watermark |

## Requirement ที่เอกสารยังไม่ระบุ — ตั้งสมมติฐานไว้ (ดู `DECISIONS.md`)

| หัวข้อ | สมมติฐานที่ใช้วางแผน |
|--------|----------------------|
| ภาษา | TH เป็นหลัก + โครงสร้างรองรับ EN (i18n ตั้งแต่แรก) |
| สกุลเงิน | THB เท่านั้นใน v1 |
| Access duration | ตั้งค่าได้ต่อคอร์ส (`AccessDurationDays`), default = ตลอดชีพ |
| ภาษี/ใบกำกับ | ออกใบเสร็จ/e-Tax invoice เป็น Phase 6+ (ยังไม่อยู่ใน MVP) |
| Live / Cohort class | ไม่อยู่ใน scope v1 (on-demand video เท่านั้น) |
| Mobile app | Web responsive + PWA ก่อน, native app เป็น Phase ถัดไป |
| Scale เป้าหมาย | ~50k registered users, ~5k concurrent viewers ปีแรก |
