# SIRI UpSkill — System Architecture

## 1. ภาพรวมระบบ

```
                          ┌──────────────────────┐
   Browser / PWA  ───────►│  Angular 22 (SSR)    │  public pages: SEO, hydration
                          │  Angular 22 (CSR)    │  /learn, /instructor, /admin
                          └──────────┬───────────┘
                                     │ HTTPS (JWT access token)
                          ┌──────────▼───────────┐
                          │   API Gateway layer  │  rate limit, CORS, auth, output cache
                          │  ASP.NET Core 10 API │
                          └──────────┬───────────┘
        ┌──────────────┬─────────────┼─────────────┬───────────────┐
        │              │             │             │               │
   ┌────▼────┐   ┌─────▼────┐  ┌─────▼─────┐ ┌─────▼────┐   ┌──────▼─────┐
   │Identity │   │ Catalog  │  │ Learning  │ │ Commerce │   │   Media    │
   │ module  │   │  module  │  │  module   │ │  module  │   │   module   │
   └────┬────┘   └─────┬────┘  └─────┬─────┘ └─────┬────┘   └──────┬─────┘
        └──────────────┴─────────────┼─────────────┴───────────────┘
                                     │
        ┌────────────────┬───────────┼────────────┬──────────────────┐
   ┌────▼─────┐   ┌──────▼────┐ ┌────▼────┐ ┌─────▼──────┐  ┌────────▼────────┐
   │  MSSQL   │   │   Redis   │ │Hangfire │ │Blob/S3+CDN │  │ External: Video │
   │ (schema  │   │ cache/    │ │ workers │ │  storage   │  │ Bunny Stream,   │
   │ per mod) │   │ sessions  │ │         │ │            │  │ Stripe, Email   │
   └──────────┘   └───────────┘ └─────────┘ └────────────┘  └─────────────────┘
```

ทั้งหมดรันบน **Contabo VPS** เครื่องเดียวด้วย Docker Compose (ดู `DEPLOYMENT.md`)
วิดีโอไม่วิ่งผ่าน VPS — client ดึงจาก **Bunny CDN** ตรง โดย API ของเราเป็นคนออก signed token ให้

**หลักการ:** Modular Monolith — deploy เป็น unit เดียว แต่แบ่ง module ชัดเจนระดับ project เพื่อบังคับ boundary ถ้าวันหนึ่ง traffic โต แยก `Media` และ `Commerce` ออกเป็น service อิสระได้โดยไม่ต้องรื้อ domain

---

## 2. Backend — โครงสร้าง solution

```
backend/
  SiriUpSkill.sln
  src/
    Siri.Api/                        # Host: Program.cs, DI, middleware, endpoint mapping
    Siri.Workers/                    # Hangfire jobs: transcode poll, payout, email, analytics rollup
    Siri.SharedKernel/               # Result<T>, DomainError, ValueObjects, Clock, IUserContext
    Siri.Persistence/                # AppDbContext, EF configurations, migrations, interceptors
    Siri.Modules.Identity/
    Siri.Modules.Catalog/
    Siri.Modules.Media/
    Siri.Modules.Learning/
    Siri.Modules.Commerce/
    Siri.Modules.Payout/
    Siri.Modules.Cms/
    Siri.Modules.Community/
    Siri.Modules.Notification/
    Siri.Modules.Analytics/
    Siri.Integrations.Video/         # IVideoProvider + adapters (Bunny/Mux/Cloudflare)
    Siri.Integrations.Payment/       # IPaymentMethod + Stripe adapter — v1 PromptPay QR ผ่าน Stripe PaymentIntent + webhook (ดู PAYMENT.md)
    Siri.Integrations.Storage/       # IFileStorage (Blob/S3)
    Siri.Integrations.Email/
  tests/
    Siri.UnitTests/
    Siri.IntegrationTests/           # Testcontainers: MSSQL + Redis จริง
    Siri.ArchitectureTests/          # NetArchTest: บังคับ module ห้าม reference ข้ามกัน
```

