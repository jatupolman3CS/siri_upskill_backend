# SIRI UpSkill — Security Baseline

Requirement SE-01/02/03 ถูกระบุเป็น **CRITICAL INFRASTRUCTURE** ในเอกสารต้นทาง จึงถือเป็น acceptance criteria ไม่ใช่ nice-to-have

## 1. Anti-piracy (SE-01, SE-02)

**ชั้นการป้องกัน (ต้องมีครบ ไม่ใช่เลือกอย่างใดอย่างหนึ่ง)**

| ชั้น | ป้องกันอะไร | หมายเหตุ |
|-----|------------|---------|
| Signed manifest URL (TTL 2–5 นาที, ผูก IP/session) | ก๊อป URL ไปแชร์ | จำเป็นแต่ไม่พอ |
| DRM จริง (Widevine / PlayReady / FairPlay) | IDM, extension, ตัว downloader | **หัวใจของ SE-01** |
| License proxy ผ่าน API เรา | ขอ license โดยไม่มีสิทธิ์ | ตรวจ enrollment ซ้ำทุกครั้งที่ขอ license |
| Dynamic watermark (ชื่อ+อีเมล, ขยับสุ่ม) | ถ่ายจอ / อัดหน้าจอ | **SE-02** — สืบย้อนหาต้นตอได้ |
| `PlaybackSessions` log | สืบสวนย้อนหลัง | เก็บ user, ip, device, เวลา |
| Rate limit + anomaly detection | ดูดคอร์สทั้งคอร์สรวดเดียว | เช่น ขอ playback token > 30 ตอน/ชม. → flag |

**ต้องสื่อสารให้ชัดกับ stakeholder:** DRM กันการถ่ายด้วยกล้องมือถือไม่ได้ — ไม่มีระบบไหนกันได้ watermark คือมาตรการที่ทำให้การปล่อยไฟล์ *มีต้นทุน* กับคนปล่อย

## 2. Concurrent login (SE-03)
- default `MaxConcurrentSessions = 2` (ตั้งค่าระดับ `Identity:Security:MaxConcurrentSessions`, override รายบัญชีได้ผ่าน `Users.MaxConcurrentSessionsOverride`)
- เกิน limit ตอน login → revoke session เก่าสุด (เรียง `CreatedAtUtc`) + revoke refresh token ของ session นั้น + แจ้งเตือนทางอีเมล + เขียน `SecurityAudits` — ทั้งหมด atomic กับ transaction login เดียวกัน
- ผู้ใช้ดู/ถอดอุปกรณ์เองได้ที่หน้า "อุปกรณ์ที่เข้าสู่ระบบ" (P0-18)
- **MSSQL เป็น source of truth ของการตัดสิน evict** (ตัดสินใจเปลี่ยนจากแผนเดิมตอนทำ P0-17 — ดูเหตุผลและแผนสำหรับ Phase 2 ใน `docs/ARCHITECTURE.md` §5) Redis เป็นแค่ mirror เขียนตามหลังหลัง commit สำเร็จ, fail-open ถ้า Redis ล่ม (ไม่ block login) — ยังไม่มี fail-closed สำหรับ playback เพราะ feature นั้นยังไม่ถูกสร้าง ต้องออกแบบใหม่ตอน Phase 2

## 3. AuthN / AuthZ
- Access token JWT 15 นาที, refresh token 30 วัน แบบ rotation + reuse detection (ถ้าเจอ token เก่าถูกใช้ซ้ำ = revoke ทั้ง family)
- Refresh token เก็บใน **httpOnly + Secure + SameSite=Strict cookie**, access token อยู่ใน memory เท่านั้น (ห้าม localStorage)
- Password: ASP.NET Core Identity hasher (PBKDF2 ≥ 600k iterations) หรือ Argon2id
- บังคับ 2FA สำหรับ role Admin / SuperAdmin
- Authorization ต้องเช็คที่ server ทุกครั้ง — การซ่อนปุ่มที่ frontend ไม่ใช่การป้องกัน
- ทุก endpoint ต้องระบุ policy ชัดเจน; default deny (`RequireAuthorization()` ที่ระดับ group)

