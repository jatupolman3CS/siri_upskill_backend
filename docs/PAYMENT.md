# Payment Design — PromptPay QR + EasySlip Verification

> **ตัดสินใจแล้ว (Q2):** เริ่มด้วย **PromptPay QR + ตรวจสลิปอัตโนมัติผ่าน EasySlip** ยังไม่ใช้ payment gateway

## ⚠️ ผลกระทบต่อ requirement — ต้องรับรู้ร่วมกัน

Requirement **LX-06** (Must-Have) ระบุว่าต้องรองรับ **QR PromptPay + บัตรเครดิต/เดบิต + ผ่อนชำระ**
EasySlip เป็นบริการ *ตรวจสอบสลิปโอนเงิน* ไม่ใช่ payment gateway จึงทำได้แค่ส่วน PromptPay

| วิธีจ่าย | v1 (EasySlip) | ต้องรอ |
|---------|---------------|--------|
| PromptPay QR | ✅ | — |
| บัตรเครดิต/เดบิต | ❌ | เปิด gateway จริง (Opn/2C2P/GBPrime) |
| ผ่อนชำระ 0% | ❌ | เปิด gateway + ข้อตกลงกับธนาคาร |

**ผลที่ตามมา:** คอร์สราคาสูง (>5,000 บาท) จะขายยากขึ้นเพราะไม่มีผ่อน — เป็นการลด scope ของ Must-Have ที่เจ้าของโปรเจ็คเลือกเอง
**การรับมือ:** ออกแบบ `IPaymentMethod` แบบ pluggable ตั้งแต่ v1 → เสียบ gateway ทีหลังได้โดยไม่ต้องรื้อ Order/Enrollment
**เกณฑ์ที่ควรย้ายไป gateway จริง:** ยอดขาย > 300 ออร์เดอร์/เดือน หรือมีคอร์ส > 5,000 บาท ขึ้นขาย หรือ manual review > 10% ของออร์เดอร์

---

## Flow การชำระเงิน

```
1. ผู้ซื้อกด "ชำระเงิน"
   → server คำนวณราคาเอง (ห้ามเชื่อราคาจาก client)
   → สร้าง Order  status = AwaitingPayment, ExpiresAtUtc = now + 30 นาที

2. server สร้าง PromptPay QR payload (EMVCo + CRC16) จาก PromptPay ID ของร้าน + ยอดเงิน
   → ส่ง QR เป็น SVG/PNG กลับไป  (ไม่ต้องเรียก API ภายนอก — คำนวณเองได้)

3. ผู้ซื้อโอนผ่านแอปธนาคาร → บันทึกภาพสลิป

4. ผู้ซื้ออัปโหลดสลิปที่หน้า order นั้น
   → ตรวจ magic bytes, ขนาด ≤ 5MB, rate limit 5 ครั้ง/ออร์เดอร์
   → เก็บไฟล์ (private, ไม่ public URL)

5. server เรียก EasySlip verify (ส่งภาพ หรือส่ง payload ที่อ่านจาก QR บนสลิป)
   → ได้ transRef, amount, sender, receiver, date

6. ตรวจ 5 ข้อ (ทุกข้อต้องผ่าน):
   ✓ transRef ยังไม่เคยถูกใช้    ← unique index กันสลิปซ้ำ (สำคัญที่สุด)
   ✓ amount == Order.TotalAmount แบบเป๊ะ
   ✓ receiver ตรงกับบัญชีร้าน (เทียบเลขบัญชี 4 ตัวท้าย + ชื่อบัญชี)
   ✓ date อยู่ระหว่าง Order.CreatedAtUtc - 10 นาที ถึง now + 5 นาที
   ✓ Order ยังไม่หมดอายุ / ยังไม่จ่าย

7a. ผ่านทุกข้อ → Payment.Status = Succeeded → Order = Paid
    → outbox event → auto-enroll + ส่งอีเมลใบเสร็จ + แจ้งเตือนในระบบ
7b. ไม่ผ่านข้อใดข้อหนึ่ง หรือ EasySlip ล่ม/โควตาหมด
    → Order = UnderReview → เข้าคิว admin ตรวจมือ (ไม่ปฏิเสธทันที)
    → แจ้งผู้ซื้อว่า "กำลังตรวจสอบ ภายใน 24 ชม."
```

## ความเสี่ยงและการรับมือ

