# SIRI UpSkill — Requirements Traceability

รวม requirement จาก 2 แหล่ง:
- `Siri_E_Learning_Requirements.xlsx` (sheet: Platform Requirements, 20 รายการ)
- `Siri_E_Learning_Prompt_Requirements.pdf` (3 หน้า, รายละเอียดขยายความ)

**Product name (ตัดสินใจแล้ว):** SIRI UpSkill
**Positioning:** long-term E-Learning marketplace (แนว SkillLane / FutureSkill) — ผู้เรียนซื้อคอร์สแล้วเข้าถึงได้ระยะยาว, มี instructor เป็น content creator, platform หักส่วนแบ่งรายได้

---

## 1. Learner Experience (Frontend / User Facing)

| ID | Feature | รายละเอียด | Priority | Phase |
|----|---------|-----------|----------|-------|
| LX-01 | Smart Search & Deep Categorization | ค้นหา + filter (category, difficulty, price, rating) + Mega Menu | Must | P1 |
| LX-02 | Course Detail & Syllabus Preview | แสดงจำนวน episode, duration รวม, free preview, learning outcomes | Must | P1 |
| LX-03 | Advanced Video Player | ปรับความเร็ว 0.5x–2.0x, resume playback, auto-resolution ตาม bandwidth | Must | P2 |
| LX-04 | Interactive Learning per Episode | video + attachment (PDF/Source Code) + quiz ท้ายบท + assignment submission | Must | P5 |
| LX-05 | Progress Tracking & Gamification | progress % + auto-generate Certificate of Completion | Nice | P5 |
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
| SE-02 | Dynamic Watermark | watermark เคลื่อนไหวแบบสุ่ม แสดงชื่อ/อีเมลผู้เรียนทับวิดีโอ เพื่อป้องปรามและสืบหาต้นตอการอัดจอ | Must | P2 |
| SE-03 | Concurrent Login Control | จำกัด session พร้อมกัน (default สูงสุด 2 devices ต่อ account) กันแชร์รหัส | Must | P0 |

---

## ⚠️ Requirement ที่ v1 จะยังทำไม่ครบ (จากการตัดสินใจของเจ้าของโปรเจ็ค 2026-08-17)

| ID | ส่วนที่ขาด | สาเหตุ | จะได้เมื่อไหร่ |
|----|-----------|--------|--------------|
| LX-06 | บัตรเครดิต/เดบิต และ **ผ่อนชำระ** | เลือกใช้ EasySlip (ตรวจสลิป) แทน payment gateway ใน v1 | เมื่อเปิด gateway จริง — ดู `PAYMENT.md` |
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
