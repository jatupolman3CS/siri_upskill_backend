# Contract: P11-09 + P11-24 — Credit/debit card checkout (Stripe PaymentIntent, D-21)

Status: FROZEN · วันที่: 2026-09-16 · Module: `Siri.Modules.Commerce` (Repository+Service, UPPERCASE entity/DB) + `Siri.Integrations.Payment` (provider adapter, PascalCase — leaf project, no UPPERCASE naming) + `Siri.Modules.Payout` (Repository+Service, UPPERCASE — **contract already supports this task, no change needed there**, see §1.4)

ผู้ลงมือที่แนะนำ: **backend-developer (Claude)** ทั้งสองฝั่ง BE/FE — งานนี้แตะ (ก) เงิน/webhook fulfillment ที่ enroll คนเรียนจริง (ข) ต้องแก้ signature ของ `IPaymentMethod` ซึ่งเป็น contract ข้าม project (`Siri.Integrations.Payment` ↔ `Siri.Modules.Commerce`) (ค) revenue-split fee wiring ที่กระทบ Q4's formula จริง — ตรงตามเกณฑ์ charter ("งานแตะเงิน/access-right/เพิ่มแก้ Contracts/ ข้ามโมดูล → Claude ไม่ใช่ Antigravity") ทั้งสองฝั่งพร้อมเริ่มขนานกันได้ทันทีที่ contract นี้ freeze เพราะ DTO shape (`PaymentResponse.clientSecret`, `PaymentConfigResponse`) ไม่เปลี่ยนจากที่กำหนดไว้ที่นี่แล้ว — **BE เป็นเจ้าของทุกไฟล์ใต้ `src/`, FE เป็นเจ้าของทุกไฟล์ใต้ `siri_upskill_ui/src/`** ไม่มีไฟล์ไหนที่ทั้งสองฝั่งต้องแก้ร่วมกัน

**DESIGN_DATABASE/DATABASE: ไม่จำเป็น** — §2 schema delta = **none** ทั้งหมด (ดูเหตุผลแต่ละจุดใน §2)

---

## 0. สิ่งที่ยืนยันจากโค้ดจริงแล้ว (ก่อนออกแบบ — ไม่เดา)

อ่าน method body จริงของทุกไฟล์ที่คำสั่งงานระบุแล้ว สรุปสิ่งที่เกี่ยวกับการออกแบบ:

- **`src/Siri.Integrations.Payment/Stripe/StripePaymentMethod.cs:54-75`** — `CreatePaymentIntentAsync` ฮาร์ดโค้ด `PaymentMethodTypes=["promptpay"]`, `Confirm=true`, และ `PaymentMethodData.Type="promptpay"` เสมอ ไม่มีการแยกตาม method เลย นี่คือจุดเดียวที่ต้องแตกสาขา
- **`src/Siri.Integrations.Payment/IPaymentMethod.cs`** — `CreatePaymentIntentRequest` (record, บรรทัด 32-38) **ไม่มีฟิลด์ method เลย** และ `Siri.Integrations.Payment` project reference แค่ `Siri.SharedKernel` (เช็คจาก `.csproj` แล้ว) **ไม่ reference `Siri.Modules.Commerce`** — จะใช้ `Siri.Modules.Commerce.Domain.PaymentMethod` enum ตรงในนี้ไม่ได้ (ผิดทิศทาง dependency) ต้องมี enum ใหม่ใน `Siri.Integrations.Payment` เอง แล้วให้ `PaymentService` เป็นคน map
- **`src/Siri.Modules.Commerce/Domain/PaymentMethod.cs:8`** — `public enum PaymentMethod { PromptPay, Card }` — **`Card` มีอยู่แล้ว** ไม่ต้องเพิ่ม enum value
- **`src/Siri.Modules.Commerce/Application/CreatePaymentValidator.cs`** — `RuleFor(c => c.Method).IsInEnum()` เท่านั้น **ไม่มีอะไรปฏิเสธ `Card` เลยที่ชั้น validator** — DTO/validator ไม่ใช่จุดที่บล็อก
- **`src/Siri.Modules.Commerce/Application/PaymentService.cs:76-83`** — `CreateAsync` เรียก `CreatePaymentIntentAsync(new CreatePaymentIntentRequest(order.ORDER_ID, order.ORDER_NO, order.TOTAL_AMOUNT, order.CURRENCY, CustomerEmail: customerEmail), ct)` — **ไม่เคยส่ง `command.Method` เข้าไปเลย** นี่คือช่องโหว่ตัวจริงที่ทำให้ `Card` "unreachable end-to-end" ตามที่คำสั่งงานสงสัย ไม่ใช่ validator หรือ DB schema
- **`src/Siri.Api/Controllers/Commerce/PaymentsController.cs`** — MVC controller ตัวจริงที่ live (D-19) ชั้น `[Authorize]`, มีแค่ `GET {id}` กับ `POST` — ไม่มี route `config`
- **`src/Siri.Modules.Commerce/Application/PaymentEndpoints.cs`** — minimal-API mapper คู่ขนานที่ `Program.cs` **ไม่เรียกแล้ว** (`app.MapControllers()` เท่านั้น, เช็คแล้วด้วย grep) แต่ `_app.MapCommerceEndpoints()` ยังถูกเรียกตรงในเทสต์เก่า 4 ไฟล์ (`OrderEndpointsTests.cs`, `OrderExpiryIntegrationTests.cs`, `PayoutIntegrationTests.cs`, `PromoCodeTests.cs`) และ `docs/contracts/P11-01-catalog-live-sessions.md` (FROZEN แล้วในเวฟเดียวกัน) วางกฎไว้แล้วว่า endpoint ใหม่ทุกตัวต้องมี mapper คู่กัน — **ทำตาม precedent นั้น** (§1.3 ด้านล่าง)
- **`src/Siri.Modules.Commerce/Application/StripeWebhookHandler.cs:135-254`** (`HandlePaymentIntentSucceededAsync`) — **ยืนยันแล้วว่า method-agnostic จริง**: ทั้งฟังก์ชันคีย์ด้วย `_paymentRepository.GetByProviderPaymentIntentIdAsync(paymentIntent.Id, ...)` (หา `PAYMENT` row จาก Stripe PaymentIntent id) ไม่มี `if (payment.METHOD == ...)` ที่ไหนเลยในทั้งไฟล์ — **อย่าเพิ่ม branch ตาม method ที่นี่เด็ดขาด ไม่มีเหตุผลต้องมี** จุดเดียวที่ต้องแก้คือเพิ่มการอ่าน payment fee (§1.4)
- **`PaymentResponse` (`PaymentService.cs:120-129`)** — มี `ClientSecret` อยู่แล้วจริง (บรรทัด 127, `string? ClientSecret = null`) และฝั่ง FE (`commerce-api.models.ts:32-40`) มี `clientSecret?: string` รออยู่แล้วเช่นกัน — ยืนยันคำกล่าวอ้างของคำสั่งงานว่า "ไม่เคยถูกอ่าน" ถูกต้อง (ไม่มี component ไหนอ่าน `payment().clientSecret` เลยตอนนี้)
- **`checkout-page.ts`/`.html`** — บัตรเครดิตเป็น `<div>` ที่ `cursor-not-allowed opacity-60` (บรรทัด 90-109 ของ `.html`) มี badge "เร็ว ๆ นี้" (`checkout.methods.comingSoon`) ตาย ไม่มี `(click)` เลย — และ **ไม่มี statement-descriptor disclosure text ที่ไหนในหน้านี้เลยแม้แต่สำหรับ PromptPay** (grep ทั้งไฟล์แล้ว ไม่เจอ "STRIPE PAYMENTS" หรือคำแปลไทยเลย) — `PAYMENT.md`'s ข้อบังคับข้อนี้ยังไม่เคย implement มาก่อน ไม่ใช่แค่ยังไม่ครอบคลุม Card
- **`@stripe/stripe-js`** — ไม่อยู่ใน `package.json` เลย (grep แล้ว) ต้องเพิ่มใหม่
- **`Stripe.net` เวอร์ชันจริงที่ล็อกไว้ (`Directory.Packages.props:46`) = `52.3.0`** — เช็คจาก DLL string table แล้ว (ไม่มี decompiler ในสภาพแวดล้อมนี้ เช็คได้แค่ชื่อ public member ผ่าน string extraction): ยืนยันว่า `PaymentIntent.LatestCharge`/`LatestChargeId`, `BalanceTransaction.Fee`, และ `PaymentIntentGetOptions` มีอยู่จริงในแอสเซมบลีนี้ — **backend-developer ต้องเปิด IntelliSense/Object Browser ยืนยัน `PaymentIntentGetOptions.Expand` เป็น `List<string>` ก่อนใช้จริง** (ตาม backend.md ข้อ 2 "ห้ามเดา API" — สิ่งที่ยืนยันได้จากที่นี่คือ type มีอยู่จริง ไม่ใช่ signature ทุก property)