### โครงภายในแต่ละ module — สองรูปแบบ (ตัดสินใจ 2026-08-20, `docs/DECISIONS.md` D-17)

**Vertical Slice** — Identity, Catalog, Notification (ของเดิม ไม่แตะ):
```
Siri.Modules.Catalog/
  Domain/           # entity + business rule ล้วน ไม่รู้จัก EF/HTTP
  Features/
    CreateCourse/   # Command.cs + Handler.cs + Validator.cs + Endpoint.cs + Response.cs
    PublishCourse/
    SearchCourses/
  Contracts/        # DTO / integration event ที่ module อื่นเรียกได้ (public surface เดียว)
  Infrastructure/   # repository, EF config, external call
  CatalogModule.cs  # AddCatalogModule(IServiceCollection) + MapCatalogEndpoints(...)
```

**Repository + Service** — Commerce, Media, Learning, Payout, Cms, Community, Analytics (ใหม่):
```
Siri.Modules.Commerce/
  Domain/                          # entity (UPPERCASE — ดู database.md) + enum ล้วน ไม่รู้จัก EF/HTTP
  Infrastructure/
    ORDERConfiguration.cs          # IEntityTypeConfiguration<ORDER>
    OrderRepository.cs             # implementation (class name ปกติ PascalCase)
    AppDbContextCommerceExtensions.cs  # context.Orders() ฯลฯ — mirror AppDbContext ที่ไม่มี DbSet<> ตรง ๆ
  Application/
    IOrderRepository.cs            # interface อยู่ตรงนี้ ไม่ใช่ Infrastructure/ (คนเรียกอยู่ที่นี่)
    OrderService.cs                # 1 aggregate = 1 service, หลาย method, ไม่มี IOrderService
    OrderResponse.cs / CreateOrderCommand.cs / CreateOrderValidator.cs
    OrderEndpoints.cs
  CommerceModule.cs                # AddCommerceModule(IServiceCollection) + MapCommerceEndpoints(...)
```
Contracts/ ยังมีความหมายเหมือนเดิมถ้าโมดูลนี้ต้อง expose อะไรให้โมดูลอื่นเรียก (เช่น pattern เดียวกับ `Identity.Contracts.IInstructorRoleGrantor`) แค่ยังไม่มี aggregate ไหนในกลุ่มนี้ต้องใช้ตอน scaffold

**กฎ boundary เหมือนกันทั้งสองรูปแบบ:** module A เรียก module B ได้ผ่าน `Contracts` เท่านั้น ห้าม reference `Domain`/`Infrastructure` ข้าม module — มี ArchitectureTest บังคับ (`Siri.ArchitectureTests/ModuleAssemblyCatalog.cs` ต้องเพิ่ม module ใหม่เข้า list เองด้วยมือ ไม่ใช่ reflection อัตโนมัติ — ลืมเพิ่มแปลว่า 4 boundary test จะข้าม module นั้นไปเงียบ ๆ)

### OpenAPI

`Siri.Api/Program.cs` เปิด native `Microsoft.AspNetCore.OpenApi` อยู่แล้ว (`builder.Services.AddOpenApi()` + `app.MapOpenApi()` เฉพาะ `IsDevelopment()`, path default `/openapi/v1.json`) — ไม่ใช่ Swashbuckle/Swagger UI (ไม่มี package นั้นในโซลูชัน) เอกสารสร้างจาก route metadata ตอน map endpoint (`.Produces<T>()`, ชนิด parameter ที่ bind) ล้วน ๆ — **ไม่เรียก handler body เลย** endpoint ที่ body ยัง `throw new NotImplementedException()` ก็ยังโผล่ถูก schema ใน spec ปกติ ใช้ประโยชน์ตรงนี้ตอน scaffold โมดูลใหม่ล่วงหน้าได้ (ฝั่ง frontend เริ่มงานจาก contract ได้ก่อน logic จริงเสร็จ)

