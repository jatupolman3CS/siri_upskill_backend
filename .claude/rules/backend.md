# Backend Rules — .NET 10 / ASP.NET Core

## โครงสร้าง

**สองรูปแบบอยู่ร่วมกันในโค้ดเบสนี้โดยตั้งใจ — เช็คก่อนว่าโมดูลที่กำลังแก้อยู่ฝั่งไหน อย่าผสมปนกันในโมดูลเดียว:**

| | Vertical slice (Identity / Catalog / Notification) | Repository + Service (Commerce / Media / Learning / Payout / Cms / Community / Analytics) |
|---|---|---|
| ตัดสินใจเมื่อ | ของเดิม, ใช้มาตั้งแต่ P0/P1 | 2026-08-20, ตัดสินใจโดยเจ้าของโปรเจ็คตอน scaffold P2–P6 จาก mockup handoff (ดู `docs/DECISIONS.md` D-17) |
| Data access | Handler คุย `AppDbContext` ตรง | `I{Entity}Repository` interface + implementation, handler/service เรียกผ่าน interface |
| Business logic | หนึ่ง `{UseCase}Handler` ต่อ 1 command/query, มี `HandleAsync` เดียว | หนึ่ง `{Entity}Service` ต่อ 1 aggregate, มีหลาย method (`CreateAsync`, `GetByIdAsync`, `CancelAsync`, ...) — **ไม่มี** `I{Entity}Service` interface (มี implementation เดียวเสมอ, เทสต์ปลอม `I{Entity}Repository`/`IClock` ตรง ๆ ได้อยู่แล้ว) |
| โฟลเดอร์ | `Features/{UseCase}/{Command,Validator,Handler,Endpoint,Response}.cs` | `Domain/{ENTITY}.cs` (entity+enum, ห้ามรู้จัก EF) · `Infrastructure/{Entity}Configuration.cs`+`{Entity}Repository.cs`+`AppDbContext{Module}Extensions.cs` (รู้จัก EF ได้) · `Application/I{Entity}Repository.cs`+`{Entity}Service.cs`+DTO+validator+`{Entity}Endpoints.cs` |
| ทำไมไม่ใช่ god service | Handler ต่อ use case คุมอยู่แล้วโดยธรรมชาติ (vertical slice) | Repository+Service ต่อ **1 aggregate เท่านั้น** ไม่ใช่ 1 module — ยังคุม "ทำหน้าที่เดียว" อยู่ ไม่ใช่ god service ที่รวมทุก aggregate ของ module เข้าด้วยกัน |

- ห้ามสร้างชั้น `IXxxService` ที่มี method รวมทุกอย่างข้าม aggregate (god service) — ทั้งสองรูปแบบยังคุมกฎนี้เหมือนกัน แค่คนละหน่วยความรับผิดชอบ (use case เทียบ aggregate)
- **ห้ามใช้ MediatR** — inject handler/service ตรงผ่าน DI (`services.AddScoped<CreateCourseHandler>()` หรือ `services.AddScoped<OrderService>()`)
- **ห้ามใช้ AutoMapper** — เขียน `ToResponse()` extension method เอง ชัดเจนกว่าและ debug ได้
- Module A เรียก Module B ผ่าน `Contracts/` เท่านั้น ห้าม reference `Domain`/`Infrastructure` ของ module อื่น (มี ArchitectureTest บังคับ — ถ้าเทสต์ตัวนี้แดงแปลว่าออกแบบผิด อย่าไปแก้เทสต์) — กฎนี้ใช้เหมือนกันทั้งสองรูปแบบ
- **ชื่อ entity class/property แบบ UPPERCASE** ใน 7 โมดูลข้างบนเป็นข้อยกเว้นเฉพาะที่ตัดสินใจแล้ว — รายละเอียดเต็มอยู่ที่ `.claude/rules/database.md`'s "ชื่อ entity/DB แบบ UPPERCASE" ห้ามเอา convention นี้ไปใช้กับ Identity/Catalog/Notification หรือ Repository/Service/DTO/endpoint class name ใด ๆ (สิ่งเหล่านั้นยังเป็น PascalCase ปกติ)

## Endpoint

