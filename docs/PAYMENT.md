# Payment Design — Stripe (PromptPay QR)

> **ตัดสินใจแล้ว (Q2 — แก้ไข 2026-08-18):** ใช้ **Stripe** เป็น payment provider เพียงเจ้าเดียว
> (เดิม Q2 ตัดสินเป็น PromptPay QR + ตรวจสลิปผ่าน EasySlip — เจ้าของโปรเจ็คสั่งเปลี่ยนเป็น Stripe และตัด EasySlip ออกทั้งหมด)
> v1 เปิดรับเฉพาะ **PromptPay QR** ผ่าน Stripe; บัตรเครดิต/เดบิต (Visa/Mastercard) เปิดเพิ่มได้ภายหลังจาก Stripe Dashboard โดยไม่ต้องเปลี่ยนสถาปัตยกรรม

## ข้อเท็จจริงของ Stripe ประเทศไทย (ตรวจสอบจาก docs จริง 2026-08-18)

- Stripe เปิดให้ธุรกิจไทยสมัครใช้งานได้จริง (Stripe Payments (Thailand) Ltd.) และรองรับ PromptPay เป็น payment method
- PromptPay ผ่าน Stripe: รับได้เฉพาะ **THB**, บัญชี merchant ต้องเป็นประเทศไทย, เพดาน **2 ล้านบาท/รายการ**, ใช้ได้เฉพาะ **non-recurring** (ตรงกับ use case ซื้อคอร์สครั้งเดียวพอดี)
- เงินเข้าบัญชีได้เฉพาะ THB; รับบัตรได้ Visa/Mastercard (ไม่มี Amex/JCB/UnionPay) — **ไม่มีผ่อนชำระ 0%**
- ชื่อบน statement ของผู้ซื้อจะขึ้นเป็น `STRIPE PAYMENTS (THAILAND) LTD` เสมอ (custom statement descriptor ใช้ไม่ได้กับ PromptPay) — ต้องสื่อสารบนหน้า checkout ให้ชัด
- อ้างอิง: [PromptPay payments](https://docs.stripe.com/payments/promptpay) · [Stripe TH supported methods](https://support.stripe.com/questions/supported-payment-methods-currencies-and-businesses-for-stripe-accounts-in-thailand)

## ⚠️ ผลกระทบต่อ requirement — ต้องรับรู้ร่วมกัน

Requirement **LX-06** (Must-Have) ระบุว่าต้องรองรับ **QR PromptPay + บัตรเครดิต/เดบิต + ผ่อนชำระ**

| วิธีจ่าย | v1 (Stripe) | ต้องรอ |
|---------|-------------|--------|
| PromptPay QR | ✅ | — |
| บัตรเครดิต/เดบิต | ⏸ ทำได้ทันทีทางเทคนิค (เปิดใน Stripe Dashboard) แต่ scope v1 เปิดเฉพาะ QR | ตัดสินใจเปิดเมื่อพร้อม |
| ผ่อนชำระ 0% | ❌ Stripe ไทยไม่รองรับ | ต้องเพิ่ม gateway ไทย (Opn/2C2P/GBPrime) + ข้อตกลงธนาคาร |

**การรับมือ:** ออกแบบ `IPaymentMethod` แบบ pluggable ตั้งแต่ v1 → เสียบ gateway อื่นเพิ่มทีหลังได้โดยไม่ต้องรื้อ Order/Enrollment

## สิ่งที่ต้องทำก่อนเริ่ม P3 (เจ้าของโปรเจ็คทำเอง — ควรเริ่มตั้งแต่ P1)

1. สมัครบัญชี Stripe Thailand (มี KYC/ข้อมูลธุรกิจ ใช้เวลา)
2. เปิดใช้ PromptPay ใน Stripe Dashboard (Settings → Payment methods)
3. เก็บ 3 ค่า: **secret key**, **publishable key**, **webhook signing secret** — ใส่ `dotnet user-secrets` (dev) / env (prod) เท่านั้น
4. ใช้ test mode ทดสอบให้ครบก่อนสลับ live key

---

## Flow การชำระเงิน

```
1. ผู้ซื้อกด "ชำระเงิน"
   → server คำนวณราคาเอง (ห้ามเชื่อราคาจาก client)
   → สร้าง Order  status = AwaitingPayment, ExpiresAtUtc = now + 30 นาที
   → server สร้าง Stripe PaymentIntent (currency=thb, payment_method_types=['promptpay'],
     amount เป็นหน่วยสตางค์) แล้วเก็บ PaymentIntentId ผูกกับ Payment row

2. Frontend confirm PaymentIntent → Stripe คืน next_action.promptpay_display_qr_code
   → แสดง QR ที่ Stripe สร้างให้ (ห้ามสร้าง QR payload เอง — Stripe เป็นผู้รับเงิน)

3. ผู้ซื้อสแกน QR จ่ายในแอปธนาคาร → Stripe รู้ผลทันที (real-time payment)

4. Stripe ยิง webhook มาที่ endpoint ของเรา (payment_intent.succeeded / .payment_failed / .canceled)
   → ตรวจ Stripe-Signature header ด้วย webhook signing secret ก่อนเชื่อ payload เสมอ
   → idempotent: unique index บน StripeEventId — event ยิงซ้ำต้องไม่ enroll ซ้ำ/ไม่เปลี่ยนสถานะซ้ำ
   → mark Payment = Succeeded → Order = Paid ใน transaction เดียว
   → outbox event → auto-enroll + ส่งอีเมลใบเสร็จ + แจ้งเตือนในระบบ

5. ไม่จ่ายภายใน 30 นาที → job ปิด Order (Expired) + cancel PaymentIntent ที่ Stripe
   (กัน race: ถ้า webhook succeeded มาถึงพร้อม/หลัง expiry ให้ยึดผลจาก Stripe เป็นหลัก
    — เงินออกจากบัญชีผู้ซื้อแล้ว ต้อง enroll ให้หรือ refund ไม่ใช่เงียบ)

6. Reconcile job รายวัน: list PaymentIntents จาก Stripe API เทียบกับ Orders
   → จับ webhook ที่หลุด/รายการไม่แมตช์ → รายงาน + แก้สถานะ
```

## ความเสี่ยงและการรับมือ

| ความเสี่ยง | การรับมือ |
|-----------|----------|
| **Webhook หลุด/มาช้า** | Stripe retry อัตโนมัติอยู่แล้ว + reconcile job รายวันเทียบกับ Stripe API เป็น safety net |
| **Webhook ยิงซ้ำ / replay** | unique index บน `StripeEventId` ที่ระดับ DB — ไม่ใช่แค่เช็คใน memory |
| **Webhook ปลอม** | ตรวจ `Stripe-Signature` ด้วย signing secret ทุก request — ไม่ผ่านตอบ 400 ทันที ห้าม process |
| **ผู้ซื้อสแกน QR เดิมซ้ำหลังจ่ายแล้ว** | Stripe คืนเงินส่วนเกินเข้า balance เราพร้อมแจ้งเตือน — ต้อง refund คืนผู้ซื้อ (มี ops queue รองรับ) |
| **จ่ายหลัง order หมดอายุ** | ยึดผลจาก Stripe: ถ้าเงินเข้าจริง เปิด order ให้ใหม่อัตโนมัติหรือ refund — ห้ามกลืนเงินเงียบ ๆ |
| **Refund ล้มเหลว** | refund ของ PromptPay เป็น async — Stripe ติดต่อขอเลขบัญชีจากผู้ซื้อเอง ถ้าผู้ซื้อไม่ตอบ refund ค้าง → track สถานะ + ops queue ตามงาน |
| **Stripe API ล่มตอนสร้าง PaymentIntent** | retry แบบ exponential backoff → ไม่สำเร็จแจ้งผู้ซื้อให้ลองใหม่ (ยังไม่มีเงินออกจากกระเป๋าใคร) |
| **บัญชี Stripe ถูก freeze/review** | ความเสี่ยงของ provider เดียว — monitor payout เข้าบัญชีจริงสม่ำเสมอ; สถาปัตยกรรม `IPaymentMethod` เผื่อเสียบเจ้าอื่นไว้แล้ว |

## ข้อบังคับด้าน implementation

- ราคาคำนวณที่ server เท่านั้น — request รับได้แค่ course/bundle id + promo code
- **ยืนยันการจ่ายจาก webhook + signature เท่านั้น** — ห้ามเชื่อ redirect/return_url/สถานะจาก frontend (ใช้ได้แค่แสดงผลชั่วคราวระหว่างรอ webhook)
- การเปลี่ยนสถานะ Payment/Order จาก webhook ต้องอยู่ใน transaction เดียว (กัน race กับ expiry job และ webhook ซ้ำ)
- `StripeWebhookEvents.StripeEventId` ต้องมี unique index — **ไม่ใช่แค่ `if (exists)` ในโค้ด**
- Secret key / webhook signing secret อยู่ใน user-secrets (dev) / env (prod) เท่านั้น; frontend เห็นได้แค่ publishable key
- ทุกการเปลี่ยนสถานะเงิน + ทุกการตัดสินของ admin ต้องเขียน audit ที่แก้ย้อนหลังไม่ได้
- เก็บ raw event JSON จาก Stripe ไว้ในตาราง เพื่อใช้ตรวจสอบย้อนหลังและ dispute
- ใช้ SDK ทางการ **Stripe.net** — auth ผ่าน `StripeClient` ด้วย secret key (ห้าม hardcode, ห้าม log)
- เตรียม `IPaymentMethod` ตั้งแต่ต้น เพื่อเสียบ gateway อื่น (ผ่อนชำระ) ในอนาคต

## Admin amount override — ปรับยอดที่ส่งให้ Stripe (เพิ่ม 2026-10-10)

ใช้ทดสอบ flow เงินจริงบน production ด้วยยอดน้อย ๆ (เช่น ฿20) โดยไม่ต้องแก้ราคาคอร์ส · หน้าจอ `/admin/payment-settings` → `PUT /api/commerce/admin/payment-amount-override` (AdminOnly)

- **ยอดคงที่ทั้งระบบ**: เปิด/ปิด + ยอด THB เดียว — ขณะเปิด ทุก PaymentIntent ที่ **สร้างใหม่** (PromptPay และบัตร) จะถูกตัดที่ `min(ยอด override, ยอดออเดอร์)` · **ลดได้อย่างเดียว ไม่มีทางเก็บเกินราคาจริง** · ออเดอร์ฟรีไม่เกี่ยว · PaymentIntent ที่สร้างไปก่อนเปิด override ไม่เปลี่ยนยอด
- **ตั้งค่า = audit**: ตาราง `PaymentAmountOverrides` append-only (ทุกการเปลี่ยนคือแถวใหม่ ไม่มี update/delete) แถวล่าสุดคือค่าที่ใช้อยู่ · เก็บ admin ที่เปลี่ยน (จาก token ไม่ใช่ request body), เวลา, เหตุผล (บังคับ 3–500 ตัวอักษร) · ทุกการเปลี่ยนเขียน log ระดับ Warning ด้วย
- **`Payments.Amount` = เงินที่ตัดจริง**; `Payments.OriginalAmount` = ยอดออเดอร์เดิม (มีค่าเฉพาะ payment ที่ถูก override) · ออเดอร์ (`Orders.TotalAmount`) ไม่ถูกแก้
- **Revenue split ตามยอดที่ตัดจริง** (ตัดสินใจโดยเจ้าของโปรเจ็ค 2026-10-10): `StripeWebhookHandler` ย่อ gross ของแต่ละ item ด้วย `Amount / OriginalAmount` ผ่าน `PaidAmountAllocator` (เศษสตางค์ลง item สุดท้าย ผลรวมเท่ายอดที่ตัดจริงเป๊ะ) ค่าธรรมเนียม Stripe จริงยังถัวตามสัดส่วน item เหมือนเดิม
- **Refund** อิง `Payments.Amount` อยู่แล้ว → คืนได้ไม่เกินเงินที่ตัดจริง · อีเมลใบเสร็จแสดงยอดที่ตัดจริง
- **ไม่ออกใบเสร็จ PDF / ใบกำกับภาษี** ให้ออเดอร์ที่จ่ายผ่าน override (ตอบ 409) — เอกสารพิมพ์ยอดระดับออเดอร์ ซึ่งจะสูงกว่าที่จ่ายจริง
- Stripe บังคับยอดขั้นต่ำของตัวเอง — ถ้า override ต่ำกว่า จะได้ `payment.provider_error` ตอนสร้างการชำระเงินครั้งถัดไป (ไม่มีการเช็คขั้นต่ำฝั่งเราเพราะไม่ยืนยันตัวเลขจาก docs)
- ⚠️ **อย่าลืมปิดหลังทดสอบ** — ขณะเปิด ลูกค้าจริงทุกคนจะถูกตัดตามยอดนี้ (หน้า admin แสดงแบนเนอร์เตือน)

## ตารางที่เพิ่ม/เปลี่ยนจาก `DATABASE.md`

```
commerce.Payments(Id PK, OrderId FK, Method,          -- PromptPay | (อนาคต) Card
                  Provider,                            -- 'Stripe'
                  ProviderPaymentIntentId,             -- UQ ← ผูก 1:1 กับ Stripe PaymentIntent
                  Amount, Status,                      -- Pending|Processing|Succeeded|Failed|Expired|Refunded
                  OriginalAmount NULL,                 -- เฉพาะ payment ที่ admin override ลดยอด
                  SucceededAtUtc, FailureReason, CreatedAtUtc)

commerce.PaymentAmountOverrides(Id PK, IsEnabled, OverrideAmount, Reason,   -- append-only; แถวล่าสุด = ค่าที่ใช้
                  ChangedByUserId, ChangedAtUtc)

commerce.StripeWebhookEvents(Id PK,
                  StripeEventId,                       -- UQ ← กัน replay/ยิงซ้ำ (สำคัญที่สุด)
                  EventType, PayloadJson nvarchar(max),-- เก็บ raw ไว้ dispute/ตรวจย้อนหลัง
                  ReceivedAtUtc, ProcessedAtUtc, ProcessResult)

commerce.PaymentOpsQueue(Id PK, PaymentId FK, Reason, Status,   -- จ่ายซ้ำ/จ่ายหลังหมดอายุ/refund ค้าง
                  AssignedToUserId, ResolvedByUserId, ResolvedAtUtc, Note)

-- ❌ ตัดออกจากดีไซน์เดิม (ยังไม่เคยสร้างจริง ไม่ต้อง migrate):
--    PaymentSlips, MerchantAccounts, PaymentReviewQueue — เป็นของ flow อัปสลิป/EasySlip ทั้งหมด
```

> **ก่อน implement (P3):** เปิด doc Stripe เช็ค API version ปัจจุบัน + ชื่อ field ของ `next_action.promptpay_display_qr_code` อีกครั้ง และยืนยันเรตค่าธรรมเนียม PromptPay/บัตรของ Stripe Thailand จาก dashboard จริง — **ตัวเลขค่าธรรมเนียมต้องเข้าไปอยู่ในสูตร revenue split (Q4) ด้วย**
