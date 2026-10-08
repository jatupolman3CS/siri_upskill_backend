# Contract: P11-12 Refund hard-block หลังผู้เรียนเข้าห้องสดแล้ว (Q13.4)

Status: FROZEN · วันที่: 2026-10-06 · Module: `Siri.Modules.Commerce` (Repository+Service, UPPERCASE entity/DB — `RefundService`) + อ่านจาก `Siri.Modules.Live.Contracts.ILiveAttendanceReader` (นิยามใน P11-05 §3.1)

ผู้ลงมือที่แนะนำ: **Claude `backend-developer`** — แก้ตรรกะ refund (เงิน) ตามเกณฑ์ที่ห้ามส่ง Antigravity และต้องห้ามเดาเอง (`security.md`: "เปลี่ยนวิธีคิดเงิน ... refund ต้องหยุดแล้วถาม") — **การตัดสินใจเชิงธุรกิจถูกตอบแล้วที่ Q13.4**; ส่วนที่ยังเป็นการตีความ (refund รายคอร์สใน order หลายคอร์ส) บันทึกเป็น default ใน §1 และ **รายงานให้เจ้าของโปรเจ็คยืนยัน** · ไม่ต้อง DATABASE (ไม่มี schema delta)

**Dependency:** P11-05 (ตาราง `SESSION_JOIN_LOGS` + `ILiveAttendanceReader` implement แล้ว) · ถ้า Live ยังไม่ merge ให้ทำ P11-12 ไม่ได้ (Commerce ต้อง reference `Siri.Modules.Live`)

---

## 0. ข้อเท็จจริงจากโค้ด (อ่าน `RefundService.cs`, `REFUND.cs`, `ORDER_ITEM.cs`, `RefundsController.cs`)

| # | ข้อเท็จจริง | ผล |
|---|---|---|
| F1 | `RefundService.RequestAsync(userId, {PaymentId, Amount, Reason})` — โหลด payment → order → เช็ค `order.USER_ID == userId` (403) → `PAYMENT.STATUS == Succeeded` (400) → `0 < Amount <= payment.AMOUNT` (400) → `REFUND.Request(...)` | จุดแทรก gate = **หลัง** เช็ค amount, **ก่อน** `REFUND.Request` |
| F2 | **`REFUND` ผูกกับ `PAYMENT` ทั้งก้อน** (`PAYMENT_ID`, `AMOUNT` อิสระ) ไม่มีโครง per-`ORDER_ITEM`; `ApproveAsync` ย้อน revenue split ของ **ทั้ง order** + คืน promo redemption แต่ **ไม่ revoke enrollment** | "refund รายคอร์ส" ไม่มีใน schema — TASKS.md สั่งให้รายงานก่อนแก้ → **ตัดสินใจ default ใน §1** (ไม่แก้ schema REFUND) |
| F3 | `ORDER_ITEM.LINE_TOTAL` = `FinalLineTotal` หลังส่วนลดต่อรายการ (PricingEngine) — ผลรวม ≈ ยอด order; มี `COURSE_ID` (nullable) + `TITLE_SNAPSHOT` | ใช้ `LINE_TOTAL` เป็นฐานสัดส่วนของคอร์สที่ถูกบล็อก |
| F4 | `RefundService` ใช้ optional ctor param สำหรับ cross-module dep (`IRevenueSplitContract? revenueSplitContract = null`) | เพิ่ม `ILiveAttendanceReader? liveAttendance = null` แบบเดียวกัน — host ที่ไม่มี Live ทำงานเหมือนเดิม |
| F5 | `/api/commerce/refunds` เป็น `[Authorize]` ระดับ class, ไม่มี rate-limit ระบุ (`RefundsController.cs`) | **นอกขอบเขต** (ไม่เปลี่ยน); บันทึกข้อสังเกตให้ integrator-qa |

## 1. การตัดสินใจที่บันทึกไว้ (default — เจ้าของโปรเจ็คเปลี่ยนได้ด้วย revision)