ฝั่ง frontend มี `npm run generate:api-types` (อ่าน `/openapi/v1.json` ผ่าน `openapi-typescript`) เขียนเป็น `frontend/src/app/core/http/api-types.generated.ts` — รันเพื่อ sync type ให้ตรง backend ทุกครั้งที่ contract เปลี่ยน ไม่ต้องเขียน TypeScript interface มือทุกจุดเหมือนเดิมทั้งหมด (ยังต้องมี TS interface ตาม frontend.md เดิม แค่ไม่ต้องเดามือ)

### Cross-cutting
| เรื่อง | วิธี |
|-------|-----|
| Validation | FluentValidation + endpoint filter (คืน RFC 9457 ProblemDetails) |
| Error | `Result<T>` ไม่ throw exception เป็น control flow; global exception handler สำหรับ unexpected |
| AuthN | JWT access token อายุ 15 นาที + refresh token (rotation, hash เก็บใน DB) |
| AuthZ | policy-based: `CourseOwner`, `EnrolledInCourse`, `AdminOnly`, `InstructorOnly` |
| Rate limit | ASP.NET Core RateLimiter + Redis: login, playback-token, checkout เข้มเป็นพิเศษ |
| Idempotency | webhook + checkout ใช้ `Idempotency-Key`; ตาราง `PaymentWebhookEvents` มี unique index บน provider event id |
| Reliability | Outbox pattern (`OutboxMessages`) สำหรับ event ที่ต้องส่งออกนอกระบบ |
| Observability | OpenTelemetry (trace/metric/log) → OTLP; correlation id ทุก request |
| Local dev | .NET Aspire AppHost ยก MSSQL + Redis + API + Angular ขึ้นด้วยคำสั่งเดียว |

---

## 3. Frontend — โครงสร้าง Angular 22

```
frontend/
  src/app/
    core/           # interceptor, guard, auth service, api client base, error handler
    shared/         # ui component, pipe, directive, a11y util (ไม่มี business logic)
    layouts/        # public-layout, learn-layout, instructor-layout, admin-layout
    features/
      home/  catalog/  course-detail/  checkout/  learn/  certificate/
      instructor/{courses,builder,analytics,announcements}/
      admin/{cms,marketing,revenue,users}/
    styles/         # design tokens, tailwind config, theme (light/dark)
  server.ts         # SSR entry
```

**ข้อกำหนดหลัก**
- Standalone components เท่านั้น (ไม่มี NgModule)
- **Signals** เป็น state หลัก; `resource()` / `httpResource()` สำหรับ data fetching; RxJS ใช้เฉพาะ event stream จริง ๆ
- **Zoneless change detection** (`provideZonelessChangeDetection()`) — ห้ามพึ่ง zone.js
- Route-level lazy loading ทุก feature + `@defer` สำหรับ block หนัก (player, chart)
- Typed reactive forms เท่านั้น
- SSR-safe: ห้ามแตะ `window`/`document` ตรง ๆ — ใช้ `afterNextRender()` หรือเช็ค `isPlatformBrowser`
- i18n TH/EN ตั้งแต่ต้น (ห้าม hardcode ข้อความไทยใน template)
- Styling: **Tailwind CSS v4** + design token + Angular CDK สำหรับ a11y primitive
- Charts: ECharts (instructor analytics)

---

## 4. Video pipeline & DRM (SE-01, SE-02, LX-03)

**Upload (instructor)**
```
Instructor → API ขอ direct-upload URL → อัปโหลดตรงเข้า provider (ไม่ผ่าน API เรา)
          → provider webhook แจ้ง "ready" → เราอัปเดต MediaAssets.Status
          → Hangfire job ดึง duration/rendition มาเก็บ + คำนวณ Course.TotalDurationSeconds
```

