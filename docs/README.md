# เอกสารโปรเจ็ค SIRI UpSkill

> **ต้นฉบับอยู่ที่ repo `siri_upskill_backend`** — สำเนาใน `siri_upskill_ui` มีไว้อ่านอย่างเดียว ถ้าต้องแก้ให้แก้ที่ backend แล้ว copy ไปทับ

> เอกสารทั้งหมดในโฟลเดอร์นี้เขียนขึ้นตอนโปรเจ็คยังเป็น monorepo (แยกเป็นสอง repo เมื่อ 2026-08-25) — **path ที่ขึ้นต้นด้วย `backend/` หมายถึงไฟล์ที่ root ของ repo `siri_upskill_backend` และ `frontend/` หมายถึงไฟล์ที่ root ของ repo `siri_upskill_ui`**

| ไฟล์ | เนื้อหา |
|------|---------|
| `TASKS.md` | คิวงานทั้งหมดพร้อม ID / dependency / acceptance / คอลัมน์ `Status` — **แหล่งความจริงว่า task ไหนเสร็จแล้ว** |
| `PROGRESS.md` | บันทึกรายละเอียดงานที่ทำไปแล้ว (ยกออกมาจาก `CLAUDE.md` ตอนแยก repo) |
| `REQUIREMENTS.md` | requirement จากเอกสารลูกค้า — มีน้ำหนักเหนือเอกสารตัวอื่นเมื่อขัดกัน |
| `ARCHITECTURE.md` | โครงสร้าง module, layer, boundary, OpenAPI |
| `DATABASE.md` | ตาราง, index, convention |
| `SECURITY.md` | baseline ความปลอดภัยฉบับเต็ม |
| `PAYMENT.md` | Stripe / PromptPay / webhook |
| `DEPLOYMENT.md` | Contabo, docker, backup, CI/CD |
| `DECISIONS.md` | การตัดสินใจที่ล็อกแล้ว (D-xx) + คำถามที่ยังไม่ตอบ (Q-xx) |
| `ROADMAP.md` / `PLAN.md` | phase และลำดับความสำคัญ |
| `ANTIGRAVITY_HANDOFF.md` | กติกาส่งงานให้ coding agent อีกตัว + คิวงาน |