**Q13.4:** ห้าม refund ทันทีที่ผู้เรียนเข้าคาบสดไปแล้วอย่างน้อย 1 ครั้ง — ปฏิเสธที่ระบบ (409) ไม่เข้าคิว admin · เหตุผล refund อื่น (เนื้อหาไม่ตรง/เทคนิคฝั่งแพลตฟอร์ม) ใช้กระบวนการเดิม

**ตีความ "รายคอร์สใน order หลายคอร์ส" (ไม่มี per-item refund ใน schema):** ใช้ **เพดานยอดคืนสูงสุด** แทนการแยกคำขอรายคอร์ส —
```
blockedCourseIds = ILiveAttendanceReader.GetCourseIdsAttendedAsync(order.USER_ID, order.ORDER_ITEMS.Where(COURSE_ID != null).Select(COURSE_ID))
blockedLine      = Σ LINE_TOTAL ของ item ที่ COURSE_ID ∈ blockedCourseIds
totalLine        = Σ LINE_TOTAL ของทุก item
blockedAmount    = totalLine > 0 ? Round(payment.AMOUNT × blockedLine / totalLine, 2, MidpointRounding.AwayFromZero) : payment.AMOUNT
maxRefundable    = Max(0, payment.AMOUNT − blockedAmount)
command.Amount > maxRefundable  →  409 (ไม่สร้าง REFUND)
```
ผล: คอร์สเดียวที่เข้าเรียนสดแล้ว → ขอคืนส่วนของคอร์สอื่นได้ตามสัดส่วน, ขอเกินเพดาน/ขอคืนเต็มยอด → 409 · ทุกคอร์สใน order เข้าเรียนแล้ว → `maxRefundable = 0` → ขอไม่ได้เลย · order ที่ไม่มีคอร์สสด/ไม่มี join log → ไม่เปลี่ยนพฤติกรรมเดิมเลย
**ข้อจำกัดที่ยอมรับ (บอกเจ้าของ):** `ApproveAsync` ยังย้อน revenue split ทั้ง order ตามพฤติกรรมเดิม (ไม่ใช่เรื่องของ task นี้ — ถ้าจะอนุมัติ refund บางส่วนจริงต้องมีงานแยกให้ย้อนเฉพาะ item) · **ไม่ re-check ตอน admin approve** (คำขอที่ยื่นก่อนผู้เรียนเข้าห้อง แล้ว admin อนุมัติหลังเข้า = admin ตัดสินใจเอง) · ผู้ซื้อ ≠ ผู้เรียน (gift) ไม่มีใน v1

## 2. Schema delta: **none**

## 3. Code changes (ทั้งหมดอยู่ใน Commerce)
1. `Siri.Modules.Commerce.csproj`: `ProjectReference` → `Siri.Modules.Live` (เฉพาะใช้ `Siri.Modules.Live.Contracts`) · ตรวจ `ArchitectureTests` ว่าไม่มีการอ้าง `Live.Domain/Infrastructure`
2. `RefundService` เพิ่ม ctor param ท้ายสุด `ILiveAttendanceReader? liveAttendance = null` (ไม่เปลี่ยนลำดับ param เดิม — เทสต์เดิมสร้าง instance แบบ positional)
3. ใน `RequestAsync` **หลัง** บล็อก `command.Amount <= 0 || > payment.AMOUNT` และ **ก่อน** `REFUND.Request(...)`: ใส่ logic §1 ผ่านเมธอด private `ComputeLiveAttendanceCeilingAsync(order, payment, ct)` คืน `(decimal maxRefundable, IReadOnlyList<string> blockedTitles)?` (`null` = ไม่มีการบล็อก)
4. error: `DomainError.Conflict("คุณเข้าร่วมคาบสอนสดของคอร์สในรายการนี้แล้ว จึงขอคืนเงินส่วนของคอร์สนั้นไม่ได้").WithReason("refund.live_attended", { blockedCourseTitles = […], maxRefundableAmount = maxRefundable, blockedAmount })` (ใช้ `DomainError.Reason/Extensions` จาก P11-03 §4.5) → 409 ผ่าน `ToProblemHttpResult` เดิม · ความลับ: ไม่ใส่ session id/เวลาเข้า/ชื่อผู้สอนใน response
5. ไม่แก้ `REFUND`, `RefundRepository`, `ApproveAsync/RejectAsync`, controller, DTO ของ refund
6. DI: ไม่ต้องลงทะเบียนเพิ่ม (`ILiveAttendanceReader` ถูกลงทะเบียนโดย `AddLiveModule`; param เป็น optional)