---

## 1. Backend changes (`siri_upskill_backend`)

### 1.1 `Siri.Integrations.Payment` — `IPaymentMethod` + `StripePaymentMethod`

**`IPaymentMethod.cs`** — เพิ่ม enum ใหม่ (ไม่ยืม `Commerce.Domain.PaymentMethod` เพราะทิศทาง dependency ผิด, ดู §0):

```csharp
public enum PaymentMethodType { PromptPay, Card }
```

แก้ `CreatePaymentIntentRequest` — เพิ่ม `Method` เป็นพารามิเตอร์ตัวสุดท้ายพร้อม default `PromptPay` (**ไม่ใช่แทรกกลาง**) — เหตุผล: record นี้เป็น positional พารามิเตอร์เดิม (`Currency`/`Description`/`CustomerEmail`) มี default อยู่แล้ว การแทรก `Method` (required) ไว้ก่อนพวกนั้นจะ compile-break ทุก call site รวม `tests/Siri.UnitTests/Payment/StripePaymentMethodTests.cs` ที่เรียก `new CreatePaymentIntentRequest(Guid.NewGuid(), "ORD-2026-0001", 0m)` ตรง ๆ (3 อาร์กิวเมนต์) — ใส่ท้ายสุดพร้อม default `PromptPay` แทน ทำให้ call site เดิมทั้งหมด **compile ผ่านและพฤติกรรมเดิมไม่เปลี่ยนแม้แต่บิตเดียว** โดยไม่ต้องแก้ไฟล์เทสต์เดิมเลย:

```csharp
public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    string OrderNo,
    decimal Amount,
    string Currency = "thb",
    string? Description = null,
    string? CustomerEmail = null,
    PaymentMethodType Method = PaymentMethodType.PromptPay);
```

เพิ่ม method ใหม่ในอินเทอร์เฟซ (สำหรับ §1.4 — payment fee):

```csharp
/// <summary>Best-effort lookup ของค่าธรรมเนียมจริงที่ Stripe หักจาก charge ของ PaymentIntent นี้
/// (หน่วยเดียวกับ Amount คือ THB ไม่ใช่สตางค์). สัญญา: คืน <c>Result.Success(null)</c> เสมอเมื่อหาค่าไม่ได้
/// (transient API error, fee ยังไม่ settle, ฯลฯ) — ไม่เคยคืน Failure เพราะผู้เรียกต้องมี fallback
/// (ค่า config <c>Payout:EstimatedPaymentFeePercent</c>, ดู docs/DECISIONS.md Q4) อยู่แล้วเสมอ
/// และห้ามให้การเรียกนี้ทำให้ payment fulfillment ล้มเหลว.</summary>
Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken);
```

**`StripePaymentMethod.CreatePaymentIntentAsync`** — แตกสาขาตาม `request.Method`, PromptPay path **คงเดิมทุกบรรทัด** (byte-for-byte เดิม) เพื่อไม่เสี่ยง regress ของจริงบน production:

```csharp
var options = request.Method == PaymentMethodType.Card
    ? new PaymentIntentCreateOptions
      {
          Amount = amountInSatang,
          Currency = request.Currency.ToLowerInvariant(),
          PaymentMethodTypes = ["card"],
          Confirm = false,                         // client (Stripe.js Payment Element) confirms
          // ไม่ใส่ PaymentMethodData เลย — client เป็นคนส่ง payment method details ตอน confirmPayment()
          Description = request.Description ?? $"Order {request.OrderNo}",
          ReceiptEmail = request.CustomerEmail,
          Metadata = new Dictionary<string, string> { { "orderId", request.OrderId.ToString() }, { "orderNo", request.OrderNo } },
      }
    : new PaymentIntentCreateOptions
      {
          // เดิมทุกบรรทัด — ห้ามแก้ path นี้เลย
          Amount = amountInSatang,
          Currency = request.Currency.ToLowerInvariant(),
          PaymentMethodTypes = ["promptpay"],
          Confirm = true,
          PaymentMethodData = new PaymentIntentPaymentMethodDataOptions
          {
              Type = "promptpay",
              BillingDetails = new PaymentIntentPaymentMethodDataBillingDetailsOptions { Email = request.CustomerEmail },
          },
          Description = request.Description ?? $"Order {request.OrderNo}",
          ReceiptEmail = request.CustomerEmail,
          Metadata = new Dictionary<string, string> { { "orderId", request.OrderId.ToString() }, { "orderNo", request.OrderNo } },
      };
```

