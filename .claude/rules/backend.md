# Backend Rules — .NET 10 / ASP.NET Core

## โครงสร้าง

- Vertical slice: หนึ่ง feature = หนึ่งโฟลเดอร์ใน `Features/` ประกอบด้วย `Command`/`Query` + `Handler` + `Validator` + `Endpoint` + `Response`
- ห้ามสร้างชั้น `IXxxService` ที่มี method รวมทุกอย่าง (god service) — handler ต่อ use case เท่านั้น
- **ห้ามใช้ MediatR** — inject handler ตรงผ่าน DI (`services.AddScoped<CreateCourseHandler>()`)
- **ห้ามใช้ AutoMapper** — เขียน `ToResponse()` extension method เอง ชัดเจนกว่าและ debug ได้
- Module A เรียก Module B ผ่าน `Contracts/` เท่านั้น ห้าม reference `Domain`/`Infrastructure` ของ module อื่น (มี ArchitectureTest บังคับ — ถ้าเทสต์ตัวนี้แดงแปลว่าออกแบบผิด อย่าไปแก้เทสต์)

## Endpoint

- Minimal API + `MapGroup()` ต่อ module, ตั้ง `.RequireAuthorization()` ที่ระดับ group แล้วค่อย `.AllowAnonymous()` เป็นราย endpoint (default deny)
- Endpoint ต้องบางที่สุด: bind → เรียก handler → map ผลลัพธ์เป็น HTTP **ห้ามมี business logic ใน endpoint**
- Response ต้องเป็น DTO เสมอ **ห้ามคืน EF entity ออก API**
- Error → RFC 9457 `ProblemDetails` เสมอ; ใส่ `traceId` ทุกครั้ง
- ตั้ง `.WithName()` + `.Produces<T>()` ให้ครบ เพื่อให้ OpenAPI สร้าง client ฝั่ง Angular ได้

## Result & error handling

```csharp
// ✅ ใช้ Result<T> สำหรับ error ที่คาดไว้แล้ว (validation, not found, forbidden, business rule)
public async Task<Result<CourseResponse>> HandleAsync(PublishCourseCommand cmd, CancellationToken ct)

// ❌ ห้ามใช้ exception เป็น control flow
throw new NotFoundException("course not found");
```
- Exception ใช้เฉพาะกรณีที่ระบบพังจริง (DB ล่ม, config ผิด) → global handler ตอบ 500 + log
- ห้าม `catch (Exception) { }` เงียบ ๆ; ห้าม `catch` แล้ว rethrow เปล่า ๆ

## Async

- ทุก I/O เป็น `async` และ **ต้องรับ + ส่งต่อ `CancellationToken` เสมอ**
- ห้าม `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `async void` (ยกเว้น event handler จริง ๆ)
- Query ที่อ่านอย่างเดียวใส่ `.AsNoTracking()`

## Validation & security

- FluentValidation ต่อ command/query, ผูกผ่าน endpoint filter — ห้ามเขียน `if (string.IsNullOrEmpty(...)) return BadRequest` กระจายในโค้ด
- ทุก handler ที่รับ `{id}` จากผู้ใช้ **ต้องเช็ค ownership/สิทธิ์** ก่อนคืนหรือแก้ข้อมูล (กัน IDOR) — เป็นข้อบังคับ ไม่ใช่ทางเลือก
- อ่าน user ปัจจุบันจาก `IUserContext` เท่านั้น ห้ามรับ `userId` มาจาก request body
- Endpoint กลุ่มเสี่ยง (login, refresh, playback token, checkout, webhook) ต้องมี rate limit policy ระบุชัด

## Domain

- Entity ต้องปกป้อง invariant ของตัวเอง: property เป็น `private set`, เปลี่ยนสถานะผ่าน method (`course.Publish()`) ไม่ใช่ setter จากภายนอก
- ห้ามให้ Domain รู้จัก EF, HttpContext, หรือ DTO
- ตรรกะเรื่องเงินอยู่ในที่เดียว (`Siri.Modules.Commerce/Domain/Pricing/`) ห้ามคำนวณราคาซ้ำที่อื่น
- ใช้ `decimal` กับเงินเสมอ ห้าม `double`/`float`

## Configuration

- Options pattern + `ValidateOnStart()` ทุกตัว (`VideoProviderOptions`, `PaymentOptions`, ...)
- ห้ามอ่าน `IConfiguration["key"]` กระจายกลางโค้ด
- Feature flag สำหรับของที่ยังไม่พร้อม แทนการ comment โค้ดทิ้ง

## Testing

- Unit test: domain logic, pricing, การตัดสินสิทธิ์, revenue split — บังคับ
- Integration test: endpoint สำคัญ ใช้ Testcontainers (MSSQL + Redis จริง) ห้ามใช้ InMemory provider (พฤติกรรมต่างจาก SQL Server จนหลอกให้ผ่าน)
- ตั้งชื่อเทสต์: `MethodName_Scenario_ExpectedResult`
- ทุก bug ที่แก้ ต้องมีเทสต์ที่ fail ก่อนแก้แล้วผ่านหลังแก้