**Playback (learner)** — จุดที่บังคับสิทธิ์ทั้งหมด
```
1. FE เรียก POST /api/playback/{episodeId}/session
2. API ตรวจ: (a) enrollment ยัง active ไหม (b) episode เป็น free preview ไหม
             (c) จำนวน active device ≤ limit (Redis) (d) rate limit
3. API คืน { manifestUrl(signed, TTL 2-5 นาที), drmLicenseUrl(proxy ของเรา),
             watermark: { text, seed, intervalMs } }
4. Shaka Player ขอ license → ผ่าน /api/drm/license (proxy) → ตรวจสิทธิ์ซ้ำ → forward ไป provider
5. FE วาด watermark overlay: ชื่อ+อีเมล ย้ายตำแหน่งสุ่มทุก ~8 วินาที, opacity ต่ำ
6. FE ส่ง heartbeat progress ทุก 15 วิ → บันทึก LastPositionSeconds (resume playback)
```

**สิ่งที่ DRM ทำได้จริง / ไม่ได้ — อัปเดต 2026-08-21 ตาม DRM tier จริงที่เลือก (`docs/DECISIONS.md` Q1)**

⚠️ **ตัดสินใจแล้วว่า v1 ใช้ MediaCage Basic (Bunny Stream) ไม่ใช่ Enterprise** — เป็น clear-key encryption ไม่ใช่ Widevine/PlayReady/FairPlay ของจริง รายการด้านล่างที่เคยเขียนไว้ตอนวางแผน (สมมติว่าใช้ DRM มาตรฐานอุตสาหกรรม) **ไม่ตรงกับ tier ที่เลือกจริงแล้ว** เก็บไว้ให้เห็น baseline เดิม + หมายเหตุกำกับว่าอะไรใช้ไม่ได้กับ Basic:
- ✅ กัน IDM, browser extension, `youtube-dl` ประเภทดึง manifest ตรง — **ยังใช้ได้กับ MediaCage Basic** (มันคือจุดแข็งหลักของ tier นี้ตามเอกสาร Bunny)
- ~~✅ บังคับ HDCP บนบางแพลตฟอร์ม~~ — **ใช้ไม่ได้กับ Basic** HDCP ผูกกับ hardware-backed DRM (Widevine L1/FairPlay) เท่านั้น ซึ่ง Basic ไม่มี
- ❌ ไม่กันการถ่ายจอด้วยกล้อง/มือถือ → นี่คือเหตุผลที่ต้องมี dynamic watermark คู่กัน (สืบย้อนหาคนปล่อยได้) — **สำคัญกว่าเดิมตอนนี้** เพราะ Basic ไม่มี hardware-level key protection คอยรับภาระอีกชั้นเหมือน DRM จริง watermark จึงเป็นแนวป้องกันหลัก ไม่ใช่แค่เสริม
- ~~⚠️ Firefox/Linux บางชุดรองรับแค่ Widevine L3 — ต้องทดสอบ browser matrix~~ — **ไม่เกี่ยวแล้ว** Basic ไม่ใช้ Widevine เลยไม่มีปัญหาเรื่อง L1/L3 แต่ browser matrix test (P2-30) ยังต้องทำอยู่ดี แค่เปลี่ยนโฟกัสไปที่ "Embed View เล่นได้ครบ browser ไหม" แทน
- ⚠️ **ยังไม่ยืนยัน**: MediaCage Basic บังคับเล่นผ่าน Bunny's Embed View เท่านั้น (เอกสาร Bunny ระบุไว้) — ยังไม่ได้ทดสอบว่ากระทบแผน custom Shaka Player (D-09, ปุ่มควบคุม/watermark overlay/resume logic ของเราเอง ตาม P2-20~23) แค่ไหน ต้องทดสอบกับบัญชีจริงก่อนเริ่ม P2-20 ถ้าเข้ากันไม่ได้ต้องคุย scope ใหม่

---

## 5. Concurrent login control (SE-03)

> **อัปเดต 2026-08-17 (P0-17):** สลับ source of truth จากแผนเดิม — ดูเหตุผลด้านล่าง