## 4. Data protection & PDPA
- เข้ารหัสข้อมูลอ่อนไหวก่อนลง DB: เลขบัญชีธนาคาร, เลขประจำตัวผู้เสียภาษี (`InstructorPayoutAccounts`)
- **ห้ามเก็บเลขบัตรเครดิตในระบบเราเด็ดขาด** — ใช้ token จาก gateway เท่านั้น
- Log ห้ามมี: password, token, OTP, เลขบัตร, เลขบัญชี → มี log redaction filter
- รองรับสิทธิ์เจ้าของข้อมูล: export ข้อมูลตัวเอง, ขอลบบัญชี (anonymize ไม่ใช่ hard delete เพราะ order ต้องเก็บตามกฎหมายบัญชี)
- Consent banner + privacy policy + cookie policy ก่อน launch
- Data retention: `WatchEvents` 12 เดือน, log 90 วัน, audit 3 ปี, เอกสารการเงิน 5 ปี

## 5. Application security
| หัวข้อ | มาตรการ |
|-------|---------|
| Injection | EF Core parameterized เท่านั้น — ห้าม string concat ใน SQL; ถ้าจำเป็นต้อง raw ใช้ `FromSqlInterpolated` |
| XSS | Angular sanitization default; ห้าม `bypassSecurityTrust*` เว้นแต่ผ่าน review; CMS HTML ต้อง sanitize ที่ server ด้วย whitelist |
| CSRF | refresh cookie เป็น SameSite=Strict + anti-forgery token สำหรับ cookie-based endpoint |
| File upload | ตรวจ magic bytes ไม่ใช่แค่นามสกุล, จำกัดขนาด, สแกนไวรัส, เก็บนอก web root, serve ผ่าน signed URL เท่านั้น |
| SSRF | ห้ามให้ user กำหนด URL ปลายทางที่ server จะยิงเอง (webhook/import) โดยไม่ผ่าน allowlist |
| IDOR | ทุก query ที่มี `{id}` ต้องมี ownership check เสมอ — เป็นข้อบังคับใน definition of done |
| Secrets | dev = `dotnet user-secrets`, prod = Key Vault / env; **ห้าม commit** — เปิด secret scanning ใน CI |
| Headers | HSTS, CSP (nonce-based), X-Content-Type-Options, Referrer-Policy, Permissions-Policy |
| Dependency | `dotnet list package --vulnerable` + `npm audit` ใน CI, fail ที่ระดับ high ขึ้นไป |

## 6. Payments
- ยืนยันยอดจาก **webhook ของ gateway เท่านั้น** ห้ามเชื่อ callback ที่มาจาก frontend
- ตรวจ signature ของ webhook ทุกครั้ง + unique index บน `ProviderEventId` (idempotency)
- คำนวณราคาที่ **server ล้วน** — frontend ส่งแค่ course id + promo code ห้ามส่งราคา
- ตรวจ promo code แบบ atomic (`UPDATE ... WHERE RedeemedCount < MaxRedemptions`) กัน race condition
- ทุกการเปลี่ยนสถานะเงินต้องเขียน audit trail ที่ลบไม่ได้

## 7. ก่อน launch (checklist)
- [ ] Pen-test ภายนอก + ปิดช่อง high/critical ครบ
- [ ] ทดสอบดึงวิดีโอด้วย IDM / extension / `yt-dlp` / devtools network → ต้องล้มเหลวทุกทาง
- [ ] ทดสอบ concurrent login เกิน limit
- [ ] ซ้อม restore database จริงจาก backup
- [ ] Rate limit ครบทุก endpoint สาธารณะ
- [ ] Alert + on-call runbook พร้อม
- [ ] PDPA artifacts ครบ (policy, consent, DPA กับ vendor)