## 4. Frontend notes (appendix §D)
หน้า order history (`/account/orders`, ปุ่มขอคืนเงิน) ต้องรับ ProblemDetails `status=409, reason="refund.live_attended"` แล้วแสดงข้อความ i18n (`refund.liveAttended`) พร้อม `maxRefundableAmount` (ถ้า > 0 ให้เสนอปรับยอดขอคืน) · เพิ่มข้อความแจ้งผู้เรียน **ก่อน** เข้าห้องครั้งแรก (หน้า `/live/:sid/join` และแท็บ "สอนสด" เมื่อ `hasAttendedAnySession=false`): "การเข้าร่วมคาบสอนสดครั้งแรกจะทำให้ขอคืนเงินคอร์สนี้ไม่ได้ ตามนโยบายการคืนเงิน" — กันผู้เรียนเสียสิทธิ์โดยไม่รู้ตัว (server ไม่บังคับ — เป็นข้อมูลเท่านั้น)

## 5. Test checklist
**Unit (`RefundServiceTests` เพิ่ม — fake `ILiveAttendanceReader`):** ไม่มี attended → พฤติกรรมเดิมทุกกรณี (regression ครบชุดเดิมผ่านโดยไม่แก้ assertion) · 1 คอร์ส attended (order 1 คอร์ส) ขอเต็มยอด → 409, ขอ 1 สตางค์ → 409 (`maxRefundable=0`) · order 2 คอร์ส (฿1,000/฿500) attended คอร์ส ฿1,000, `payment.AMOUNT=1,500`: ขอ ฿500 → ผ่าน, ฿500.01 → 409, ext `maxRefundableAmount=500.00` · ส่วนลดทำให้ LINE_TOTAL ≠ UNIT_PRICE (ใช้ LINE_TOTAL) · `AMOUNT ≠ ΣLINE_TOTAL` ใช้สัดส่วนถูกและ rounding AwayFromZero · item ไม่มี `COURSE_ID` (bundle) ไม่ถูกบล็อก · `totalLine == 0` + มี attended → `maxRefundable=0` · `liveAttendance == null` → ไม่เช็ค · reader ถูกเรียกด้วย `order.USER_ID` (ไม่ใช่ id จาก command) · ownership 403 ยังมาก่อน gate
**Integration:** end-to-end ผ่าน `SiriApiFactory`: สร้างคอร์ส Live+OnDemand ใน order เดียว (จ่ายจริงผ่านทาง test helper ของ `PaymentFulfillmentIntegrationTests`), ผู้เรียน `POST …/join` สำเร็จ 1 ครั้ง (มี join log) → `POST /api/commerce/refunds` เต็มยอด → 409 `refund.live_attended`; ขอเท่าส่วนของคอร์ส OnDemand → 201 · ผู้เรียนที่ **ไม่เคยเข้า** → refund ผ่านตามเดิม · join log ของ **ผู้สอน** (Role=Instructor) ไม่ทำให้ผู้ซื้อถูกบล็อก (เพราะ reader นับเฉพาะ Learner)

## 6. Work package
| WP | ใคร | ทำอะไร | Acceptance |
|---|---|---|---|
| **A** | `backend-developer` | §3 ทั้งหมด + unit §5 + integration §5 | build 0 warning · `RefundServiceTests` เดิมผ่านไม่แก้ assertion · architecture เขียว |
บอกเจ้าของโปรเจ็คในรายงานส่งงาน: การตีความ "เพดานยอดคืน" (§1) และข้อจำกัด `ApproveAsync` ย้อน split ทั้ง order

## Changelog
(ยังไม่มี revision หลัง FROZEN)