```
login สำเร็จ → นับ UserSessions ที่ active (RevokedAtUtc IS NULL) ใน MSSQL รวมตัวที่เพิ่งสร้าง
             → เกิน limit (default 2, override รายบัญชีได้ผ่าน Users.MaxConcurrentSessionsOverride)
               → revoke session เก่าสุด (เรียงตาม CreatedAtUtc) + revoke refresh token ของ session นั้น
               → เขียน SecurityAudit + คิวอีเมลแจ้งผู้ใช้ที่ถูกเตะ
             → ทั้งหมดอยู่ใน SaveChangesAsync เดียวกับ login (atomic)
             → หลัง save สำเร็จแล้วค่อย mirror ลง Redis key session:{userId}:{sessionId} (TTL = refresh token)
               — Redis ล่มตอนนี้ = แค่ log warning ไม่ block login (fail-open)
```

**MSSQL เป็น source of truth ของการตัดสิน evict** ไม่ใช่ Redis ตามที่ร่างไว้เดิม — ตัดสินใจเปลี่ยนตอนทำ P0-17 เพราะ:
- ยังไม่มีอะไรอ่าน Redis กลับมาใช้ตัดสินสิทธิ์จริง (playback ยังไม่มี ต้องรอ Phase 2 media module)
- ทำให้ authentication เองไม่ต้องพึ่ง Redis ถึงจะทำงานได้ — Redis ล่มไม่ควร block การ login
- ง่ายกว่าและ atomic กับ transaction ของ login เองได้ทันที

**เมื่อ Phase 2 (playback) เริ่มสร้างจริง ต้องตัดสินใจใหม่:** ตอนนั้น Redis จะต้องถูกอ่านบ่อยมาก (ทุก playback token request) ซึ่งเป็นเหตุผลเดิมที่อยากให้ Redis เป็น source of truth (เร็วกว่า query MSSQL ทุกครั้ง) — ตอนนั้นต้องตัดสินใจว่าจะ (ก) ยอมรับ Redis mirror ที่อาจ lag เล็กน้อยจาก MSSQL หรือ (ข) สร้างกลไกให้ Redis authoritative จริงพร้อม fail-closed สำหรับ playback ตามที่ SECURITY.md เขียนไว้ ดู `P2-04` ใน `docs/TASKS.md`
**ข้อจำกัดที่รู้อยู่แล้ว:** TTL ของ Redis key ตั้งครั้งเดียวตอน login ไม่ได้ refresh ตอน token rotation (Refresh) — ไม่กระทบอะไรตอนนี้เพราะยังไม่มีใครอ่าน แต่ต้องแก้ก่อน Phase 2 พึ่งพา key freshness จริง

---

## 6. Search (LX-01)

v1: MSSQL Full-Text Index บน `Courses.Title`, `Subtitle`, `Description` + filter ด้วย computed/indexed column
`(CategoryId, Level, Price, RatingAverage, Status, PublishedAtUtc)`
Mega Menu = `Categories` แบบ hierarchical (self-reference) cache ไว้ที่ Redis + output cache 5 นาที
เกณฑ์ย้ายไป dedicated search engine: คอร์ส > 10k หรือ p95 ของ search > 300ms

---

## 7. Environments & CI/CD

| env | ที่อยู่ | ใช้ทำอะไร |
|-----|--------|----------|
| Local | เครื่อง dev (Aspire + docker) | พัฒนา + เทสต์ |
| Staging | Contabo (compose คนละชุด, subdomain `staging.`) | UAT + smoke test ก่อนขึ้นจริง |
| Production | Contabo | จริง |

Pipeline: `build → unit test → integration test (Testcontainers) → architecture test → build image → deploy staging → smoke test → manual approval → backup DB → migration bundle → deploy prod`
Migration ขึ้น prod ด้วย **migration bundle** เท่านั้น ห้าม `Database.Migrate()` ตอน startup
รายละเอียด infra, backup, hardening, ข้อจำกัด license ของ MSSQL → `DEPLOYMENT.md`
รายละเอียด flow การชำระเงินและตรวจสลิป → `PAYMENT.md`