**ASP.NET Core MVC Controllers (attribute routing)** — ตัดสินใจแล้ว 2026-09-01 (`docs/DECISIONS.md` D-19), แทนที่ Minimal API `MapGroup()`/`*Endpoints.cs` เดิม การเปลี่ยนนี้จำกัดแค่ชั้น routing/binding เท่านั้น — DTO, validator, และ business logic ใน `{UseCase}Handler`/`{Entity}Service` **ไม่เปลี่ยน**

- Controller หนึ่งไฟล์ต่อ resource ใต้ `src/Siri.Api/Controllers/<Module>/<Resource>Controller.cs` — `[ApiController]` + `[Route("api/...")]` + `[Tags("...")]`, สืบทอดจาก `ControllerBase` ตรง ๆ (ไม่มี shared base controller ในโค้ดเบสนี้)
- **Default deny ผ่าน `[Authorize]` ที่ต้องใส่ชัดเจนเสมอ — ไม่มี global fallback policy** (`AddSiriAuthorizationPolicies()` ไม่ได้ตั้ง `FallbackPolicy`) ดังนั้น action ที่ไม่มีทั้ง `[Authorize]` และ `[AllowAnonymous]` จะ**เปิดสาธารณะโดย default ของ ASP.NET Core เอง** (ตรงข้ามกับ Minimal API เดิมที่ framework บังคับ deny เองที่ระดับ group) — ใส่ attribute ให้ครบทุก action เสมอ ห้ามลืม:
  - Controller ที่ทุก action ใช้ policy เดียวกันหมด: ใส่ `[Authorize]` (ต้อง login) หรือ `[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]`/`InstructorOnly` ที่ระดับ class เช่น `OrdersController`, `PaymentOpsController`
  - Controller ที่ปนกันระหว่าง public/protected: ใส่ `[Authorize]`/`[AllowAnonymous]` ที่ระดับ **method** ทุกตัว เช่น `CoursesController` (`SearchCourses`/`GetCourseDetail`/`GetCourseReviews` = `[AllowAnonymous]`, `CreateCourseReview` = `[Authorize]`)
  - Controller ที่ public ทั้งคลาสจริง ๆ (ไม่มี auth เลย): ใส่ `[AllowAnonymous]` ที่ระดับ class ให้ชัดว่าตั้งใจ (มีแค่ 3 ตัวในระบบ: Stripe webhook, Bunny webhook, sitemap)
- Action คืน `IResult` (ไม่ใช่ `IActionResult`) ผ่าน `Results.Ok()`/`Results.Created()`/`Results.Unauthorized()`/`Results.File()` ฯลฯ — ตัวเดียวกับที่ Minimal API เคยใช้
- Action ต้องบางที่สุด: bind → inject handler/service ตัวเดิมผ่าน `[FromServices]` → เรียก `.HandleAsync()`/`{Verb}Async()` → map ผลลัพธ์เป็น HTTP **ห้ามมี business logic ใน action**
- Response ต้องเป็น DTO เสมอ **ห้ามคืน EF entity ออก API**
- Error → `Result<T>.Error.ToProblemHttpResult(HttpContext)` (`Siri.SharedKernel`) เสมอ ได้ RFC 9457 `ProblemDetails` พร้อม `traceId` อัตโนมัติ — ตัวเดียวกับสมัย Minimal API ไม่เปลี่ยน
- FluentValidation ไม่ต้องผูกเองต่อ action: `ValidationActionFilter` (`Siri.SharedKernel`) เป็น global MVC action filter (ผูกครั้งเดียวที่ `Program.cs`'s `AddControllers(options => options.Filters.Add<ValidationActionFilter>())`) หา `IValidator<T>` ที่ลงทะเบียนไว้ให้กับ action argument ทุกตัวอัตโนมัติ แล้วตอบ `ValidationProblemDetails` เองถ้าไม่ผ่าน — แค่ `services.AddScoped<IValidator<TCommand>, TValidator>()` ให้ครบก็พอ
- ตั้ง `[EndpointName("...")]` + `[EndpointSummary("...")]` + `[ProducesResponseType(typeof(T), StatusCodes...)]` ให้ครบทุก action (แทนที่ `.WithName()`/`.WithSummary()`/`.Produces<T>()` เดิม) เพื่อให้ OpenAPI สร้าง client ฝั่ง Angular ได้
- Rate limit ผ่าน `[EnableRateLimiting("policyName")]` ที่ action (แทนที่ `.RequireRateLimiting(...)` เดิม)

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
