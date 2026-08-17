# Security Rules

baseline ฉบับเต็มอยู่ที่ `docs/SECURITY.md` — ไฟล์นี้คือกฎที่ต้องใช้ตอนเขียนโค้ดทุกครั้ง

## ต้องหยุดแล้วถามก่อน (ห้ามตัดสินใจเอง)

- เปลี่ยนวิธี authentication / อายุ token / ที่เก็บ token
- เปลี่ยนตรรกะ concurrent-login limit หรือ playback entitlement
- เปลี่ยนวิธีคิดเงิน ส่วนแบ่งรายได้ หรือ refund
- ปิด/ผ่อน DRM, watermark, rate limit "เพื่อให้เทสต์ผ่าน" — **ห้ามเด็ดขาด** ให้แก้ที่เทสต์หรือถาม
- เพิ่ม dependency ใหม่ที่แตะ crypto, auth, payment

## กฎประจำวัน

**Authorization**
- ทุก endpoint ต้องมี policy ระบุชัด (default deny ที่ระดับ group)
- ทุกครั้งที่รับ id จากผู้ใช้ ต้องเช็คว่า user คนนี้มีสิทธิ์กับ resource นั้นจริง (กัน IDOR)
- `userId` มาจาก `IUserContext` (จาก token) เท่านั้น **ห้ามรับจาก body/query**
- การซ่อนปุ่มที่ frontend ไม่นับเป็นการป้องกัน

**Video / playback**
- ทุกครั้งที่ออก playback token: เช็ค enrollment ยัง active + ไม่หมดอายุ + device limit + rate limit
- manifest URL ต้อง signed และอายุสั้น (2–5 นาที)
- DRM license request ต้องผ่าน proxy ของเราและตรวจสิทธิ์ซ้ำ — ห้ามให้ client คุยกับ license server ตรง
- watermark payload (ชื่อ/อีเมล) ต้องมาจาก server ห้ามให้ client กำหนดเอง

**เงิน**
- ราคาคำนวณที่ server ทั้งหมด — request รับได้แค่ course id / bundle id / promo code
- ยืนยันการจ่ายจาก webhook + signature เท่านั้น ห้ามเชื่อ redirect หรือ callback จาก frontend
- Webhook ต้อง idempotent (unique index บน provider event id) และรองรับการยิงซ้ำ
- ทุกการเปลี่ยนสถานะเงินต้องเขียน audit ที่แก้ย้อนหลังไม่ได้

**ข้อมูล**
- ห้าม log: password, token, OTP, เลขบัตร, เลขบัญชี, ข้อมูลสุขภาพ/ส่วนบุคคลที่ไม่จำเป็น
- เข้ารหัสก่อนเก็บ: เลขบัญชีธนาคาร, เลขประจำตัวผู้เสียภาษี
- **ห้ามเก็บเลขบัตรเครดิตในระบบเราทุกกรณี** ใช้ token จาก gateway
- Error message ที่ตอบผู้ใช้ต้องไม่หลุด stack trace, ชื่อตาราง, หรือ query

**Input / output**
- ตรวจ file upload ด้วย magic bytes ไม่ใช่แค่นามสกุล + จำกัดขนาด + serve ผ่าน signed URL
- HTML จาก CMS ต้อง sanitize ที่ server ด้วย allowlist ก่อนเก็บ **และ** ก่อนแสดง
- ห้าม `bypassSecurityTrustHtml` ใน Angular โดยไม่มีการ sanitize มาก่อน
- URL ปลายทางที่ server จะยิงเอง ต้องผ่าน allowlist (กัน SSRF)

**Secrets**
- ห้ามใส่ค่าจริงของ connection string / API key ในไฟล์ที่ commit — ใช้ `dotnet user-secrets` (dev) และ env/Key Vault (prod)
- `appsettings.*.json` ที่ commit ให้ใส่แค่ placeholder
- ถ้าเผลอเห็น secret จริงในไฟล์ที่ commit แล้ว → หยุด แจ้ง user ทันที และแนะนำให้ rotate อย่าเพิ่งทำอย่างอื่นต่อ