- แก้ข้อความ error ที่บรรทัด 46-49 จาก `"A customer email is required for PromptPay."` เป็น `"A customer email is required to process payment."` — เช็คแล้วว่าการเช็คนี้ไม่มีเงื่อนไขตาม method อยู่แล้ว (fire ทั้งสอง method เหมือนกัน) ข้อความเดิมแค่พูดผิด ไม่ใช่ behavior ผิด แก้เพราะกำลังแตะบรรทัดนี้อยู่แล้ว ไม่ใช่ scope creep
- `MapPaymentIntent` **ไม่ต้องแก้เลย** — คืน `ClientSecret`/`Status` เหมือนกันทุก method อยู่แล้ว, `QrCodeUrl`/`QrCodeData` เป็น null โดยธรรมชาติสำหรับ Card เพราะ `NextAction` จะ null เมื่อ `Confirm=false` (ไม่ได้ confirm เลยจนกว่า client จะเรียก `confirmPayment`)

เพิ่ม `GetChargeFeeAsync` implementation ใหม่ (ไม่ห่อด้วย `ExecuteWithRetryAsync` — ตั้งใจ, ดูเหตุผลใน §1.4):

```csharp
public async Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentIntentId);
    var service = new PaymentIntentService(_stripeClient);
    try
    {
        var intent = await service.GetAsync(
            providerPaymentIntentId,
            new PaymentIntentGetOptions { Expand = ["latest_charge.balance_transaction"] },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var feeInSatang = intent.LatestCharge?.BalanceTransaction?.Fee;
        if (feeInSatang is null)
        {
            _logger.LogWarning("PaymentIntent {Id}: balance_transaction.fee not yet available; falling back to estimated fee.", providerPaymentIntentId);
            return Result.Success<decimal?>(null);
        }
        return Result.Success<decimal?>(feeInSatang.Value / 100m);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogWarning(ex, "GetChargeFeeAsync failed for PaymentIntent {Id}; falling back to estimated fee.", providerPaymentIntentId);
        return Result.Success<decimal?>(null);
    }
}
```