| ความเสี่ยง | การรับมือ |
|-----------|----------|
| **สลิปปลอม / ตัดต่อ** | EasySlip อ่านจาก QR บนสลิปซึ่งเป็นข้อมูลจากธนาคาร ไม่ใช่ OCR ภาพ — ปลอมยากกว่ามาก แต่ยังต้องเทียบยอด+ผู้รับ+เวลาเสมอ |
| **ใช้สลิปเดิมซ้ำหลายออร์เดอร์** | `UQ(Provider, TransRef)` ที่ระดับ DB — ป้องกันแน่นอนที่สุด ไม่ใช่แค่เช็คใน memory |
| **สลิปคนอื่น** | ยอมรับได้ (คนอื่นโอนให้ได้) แต่ log ผู้โอนไว้ทุกครั้ง; ถ้ามีการ dispute ใช้ข้อมูลนี้ |
| **โควตา EasySlip หมด** | monitor โควตาผ่าน job + alert ที่ 80%; หมดแล้ว → fallback เข้า manual review อัตโนมัติ ห้ามปฏิเสธออร์เดอร์ |
| **EasySlip API ล่ม** | retry แบบ exponential backoff 3 ครั้ง → ไม่สำเร็จให้เข้า manual review |
| **ผู้ซื้อโอนแล้วไม่อัปสลิป** | job แจ้งเตือนอีเมลที่ 15 นาที + ปิดออร์เดอร์ที่ 30 นาที; ถ้าอัปสลิปหลังหมดอายุ ระบบเปิดออร์เดอร์ให้ใหม่อัตโนมัติถ้าตรวจผ่าน |
| **โอนยอดผิด (ขาด/เกิน)** | ขาด → manual review + แจ้งให้โอนเพิ่ม; เกิน → manual review + คืนส่วนต่าง (บันทึกเป็น Refund) |
| **ไม่มี webhook** | ต่างจาก gateway ตรงไม่มีการยืนยันแบบ push — ต้องมี **reconcile job รายวัน** เทียบ Order ที่ Paid กับ statement ธนาคารเพื่อจับรายการหลุด |

## ข้อบังคับด้าน implementation

- ราคาคำนวณที่ server เท่านั้น — request รับได้แค่ course/bundle id + promo code
- ตรวจสลิปต้องอยู่ใน transaction เดียวกับการเปลี่ยนสถานะ order (กัน race จากการอัปสลิปพร้อมกัน 2 หน้าจอ)
- `PaymentSlips.TransRef` ต้องมี unique index — **ไม่ใช่แค่ `if (exists)` ในโค้ด**
- API key ของ EasySlip อยู่ใน env/secret เท่านั้น
- ทุกการเปลี่ยนสถานะเงิน + ทุกการตัดสินของ admin ต้องเขียน audit ที่แก้ย้อนหลังไม่ได้
- เก็บ response ดิบจาก EasySlip (json) ไว้ในตาราง เพื่อใช้ตรวจสอบย้อนหลังและ dispute
- เตรียม `IPaymentMethod` + `IPaymentVerifier` ตั้งแต่ต้น เพื่อเสียบ gateway ในอนาคต

## ตารางที่เพิ่ม/เปลี่ยนจาก `DATABASE.md`

```
commerce.Payments(Id PK, OrderId FK, Method,          -- PromptPaySlip | (อนาคต) Card | Installment
                  Amount, Status,                      -- Pending|UnderReview|Succeeded|Rejected|Expired
                  VerifiedAtUtc, VerifiedBy,           -- 'system:easyslip' หรือ userId ของ admin
                  RejectReason, CreatedAtUtc)

commerce.PaymentSlips(Id PK, PaymentId FK, OrderId FK,
                      StorageKey,                      -- ไฟล์ภาพสลิป (private)
                      Provider,                        -- 'EasySlip'
                      TransRef,                        -- UQ(Provider, TransRef) ← กันสลิปซ้ำ
                      SlipAmount decimal(18,2), SlipDateUtc,
                      SenderBank, SenderAccountMasked, SenderName,
                      ReceiverBank, ReceiverAccountMasked, ReceiverName,
                      RawResponse nvarchar(max),        -- เก็บ json ดิบไว้ dispute
                      VerifyResult,                    -- Passed|Failed|ProviderError
                      FailedRules nvarchar(400),       -- ข้อที่ไม่ผ่าน เช่น 'amount,receiver'
                      UploadedByUserId, UploadedAtUtc, ClientIp)

commerce.MerchantAccounts(Id PK, PromptPayId, AccountName, BankCode,
                          AccountNoLast4, IsActive)     -- ใช้เทียบผู้รับ + สร้าง QR

commerce.PaymentReviewQueue(Id PK, PaymentId FK, Reason, Status,
                            AssignedToUserId, ResolvedByUserId, ResolvedAtUtc, Note)
```

> **ก่อน implement:** เปิด doc ของ EasySlip เช็คชื่อ field และ endpoint จริงอีกครั้ง (ชื่อ field ข้างบนอิงจากรูปแบบทั่วไป — ต้องยืนยันกับสเปคปัจจุบัน) รวมถึงเช็คโควตาของแพ็กเกจที่ซื้อว่าพอกับปริมาณออร์เดอร์ที่คาดไว้