**เหตุผลที่ไม่ retry / ไม่ expand ผ่าน webhook payload เอง (ตอบคำถามคำสั่งงานตรง ๆ):**
- **ไม่ expand ที่ตัว webhook event เอง** — Stripe ไม่ได้ expand nested object ในตัว event payload ที่ส่งมาเองโดย default การจะได้ `balance_transaction` ติดมากับ event ต้องไปตั้งค่า "webhook endpoint snapshot expansions" ที่ฝั่ง Stripe Dashboard/API ล่วงหน้า ซึ่งเป็น operational step แยกที่ไม่มีใครตั้งไว้ตอนนี้ (เช็คแล้วจาก `docs/PAYMENT.md`/`StripeWebhookController.cs` ไม่มีร่องรอยการตั้งค่านี้เลย) — เพิ่ม dependency ไปที่ config ภายนอกที่มองไม่เห็นจากโค้ด ผิดหลัก "ห้ามอ่าน config กระจาย ต้องเห็นชัดจากโค้ด"
- **เลือก follow-up `GET` call แทน** — ใช้ `IStripeClient`/`PaymentIntentService` ตัวเดียวกับที่ `GetPaymentIntentAsync` ใช้อยู่แล้ว ไม่ต้องตั้งค่าอะไรเพิ่มที่ Stripe Dashboard, ยืนยันได้ทันทีจาก dev/test mode
- **ไม่ห่อด้วย `ExecuteWithRetryAsync`** — การเรียกนี้เกิด**ภายใน** DB transaction เดียวกับ webhook fulfillment (`StripeWebhookHandler.HandleAsync`'s `_orderRepository.ExecuteInTransactionAsync(...)`, §1.4) การ retry แบบเดิม (สูงสุด 3 ครั้ง, 500ms/1s/2s) จะยืดเวลาที่ DB transaction ค้างไว้ถึง ~3.5 วินาทีในกรณีเลวร้ายที่สุด ซึ่งเสี่ยง lock contention กับ webhook/expiry job อื่นมากกว่าที่ควร — เรียกครั้งเดียว ถ้าพังก็ fallback เป็น null ทันที (มี `EstimatedPaymentFeePercent` รองรับอยู่แล้ว) ปลอดภัยกว่าการยืดธุรกรรมที่กระทบเงิน/สิทธิ์เรียนจริง

### 1.2 `Siri.Modules.Commerce` — `PaymentOptions` ใหม่ (`Payment:EnabledMethods`)

ไฟล์ใหม่ `src/Siri.Modules.Commerce/PaymentOptions.cs` (namespace `Siri.Modules.Commerce`, ตำแหน่งเดียวกับ `OrderExpiryOptions.cs` ที่มีอยู่แล้ว — pattern เดิม):

```csharp
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>วิธีชำระเงินที่เปิดใช้งานจริงตอนนี้ — เป็น operational switch แยกจากการ deploy โค้ด
    /// เพื่อให้เจ้าของโปรเจ็คเปิด Card ได้ก็ต่อเมื่อเปิดใน Stripe Dashboard จริงแล้วเท่านั้น (ดู OWN
    /// dependency ใน docs/TASKS.md P11-09) ค่า default มีแค่ PromptPay — ต้องตั้ง config/env เพิ่ม
    /// "Payment:EnabledMethods": ["PromptPay","Card"] เองถึงจะเปิด Card ได้จริง.</summary>
    public List<PaymentMethod> EnabledMethods { get; set; } = [PaymentMethod.PromptPay];
}
```

ลงทะเบียนใน `CommerceModule.AddCommerceModule` (ต่อจากบล็อก `StripeOptions` เดิม):

```csharp
services.AddOptions<PaymentOptions>()
    .Bind(configuration.GetSection(PaymentOptions.SectionName))
    .ValidateOnStart();
```

ใน `PaymentService`: inject `IOptions<PaymentOptions>` ใหม่, ใน `CreateAsync` เช็คก่อนเรียก Stripe เลย (fail-fast, ไม่เปลือง API call):

```csharp
if (!_paymentOptions.EnabledMethods.Contains(command.Method))
{
    return Result.Failure<PaymentResponse>(DomainError.Validation(
        $"วิธีชำระเงิน {command.Method} ยังไม่เปิดใช้งานในขณะนี้ กรุณาเลือกวิธีอื่น"));
}
```

`PaymentService.CreateAsync` เรียก `CreatePaymentIntentAsync` ต้องส่ง `Method:` ด้วยแล้ว (mapping `Commerce.Domain.PaymentMethod` → `Integrations.Payment.PaymentMethodType`, switch expression สั้น ๆ):

```csharp
Method: command.Method switch
{
    PaymentMethod.Card => PaymentMethodType.Card,
    _ => PaymentMethodType.PromptPay,
}
```

### 1.3 `GET /api/commerce/payments/config`

**MVC controller** (`PaymentsController.cs`, production, D-19) — เพิ่ม action ใหม่:

```csharp
[HttpGet("config")]
[EnableRateLimiting("default")]
[EndpointName("CommerceGetPaymentConfig")]
[EndpointSummary("ดึง publishable key และวิธีชำระเงินที่เปิดใช้งาน")]
[ProducesResponseType(typeof(PaymentConfigResponse), StatusCodes.Status200OK)]
public IResult GetConfig([FromServices] PaymentService paymentService) =>
    Results.Ok(paymentService.GetConfig());
```

(`[Authorize]` สืบจากระดับ class อยู่แล้ว — ไม่ต้อง ownership check เพราะ response ไม่ผูกกับ user ใด ๆ, `publishableKey` เป็นค่าที่ตั้งใจเปิดเผยสาธารณะอยู่แล้วตามธรรมชาติของ Stripe แต่ยังคง auth ไว้เพื่อความสม่ำเสมอกับ endpoint อื่นในกลุ่มนี้ทั้งหมด — ไม่มี endpoint ไหนใน `PaymentsController` เป็น anonymous เลย)

**minimal-API mapper คู่กัน** (`PaymentEndpoints.cs`, `MapPaymentEndpoints`) — ตาม precedent ที่ `docs/contracts/P11-01-catalog-live-sessions.md` วางไว้แล้วในเวฟเดียวกัน (endpoint ใหม่ทุกตัวต้องมี mapper คู่ให้ `_app.MapCommerceEndpoints()` ที่เทสต์เก่า 4 ไฟล์ยังเรียกอยู่ครอบถึง แม้ตอนนี้จะยังไม่มีเทสต์ไหนยิง `/payments/config` ผ่าน route จริง แต่รักษาความสม่ำเสมอของ pattern ไว้ดีกว่าให้สองเส้นทาง drift กัน):

```csharp
endpoints.MapGet("/config", GetConfigAsync)
    .WithName("CommerceGetPaymentConfig")
    .WithSummary("ดึง publishable key และวิธีชำระเงินที่เปิดใช้งาน")
    .Produces<PaymentConfigResponse>(StatusCodes.Status200OK);

private static IResult GetConfigAsync(PaymentService paymentService) => Results.Ok(paymentService.GetConfig());
```

**`PaymentService.GetConfig()`** — sync, ไม่มี I/O เลย, เพิ่มต่อจาก `ToResponse` ในไฟล์เดิม:

```csharp
public PaymentConfigResponse GetConfig() =>
    new(_stripeOptions.PublishableKey, _paymentOptions.EnabledMethods);

public sealed record PaymentConfigResponse(string PublishableKey, IReadOnlyList<PaymentMethod> EnabledMethods);
```

(inject `IOptions<StripeOptions>` เข้า `PaymentService` เพิ่มด้วย — ปัจจุบันยังไม่มี, เอา `.Value` เก็บเป็น field เดียวกับที่ `StripeWebhookHandler` ทำอยู่แล้ว)

**Response ตัวอย่าง:**
```json
{ "publishableKey": "pk_test_...", "enabledMethods": ["PromptPay"] }
```
เมื่อเจ้าของโปรเจ็คตั้ง `Payment:EnabledMethods` เพิ่ม `"Card"` แล้ว (คู่กับเปิด card ใน Stripe Dashboard จริง — ดู §5 OWN) response จะเปลี่ยนเป็น `["PromptPay","Card"]` โดยไม่ต้อง deploy โค้ดใหม่ — FE ใช้ค่านี้ตัดสินว่าจะโชว์แท็บบัตรเป็น active หรือ "เร็ว ๆ นี้" (§4.1)

### 1.4 Revenue split fee wiring (Q4 gap — ใช้ได้ทั้ง PromptPay และ Card)

**ยืนยันแล้ว**: `IRevenueSplitContract`/`OrderItemSplitInfo.PaymentFee` (`src/Siri.Modules.Payout/Contracts/IRevenueSplitContract.cs:7`) และ `RevenueSplitContract.RecordRevenueSplitsAsync` (`src/Siri.Modules.Payout/Infrastructure/Contracts/RevenueSplitContract.cs:67`) **implement สูตร Q4 ไว้ครบแล้ว** รวม fallback ไป `PayoutOptions.EstimatedPaymentFeePercent` เมื่อ `PaymentFee == null` — **ไม่ต้องแก้ไฟล์ไหนใน `Siri.Modules.Payout` เลยสักไฟล์** งานทั้งหมดอยู่ที่ผู้เรียก (`StripeWebhookHandler`) ที่ปัจจุบันส่ง `null` เสมอ (บรรทัด 225: `new OrderItemSplitInfo(item.ORDER_ITEM_ID, courseInfo.InstructorId, item.LINE_TOTAL)` — ไม่มี 4th argument)

**ช่องว่างที่ต้องปิด**: `charge.balance_transaction.fee` เป็นค่าของ **ทั้ง PaymentIntent** (คิดครั้งเดียวต่อการชำระเงิน 1 ครั้ง) แต่ `OrderItemSplitInfo` เป็นระดับ **รายการต่อคอร์ส** — 1 order ซื้อได้หลายคอร์สพร้อมกัน (`CreateOrderCommand.courseIds: string[]`) ต้อง**เฉลี่ยค่าธรรมเนียมตามสัดส่วน** gross amount ของแต่ละ item ไม่ใช่ยัดค่าธรรมเนียมเต็มให้ทุก item (จะทำให้ยอดรวมเกินจริงหลายเท่าเมื่อมีมากกว่า 1 item) — **นี่คือ gap จริงที่คำสั่งงานถามหา ไม่ใช่แค่เคสของ Card**: บั๊กนี้มีอยู่แล้วแฝงอยู่ในดีไซน์แม้สำหรับ PromptPay เดิม จนถึงตอนนี้ไม่เคยแสดงออกมาเพราะไม่เคยมีใครส่ง `PaymentFee` ที่ไม่ใช่ null เลย

แก้ที่ `StripeWebhookHandler.HandlePaymentIntentSucceededAsync` (`src/Siri.Modules.Commerce/Application/StripeWebhookHandler.cs`), ภายใน `if (order.STATUS == OrderStatus.AwaitingPayment)` block, แทนที่ลูปสร้าง `splitItems` เดิม (บรรทัด 220-227):

```csharp
var feeResult = await _paymentMethod.GetChargeFeeAsync(paymentIntent.Id, cancellationToken).ConfigureAwait(false);
var totalFee = feeResult.Value; // GetChargeFeeAsync สัญญาว่าคืน Success เสมอ (§1.1) — .Value ตรงนี้ปลอดภัย

var splitItems = new List<OrderItemSplitInfo>();
var qualifyingItems = order.ORDER_ITEMS
    .Where(i => i.COURSE_ID.HasValue && coursePrices.ContainsKey(i.COURSE_ID.Value))
    .ToList();

if (totalFee is null || order.TOTAL_AMOUNT <= 0m)
{
    // ไม่มีค่าธรรมเนียมจริงให้ใช้ (Stripe ยังไม่ settle / เรียก API ไม่สำเร็จ) — ปล่อยให้
    // RevenueSplitContract คำนวณ fallback จาก EstimatedPaymentFeePercent เอง (Q4)
    foreach (var item in qualifyingItems)
    {
        splitItems.Add(new OrderItemSplitInfo(item.ORDER_ITEM_ID, coursePrices[item.COURSE_ID!.Value].InstructorId, item.LINE_TOTAL));
    }
}
else
{
    // เฉลี่ยค่าธรรมเนียมทั้งก้อนตามสัดส่วน LINE_TOTAL ของแต่ละ item ต่อ TOTAL_AMOUNT ของ order —
    // ปัดเศษแบบ "remainder ไปที่รายการสุดท้าย" (pattern เดียวกับ platformAmount ใน RevenueSplitContract)
    // กันผลรวมของ fee ต่อ item ไม่เท่ากับ totalFee เป๊ะเพราะการปัดเศษ
    decimal allocatedSoFar = 0m;
    for (var i = 0; i < qualifyingItems.Count; i++)
    {
        var item = qualifyingItems[i];
        var instructorId = coursePrices[item.COURSE_ID!.Value].InstructorId;
        decimal itemFee;
        if (i == qualifyingItems.Count - 1)
        {
            itemFee = totalFee.Value - allocatedSoFar;
        }
        else
        {
            itemFee = Math.Round(totalFee.Value * item.LINE_TOTAL / order.TOTAL_AMOUNT, 2, MidpointRounding.AwayFromZero);
            allocatedSoFar += itemFee;
        }
        splitItems.Add(new OrderItemSplitInfo(item.ORDER_ITEM_ID, instructorId, item.LINE_TOTAL, itemFee));
    }
}
```

ต้อง inject `IPaymentMethod` เข้า `StripeWebhookHandler`'s constructor ใหม่ (ยังไม่มีตอนนี้) — ลงทะเบียนอยู่แล้วใน DI (`services.AddScoped<IPaymentMethod, StripePaymentMethod>()`, `CommerceModule.cs:69`) ไม่ต้องเพิ่มอะไรที่ registration

**ทำไมเรียก `GetChargeFeeAsync` ตรงนี้ (ใน branch `AwaitingPayment` เท่านั้น ไม่ใช่ทุกครั้งที่ event เข้ามา)**: เป็นจุดเดียวที่ `splitItems` ถูกสร้างจริง (idempotent replay ของ event เดิมจะไม่มาถึง branch นี้อยู่แล้วเพราะ `existingEvent != null` เช็คตัดตั้งแต่ต้น `HandleAsync`) — ไม่เปลืองเรียก Stripe API ซ้ำโดยไม่จำเป็น

---

## 2. Schema delta

**None.** ตรวจครบทุกจุดแล้ว:
- `PAYMENT.METHOD` — `HasConversion<string>().HasMaxLength(32)` อยู่แล้ว (`PAYMENTConfiguration.cs:24`) เก็บ `"Card"` ได้ทันทีไม่ต้อง migration
- `REVENUE_SPLIT.PAYMENT_FEE_AMOUNT` — คอลัมน์มีอยู่แล้ว, `RevenueSplitContract` เขียนค่าอยู่แล้วทุก path (แค่ตอนนี้ยังเป็น estimate เสมอเพราะ caller ส่ง null)
- `Payment:EnabledMethods` เป็น app config (`appsettings.json`/env) ไม่ใช่ DB — ไม่มี migration
- ไม่มีตารางใหม่ ไม่มีคอลัมน์ใหม่ ไม่มี index ใหม่

---

## 3. API contract

### `POST /api/commerce/payments` (มีอยู่แล้ว — ปลดล็อก `method:"Card"`)

- Auth: `[Authorize]` (class-level, เดิม) · Rate limit: `default` (เดิม)
- Request (**shape เดิมทุกฟิลด์ ไม่เปลี่ยน**): `{ "orderId": "uuid", "method": "PromptPay" | "Card" }`
- Response 201 (**shape เดิมทุกฟิลด์ ไม่เปลี่ยน**) — สำหรับ `method:"Card"`: `qrCodeUrl`/`qrCodeData` จะเป็น `null` เสมอ (ไม่มี `NextAction` เพราะ `Confirm=false`), `clientSecret` จะมีค่าเสมอ (ต่างจาก PromptPay ที่ FE ไม่เคยต้องอ่านมันมาก่อน)
- Error ใหม่ 400: เมื่อ `Payment:EnabledMethods` ไม่มี method ที่ขอ → `ValidationProblemDetails`/`ProblemDetails` เดิม ข้อความ `"วิธีชำระเงิน Card ยังไม่เปิดใช้งานในขณะนี้ กรุณาเลือกวิธีอื่น"` (§1.2) — FE ต้อง handle เคสนี้เป็น toast/inline error ไม่ใช่ crash (§4.3)

### `GET /api/commerce/payments/{paymentId}` (มีอยู่แล้ว — ไม่เปลี่ยนเลย)

พฤติกรรม method-agnostic อยู่แล้วตามที่ยืนยันใน §0 — FE ใช้ endpoint เดิมตัวเดียวกัน poll สถานะได้ทั้งสอง method ไม่ต้องมี endpoint แยก

### `GET /api/commerce/payments/config` (ใหม่)

- Auth: `[Authorize]` (สืบจาก class) · Rate limit: `default`
- Request: ไม่มี body/param
- Response 200:
  ```json
  { "publishableKey": "pk_test_51...", "enabledMethods": ["PromptPay"] }
  ```
  `enabledMethods` เป็น array ว่างได้ในทางทฤษฎี (ถ้า config ตั้งผิดเป็น `[]`) — FE ต้อง handle เคสนี้ (ซ่อนทั้งสองแท็บ, ไม่ crash) แม้จะไม่ใช่ค่า default ที่คาดว่าจะเกิดจริง
- ไม่มี error case พิเศษ (ไม่มี input ให้ผิด, auth fail = 401 มาตรฐาน)

### Webhook `POST /api/commerce/webhooks/stripe` (มีอยู่แล้ว — **ไม่เปลี่ยน path/logic การตัดสินสถานะเลย**)

การเปลี่ยนแปลงเดียวคือภายใน (§1.4: อ่าน fee เพิ่มก่อนสร้าง `splitItems`) — สัญญาที่มีต่อ Stripe (event type ที่ handle, signature verification, idempotency) **ไม่เปลี่ยน**

---

## 4. Frontend notes (`siri_upskill_ui`)

Route ที่บริโภค: `/checkout/:orderId` — **Existing FE page**: `features/commerce/checkout-page/checkout-page.ts`+`.html` ต่อ API จริงอยู่แล้วสำหรับ PromptPay (ไม่ใช่ mock) SSR: ไม่เกี่ยว — หน้านี้ตั้ง `robots: 'noindex'` อยู่แล้ว (บรรทัด 168 ของ `.ts`, "Protected financial page") เป็น CSR โดยพฤตินัยอยู่แล้วแม้จะไม่ได้ประกาศ CSR-only ชัดเจนแบบ `/learn`

### 4.1 `PaymentConfigResponse` + เกท Card ด้วย `enabledMethods`

เพิ่ม `commerce-api.models.ts`:
```typescript
export interface PaymentConfigResponse {
  readonly publishableKey: string;
  readonly enabledMethods: readonly PaymentMethod[];
}
```
เพิ่ม `commerce-api.service.ts`:
```typescript
getPaymentConfig(): Observable<PaymentConfigResponse> {
  return this.get<PaymentConfigResponse>('/commerce/payments/config');
}
```

`CheckoutPage` เพิ่ม `paymentConfig = signal<PaymentConfigResponse | null>(null)` โหลดครั้งเดียวใน constructor (ไม่ต้องรอ order load เสร็จก่อน — ยิงคู่ขนานกันได้) การ์ด "บัตรเครดิต/เดบิต" ในเทมเพลตเปลี่ยนจาก `<div class="...cursor-not-allowed...">` ตายตัวเดิม (บรรทัด 90-109 ของ `.html`) เป็น `@if (paymentConfig()?.enabledMethods.includes('Card'))` แยก 2 ทาง:
- `true` → render เป็น `<button>` แบบเดียวกับปุ่ม PromptPay (คลิกได้จริง `(click)="setMethod('Card')"`)
- `false`/ยังไม่โหลดเสร็จ → **คงการ์ด "เร็ว ๆ นี้" (disabled) แบบเดิมทุกอย่าง** ไม่เปลี่ยน UX ของคนที่เห็นตอนที่เจ้าของโปรเจ็คยังไม่เปิด Card ใน Stripe Dashboard จริง (OWN dependency ยังไม่ครบ)

### 4.2 `CardPaymentPanel` — component ใหม่ (`@defer`-loaded)

ไฟล์ใหม่ `features/commerce/checkout-page/card-payment-panel/card-payment-panel.ts`+`.html` — แยกออกจาก `checkout-page.ts` (ปัจจุบัน 396 บรรทัด อยู่ใกล้เพดาน ~200 บรรทัดของ frontend.md อยู่แล้ว เพิ่ม logic ของ Stripe Elements เข้าไปตรง ๆ จะเกินเพดานทันที) มาตรฐาน standalone/OnPush:

```typescript
input<string>() clientSecret   // required — ได้จาก payment().clientSecret หลัง createPayment(Card)
input<string>() paymentId      // required — payment().id เดียวกัน (ไม่ใช่ orderId) เก็บลง sessionStorage
                                // ทันทีก่อนเรียก confirmPayment() เพื่อ resume-after-3DS-redirect (§4.3) —
                                // panel เองไม่มีทางเข้าถึง payment() ของ parent และ clientSecret parse กลับ
                                // เป็น id ไม่ได้ จึงต้องรับมาเป็น input แยกต่างหาก
input<string>() publishableKey // required — จาก paymentConfig().publishableKey
input<string>() returnUrl      // required — `${origin}/checkout/${orderId}`
output<string>() paymentError  // ข้อความ error ให้ parent แสดง (inline confirm error เท่านั้น — ไม่ใช่ผลสำเร็จ)
output<void>() confirmedInline // confirmPayment() resolve โดยไม่ redirect (ไม่มี error) — parent เริ่ม poll ต่อ
```

Logic ภายใน (SSR-safe ผ่าน `afterNextRender()` ตาม frontend.md's กฎ SSR — `loadStripe`/DOM mount ต้องอยู่ browser-only เท่านั้น):
1. `afterNextRender()` → `loadStripe(publishableKey())` (จาก `@stripe/stripe-js`) → ถ้า `null` (โหลด Stripe.js ไม่สำเร็จ เช่น ad-blocker/เครือข่าย) → emit error แปล i18n key `checkout.card.loadFailed` ทันที ไม่ throw
2. `stripe.elements({ clientSecret: clientSecret() })` → `elements.create('payment')` → `.mount(...)` ใส่ container ในเทมเพลต (`<div #cardElementContainer>`)
3. ปุ่ม "ชำระเงิน" ของ panel เอง (แยกจากปุ่มหลักของ checkout-page — ปุ่มหลักจะถูกซ่อนเมื่อ `selectedMethod() === 'Card'` และมี panel นี้ mount อยู่ §4.3) → `stripe.confirmPayment({ elements, clientSecret: clientSecret(), confirmParams: { return_url: returnUrl() }, redirect: 'if_required' })`
   - **`redirect: 'if_required'` ไม่ใช่ default `'always'`** — เลือกเพราะบัตร Visa/Mastercard ส่วนใหญ่ที่ต้อง 3DS2 ยุคปัจจุบันแสดง challenge เป็น modal/iframe ในหน้าเดิมได้โดยไม่ redirect ออกจากเว็บเลย `'if_required'` ให้ Stripe ตัดสินเองว่าจำเป็นต้อง redirect จริงไหม (เผื่อ 3DS1 แบบเก่า/ธนาคารบางเจ้า) — ลด friction โดยไม่เสีย fallback ที่ถูกต้อง
   - Resolve มี error → emit `paymentError` (ใช้ `error.message` ที่ Stripe ให้มา ซึ่งเป็นข้อความที่ตั้งใจให้แสดงกับผู้ใช้ได้ปลอดภัยอยู่แล้วตาม Stripe.js's contract) — **ไม่เรียก `completeCheckout()` ในกรณีนี้หรือกรณีไหนเลยจากในนี้**
   - Resolve ไม่มี error, ไม่ redirect (กรณี inline) → emit `confirmedInline` — parent เรียก `startPaymentPolling(payment().id)` ตัวเดิมที่มีอยู่แล้ว **ไม่สร้าง flow ใหม่**
   - Resolve แล้ว browser redirect ออกไปจริง (3DS เต็มรูปแบบ) → ก่อนเรียก `confirmPayment` ต้อง `sessionStorage.setItem('siri.checkout.pendingPaymentId', payment().id)` ไว้ก่อนเสมอ (ดู §4.3 ทำไมต้องใช้ sessionStorage ไม่ใช่ signal state)

**Bundle**: `@stripe/stripe-js` เป็น `import type` เท่านั้นที่ static-imported (สำหรับ type), ตัว `loadStripe` runtime ต้องอยู่หลัง `@defer` — เพิ่ม `npm install @stripe/stripe-js` (เวอร์ชัน stable ล่าสุด ณ ตอน implement, เช็ค changelog ก่อนล็อกเวอร์ชัน ตาม frontend.md ข้อ "ห้ามเดา version")

### 4.3 `checkout-page.ts`/`.html` — จุดแทรก

- `CheckoutStep` เพิ่มค่า `'card_entry'` (แสดง `CardPaymentPanel`) และ `'card_processing'` (หลัง `confirmedInline`, รอ poll — reuse UI แบบเดียวกับสถานะ `polling` ของ PromptPay ได้เลย ไม่ต้องออกแบบ UI ใหม่)
- `initiatePayment()`/`executePayment()` เดิม (มี logic promo-code replace order อยู่แล้ว) **ใช้ร่วมกันได้ทั้งสอง method โดยไม่ต้องแยกฟังก์ชัน** — `createPayment({orderId, method: selectedMethod()})` เหมือนเดิมทุกจุด เปลี่ยนแค่การ "หลัง" ได้ response: ถ้า `selectedMethod() === 'Card'` → เข้า step `card_entry` (mount panel ด้วย `payment().clientSecret`) แทนที่จะเช็ค `qrCodeUrl`
- ปุ่มหลัก "ชำระเงิน" (บรรทัด 331-346 + sticky mobile bar บรรทัด 360-382 ของ `.html`) **ซ่อนเมื่อ `currentStep() === 'card_entry'`** (panel มีปุ่ม "ชำระเงิน" ของตัวเองตาม §4.2) — เหมือนที่ปุ่มหลักซ่อนอยู่แล้วเมื่อ `payment()` มีค่า (บรรทัด 331 `@if (!payment())`) ขยาย condition เพิ่มแค่ state ใหม่
- **Resume หลัง 3DS redirect กลับมา** (edge case, เกิดเมื่อ Stripe ตัดสินใจ redirect จริงแม้ตั้ง `if_required`): `CheckoutPage`'s constructor เพิ่มเช็ค (ใน `afterNextRender()`, guard `isPlatformBrowser` ตาม frontend.md SSR — อ่าน query param เป็นการอ่าน URL ของ browser จริง ต้อง browser-only) ว่า `route.snapshot.queryParamMap` มี `redirect_status` (พารามิเตอร์ที่ Stripe แนบกลับมาที่ `return_url` เสมอ) หรือไม่:
  - มี → อ่าน `paymentId` จาก `sessionStorage.getItem('siri.checkout.pendingPaymentId')` (ตั้งไว้ก่อน redirect ตาม §4.2) ถ้ามีค่า → เรียก `startPaymentPolling(paymentId)` ทันที (**ไม่ใช่เชื่อค่า `redirect_status` ในการตัดสินอะไรเลย** — ใช้แค่เป็นสัญญาณว่า "ควรเริ่ม poll" ตาม security.md "ห้ามเชื่อ redirect หรือ callback จาก frontend" — สถานะจริงมาจาก `GET /payments/{id}` ที่อ่านจาก DB ที่ webhook อัปเดตเท่านั้นเสมอ) แล้วเคลียร์ query param ออกจาก URL ด้วย `router.navigate([], { queryParams: {}, replaceUrl: true })` กัน refresh แล้ววนซ้ำ
  - ไม่มี `paymentId` ใน `sessionStorage` (เช่น เปิด tab ใหม่, sessionStorage ถูกเคลียร์) → แสดงสถานะ generic "กำลังตรวจสอบสถานะการชำระเงิน" พร้อมปุ่ม refresh ที่โหลด order ใหม่ (order status จะเป็น `Paid` เองถ้า webhook ประมวลผลไปแล้ว, `loadOrder()` เดิมมี branch `if (order.status === 'Paid') this.completeCheckout(order.id)` อยู่แล้ว — พอ)
  - **ใช้ `sessionStorage` ไม่ใช่ signal/component state ล้วน** เพราะ 3DS full-page redirect ทำให้ Angular app ถูกทำลายและสร้างใหม่ทั้งหมด (component state หายแน่นอน) — ไม่ใช่การเก็บข้อมูลอ่อนไหว (แค่ payment GUID ภายในของเราเอง ไม่ใช่ token/credential) จึงไม่ขัดกับ security.md's "access token เก็บใน memory ห้าม localStorage" (กฎนั้นพูดถึง JWT access token เท่านั้น)

### 4.4 Statement descriptor disclosure (PAYMENT.md — ต้องเพิ่มใหม่ทั้งหมด ไม่ใช่แค่ต่อยอด)

**ยืนยันจากโค้ดจริงแล้ว (§0): ยังไม่เคยแสดงเลยแม้สำหรับ PromptPay** — เพิ่ม 1 บรรทัดใหม่ใกล้ปุ่มจ่ายเงิน (ใต้ "Guarantee Badge", บรรทัด ~349-353 ของ `.html`) แสดง**เสมอทั้งสอง method** (ไม่ผูกกับ `selectedMethod()`):

```html
<p class="text-[11px] text-[var(--color-text-muted,#64748B)] text-center">
  {{ 'checkout.summary.statementDescriptor' | translate }}
</p>
```

i18n key ใหม่ (ทั้ง `th.json`/`en.json`):
- `checkout.summary.statementDescriptor` (th): `"รายการที่ปรากฏบนใบแจ้งยอดบัตร/แอปธนาคารของคุณจะแสดงชื่อผู้รับเงินเป็น STRIPE PAYMENTS (THAILAND) LTD"`
- (en คู่กัน แปลความหมายเดียวกัน)

### 4.5 i18n keys อื่นที่ต้องเพิ่ม (ทั้ง `th.json`/`en.json`)

- `checkout.methods.card`/`cardDesc` — มีอยู่แล้ว แต่ข้อความปัจจุบันสื่อว่า "เร็ว ๆ นี้" ตายตัว ต้องแก้ให้เป็นกลาง (ไม่ผูกคำว่า "เร็ว ๆ นี้" ในข้อความ description เอง เพราะ badge "เร็ว ๆ นี้"/`comingSoon` จะโชว์แยกเป็น condition ตาม `enabledMethods` แล้วตาม §4.1) — เช่น `cardDesc: "Visa, Mastercard"` เฉย ๆ
- `checkout.card.loadFailed` — Stripe.js โหลดไม่สำเร็จ
- `checkout.card.enterDetails` — heading ของ panel
- `checkout.card.payButton` — ปุ่มจ่ายในตัว panel เอง
- `checkout.card.processing` — สถานะ `card_processing`
- `checkout.card.resuming` — สถานะ resume หลัง 3DS redirect (§4.3 กรณีไม่มี `paymentId` ใน sessionStorage)

---

## 5. Integration checklist (สำหรับ integrator-qa)

- [ ] **PromptPay ต้องไม่ regress** — รัน/ตรวจ `tests/Siri.UnitTests/Payment/StripePaymentMethodTests.cs` (มีอยู่แล้ว) ผ่านทั้งหมดโดยไม่ต้องแก้ไฟล์เทสต์เลย (ยืนยันว่า `Method` default `PromptPay` ทำงานถูกตามที่ออกแบบใน §1.1) + ตรวจ `options` ที่ path PromptPay (`Confirm=true`, `PaymentMethodTypes=["promptpay"]`, `PaymentMethodData` ครบ) **เหมือนเดิมทุกบิต** เทียบกับโค้ดก่อนแก้
- [ ] **`PaymentFulfillmentIntegrationTests.cs` ต้องยังผ่าน** (มีอยู่แล้ว, เรียก `StripeWebhookHandler.HandleAsync` ตรง) — ตรวจว่าไม่พังจากการเพิ่ม `IPaymentMethod` dependency ใหม่เข้า constructor (ต้องมี fake/stub `IPaymentMethod` ที่คืน `GetChargeFeeAsync` → `Result.Success<decimal?>(null)` เป็นค่าเริ่มต้นถ้าเทสต์เดิมไม่ได้ mock ไว้)
- [ ] **เทสต์ใหม่ — Card path สร้าง PaymentIntent ถูกสาขา**: unit test ยืนยัน `request.Method = Card` → `options.Confirm == false`, `options.PaymentMethodTypes == ["card"]`, `options.PaymentMethodData == null`
- [ ] **เทสต์ใหม่ — 3DS-pending ไม่ enroll** (ตรงตามที่คำสั่งงานขอ): seed Order (`AwaitingPayment`) + `PAYMENT.Create(..., PaymentMethod.Card, ...)` (`STATUS=Pending`, จำลองว่า Stripe PaymentIntent อยู่ `requires_action`) **ไม่ส่ง webhook event ใด ๆ** → assert `ILearningAccessContract`/enrollment repository **ไม่มี** enrollment แถวใหม่, `order.STATUS` ยังเป็น `AwaitingPayment` — จากนั้นค่อยส่ง `payment_intent.succeeded` event จำลอง (pattern เดียวกับ `PaymentFulfillmentIntegrationTests.cs` ที่มีอยู่แล้ว) → assert enrollment เกิดขึ้น**หลังจากนั้นเท่านั้น** พิสูจน์ว่าไม่มี code path ไหนใน `PaymentService.CreateAsync`/`GetByIdAsync` เรียก enroll ได้เลยนอกจาก webhook
- [ ] **เทสต์ใหม่ — payment fee allocation**: order 2 item (เช่น 700/300 บาท, totalFee 30 บาท) → assert `splitItems` แต่ละตัวได้ fee ตามสัดส่วน (21.00/9.00) และผลรวม `== totalFee` เป๊ะ (ไม่ใช่แค่ "ใกล้เคียง" — พิสูจน์ remainder-to-last-item ทำงานถูก) + order 1 item → assert item เดียวได้ fee เต็มจำนวน (ไม่มี rounding drift เพราะไม่มีการหาร)
- [ ] **`GET /commerce/payments/config`**: ทดสอบผ่าน `SiriApiFactory` (`tests/Siri.IntegrationTests/Fixtures/SiriApiFactory.cs`, boot `Program.cs` จริง → เจอ `PaymentsController` ผ่าน `MapControllers()` จริง) **ไม่ใช่** `_app.MapCommerceEndpoints()` (แบบ `ApiHostSmokeTests.cs` ที่มีอยู่แล้วเป็น precedent) — assert `enabledMethods` default เป็น `["PromptPay"]` เมื่อไม่ตั้ง config, และเปลี่ยนเป็น `["PromptPay","Card"]` เมื่อตั้ง `Payment:EnabledMethods` ผ่าน test config override
- [ ] **`Payment:EnabledMethods` gate จริง**: request `POST /payments {method:"Card"}` เมื่อ config ไม่มี `Card` → assert 400 พร้อมข้อความที่ระบุใน §1.2 เป๊ะ, **ไม่มีการเรียก Stripe API เลย** (ยืนยันด้วย mock/spy บน `IPaymentMethod` ว่า `CreatePaymentIntentAsync` ไม่ถูกเรียก)
- [ ] **FE**: `CardPaymentPanel` unit test (loadStripe fail → emit error, confirmPayment error → emit paymentError ไม่ใช่ confirmedInline, confirmPayment success ไม่ redirect → emit confirmedInline) + `checkout-page.spec.ts` เพิ่มเคส resume-after-redirect (mock `sessionStorage`+query param แล้ว assert `startPaymentPolling` ถูกเรียกด้วย paymentId ที่ถูกต้อง, URL query param ถูกเคลียร์)
- [ ] **Statement descriptor** ขึ้นจริงบนทั้งสอง step (`selection` ก่อนเลือก method และหลัง QR/card panel แสดง) — เช็คด้วยตาหรือ Playwright role/text query ตาม frontend.md ("ห้าม assert ด้วย CSS class")
- [ ] **`npm run build`** ยืนยันว่า `@stripe/stripe-js` ไม่ถูกดึงเข้า initial bundle (อยู่หลัง `@defer` จริง) — เช็ค bundle stats ว่า initial chunk ยังไม่โตขึ้นมีนัยสำคัญจาก dependency ตัวนี้

**OWN (เจ้าของโปรเจ็ค, ไม่บล็อกการเขียน/เทสต์โค้ด — ตาม pattern เดียวกับ P2-01's Bunny account setup):**
1. เปิด Card (Visa/Mastercard) ใน Stripe Dashboard จริง (Settings → Payment methods) — ยังไม่ทำจนถึงตอนนี้
2. ตั้ง `Payment:EnabledMethods` ให้มี `"Card"` ใน config/env ของ deploy จริง (dev/test ปล่อยเป็น default `["PromptPay"]` ได้จนกว่าจะพร้อมทดสอบจริง)
3. เช็คเรตค่าธรรมเนียมบัตรจริงจาก Stripe Dashboard (สูงกว่า PromptPay) → พิจารณาปรับ `Payout:EstimatedPaymentFeePercent` เป็นค่าที่ใกล้เคียงเรตบัตรมากขึ้น (ตอนนี้ default `0.00m` เฉย ๆ ตาม `PayoutOptions.cs` — เป็นความเสี่ยงเดิมที่มีอยู่ก่อนหน้า P11-09 อยู่แล้ว ไม่ใช่ regression ใหม่ แต่ยิ่งสำคัญขึ้นเมื่อ Card เริ่มมีธุรกรรมจริงเพราะเรตต่างจาก PromptPay มาก — `GetChargeFeeAsync` (§1.1) ช่วยลดผลกระทบเพราะใช้ค่าจริงเป็นหลักเมื่อหาได้ ค่า estimate เป็นแค่ fallback)

---

## Changelog

- **2026-09-16** — §4.2 เพิ่ม input `paymentId` (`string`, required) ที่ตกหล่นไปตอน freeze — integrator-qa พบระหว่างรีวิวงานที่ implement เสร็จแล้วว่า `CardPaymentPanel` (`card-payment-panel.ts:52`) ต้องรับ `payment().id` เข้ามาจริงเพื่อเขียนลง `sessionStorage` ก่อนเรียก `confirmPayment()` ตามที่ §4.3's resume-after-3DS-redirect flow ต้องการอยู่แล้ว (panel เข้าถึง `payment()` ของ parent ไม่ได้ และ `clientSecret` parse กลับเป็น id ไม่ได้) — เป็นช่องโหว่ของ contract เดิมที่ไม่ครบ ไม่ใช่ implementation ผิด ไม่มีการเปลี่ยน behavior ใด ๆ
