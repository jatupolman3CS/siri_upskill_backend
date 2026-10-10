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
    Siri.Modules.Live/               # P11 (D-21): Google Meet meetings, invites, join log — Repository+Service, schema LIVE
    Siri.Integrations.Video/         # IVideoProvider + adapters (Bunny/Mux/Cloudflare)
    Siri.Integrations.Payment/       # IPaymentMethod + Stripe adapter — v1 PromptPay QR ผ่าน Stripe PaymentIntent + webhook (ดู PAYMENT.md)
    Siri.Integrations.Storage/       # IFileStorage + R2FileStorage (Cloudflare R2, private bucket — เอกสารประกอบการสอน, P4-03c)
    Siri.Integrations.Email/
    Siri.Integrations.Google/        # P11: ICalendarProvider (Calendar API v3 + Meet via conferenceData) — ยังไม่สร้าง รอ Q10
    Siri.Integrations.Ai/            # P12: ILlmClient (Anthropic SDK) + budget guard — ยังไม่สร้าง รอ Q12
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
| Reliability | Outbox pattern (`OutboxMessages`) สำหรับ event ที่ต้องส่งออกนอกระบบ · อีเมล/แจ้งเตือนใช้ outbox → Kafka → idempotent consumer + Redis (opt-in) — ดู § 9 |
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

**ปัจจุบัน (D-22, 2026-10-09): Meilisearch จับคู่ข้อความ · PostgreSQL คุมที่เหลือ** — `GET /api/catalog/courses/search?q=` ถาม Meilisearch ว่า "คอร์สไหน match ข้อความนี้ เรียงดีสุดก่อน" (ชื่อ/คำโปรย/รายละเอียด/ชื่อหมวด **และชื่อผู้สอน**) ได้ลำดับ course id กลับมา แล้ว `SearchCoursesHandler` เอา id ชุดนั้นไปกรองต่อใน PostgreSQL (Published-only, category/instructor/level/price/rating, facet, sort, paging, wishlist) — response shape ไม่เปลี่ยน

```
q ──► Meilisearch (index = Meilisearch__DocumentsIndexUid, doc type "course") ──► [course ids best-first]
        │ null (ปิด/ล่ม/timeout/circuit เปิด) หรือ [] (ไม่เจอ)
        ▼
      pg_trgm (similarity + ILIKE บน Title/Subtitle/Description, + ILIKE ชื่อผู้สอน)  ← fallback เดิม
        ▼
      PostgreSQL: Status = Published ∧ filters ∧ facets ∧ sort ∧ paging  ──► SearchCoursesResponse
```

- **index เป็นสำเนา derived** ของคอร์สที่ Published (`CourseSearchDocument`: id `course_{guid}`, `type`, title, subtitle, description (ตัด HTML ≤ 4,000 ตัวอักษร), `instructorName`, `instructorHeadline`, ชื่อหมวด th/en) — searchable ตามลำดับ title → instructorName → subtitle → ชื่อหมวด → headline → description · filterable: `type`, `instructorId`, `categoryId`
- **คงให้ตรงกับ DB** (`CourseSearchIndexer` อ่านจาก PostgreSQL เสมอ จึง idempotent): approve/unpublish sync ทันที (best-effort) · `CourseSearchIndexBootstrapper` ตอน host start (สร้าง index+settings, เติมถ้าว่าง) · recurring job `course-search-reindex` ทุกชม. (นาทีที่ 15) · admin: `POST /api/catalog/admin/search/reindex`, `GET /api/catalog/admin/search/status`
- **ความทนทาน**: search timeout 2 วิ → fallback; ล้มเหลวติด 3 ครั้ง → circuit เปิด 30 วิ; ไม่มี URL/key ที่ใช้งานได้ → ปิดทั้งฟีเจอร์ ใช้ `pg_trgm` เหมือนเดิม
- โค้ด: `Siri.Modules.Catalog/Infrastructure/Search/*` (client, options, bootstrapper, job) · `Application/CourseSearchIndexer.cs`, `ICourseSearchIndex.cs` · config ดู `docs/DEPLOYMENT.md` § Meilisearch

Mega Menu = `Categories` แบบ hierarchical (self-reference) cache ไว้ที่ Redis + output cache 5 นาที
(ข้อความเดิมก่อน D-20/D-22: MSSQL Full-Text Index → แทนที่ด้วย `pg_trgm` ที่ D-20 แล้ว Meilisearch ที่ D-22 · เกณฑ์ย้ายไป dedicated engine "คอร์ส > 10k หรือ p95 > 300ms" ถูกข้ามไปตามคำสั่งเจ้าของโปรเจ็ค)

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

---

## 8. Hybrid Live + AI Study (P11/P12 — D-21, 2026-09-16 · ยังเป็นแบบ ไม่มีโค้ด)

แบบเต็มอยู่ที่ **`HYBRID_LIVE.md`** — สรุปเฉพาะที่กระทบ boundary:

```
ตารางสอน (CATALOG.COURSE_LIVE_SESSIONS)  ← Catalog aggregate (publish invariant + SSR course detail ต้องเห็น)
       │ ILiveMeetingSink (ประกาศใน Catalog.Contracts, implement โดย Live — แบบเดียวกับ IEpisodeAccessReader)
       ▼
Siri.Modules.Live  ──► Siri.Integrations.Google (ICalendarProvider)     job: live-meeting-sync
       │  อ่าน: Catalog.Contracts.ILiveScheduleReader · Learning.Contracts.GetActiveEnrolledUserIdsAsync · Identity.Contracts.IUserContactReader
       │  เขียน: Notification.Contracts.IEmailOutbox (+ ICS calendar part)                          job: live-invite-reconcile / live-session-reminders
       ▼
POST /api/live/sessions/{id}/join  ← entitlement เดียวกับ playback (HasActiveEnrollmentAsync) + หน้าต่างเวลา + SESSION_JOIN_LOGS
บันทึกคาบ = COURSE_EPISODE ธรรมดา (RecordingEpisodeId) → playback/progress/quiz/AI ใช้ pipeline เดิมทั้งหมด
AI (P12): Bunny Transcribe (chapters) → Siri.Integrations.Ai (summary/watch-plan/copy) → MEDIA.MEDIA_AI_ENRICHMENTS · budget ledger
```
- ไม่มี reference cycle: Live → Catalog/Learning/Identity/Notification (Contracts) · Catalog **ไม่** reference Live
- Stripe ยังเป็น PaymentIntent (ไม่ใช่ Checkout Session) · Shaka ยังเป็นผู้เล่น (ไม่ใช่ Bunny Player SDK) — D-21
- `ModuleAssemblyCatalog.cs` ต้องเพิ่ม `Siri.Modules.Live` ด้วยมือตอนสร้าง

---

## 9. Notification delivery pipeline (Kafka + Redis — D-23, 2026-10-09)

อีเมลและแจ้งเตือนในแอปเดินทางจาก "ที่ที่ business logic เขียน" ไปถึงผู้รับผ่าน **transactional outbox → Kafka → idempotent consumer** โดย Redis เป็นตัวกันส่งซ้ำ/คุมอัตรา/แคช ทั้งหมด **opt-in** ด้วย `Notification:Delivery:Transport` (`Database` = default = พฤติกรรมเดิมทุกอย่าง · `Kafka` = เปิด pipeline นี้)

```
 caller (Identity/Live/Commerce/AnnouncementDispatchJob ...)          ไม่เปลี่ยน: IEmailOutbox.Enqueue / IUserNotificationOutbox.Stage
        │  เขียนแถวใน transaction เดียวกับ business write (commit พร้อมกันหรือไม่เลย)
        ▼
 PostgreSQL  NOTIFY.EMAIL_OUTBOX (Pending/Failed/Queued/Sent)        NOTIFY.NOTIFICATIONS (PublishedAtUtc IS NULL = ยังไม่ประกาศ)
        │  relay = BackgroundService ใน host ที่รัน Hangfire server (API เมื่อ ServerInApi, Workers เสมอ)
        │  email: [BEGIN; SELECT .. FOR UPDATE SKIP LOCKED; mark Queued; COMMIT] → produce (acks=all, idempotent) → broker ไม่รับ = คืนสถานะเดิม
        │  in-app: [BEGIN; SELECT .. FOR UPDATE SKIP LOCKED; produce; mark PublishedAtUtc; COMMIT]  (consumer ไม่อ่านแถว จึงถือล็อกระหว่าง produce ได้)
        ▼
 Kafka   {prefix}.notification.email.v1   key = messageId      value = { messageId }        (claim check: ไม่มี body/subject/อีเมล)
         {prefix}.notification.inapp.v1   key = userId               value = { notificationId, userId, type }
         *.dlq.v1                         record ที่ประมวลผลไม่ได้เลย (poison / consumer ยอมแพ้) + เหตุผล
        │  consumer group {prefix}.notification.email | .inapp   (manual offset store หลังทำเสร็จ = at-least-once)
        ▼
 email consumer:  โหลดแถวจาก DB → Redis claim (SET NX 5 นาที) → [throttle ต่อนาที] → SMTP → Redis "delivered" (7 วัน) → RecordSent + SaveChanges
 inapp consumer:  DEL notify:unread:{userId} (cache ของ GET /api/notifications/unread-count)
```

**หลักที่ทำให้ "ถูกต้อง" ไม่ใช่แค่ "เร็ว"**

| เรื่อง | ทำอย่างไร | ถ้าพังตรงไหน |
|---|---|---|
| ไม่มี dual write | request ไม่คุยกับ broker เลย — แค่ commit แถว outbox; relay ค่อย publish ทีหลัง | broker ล่ม = แถวค้าง `Pending` (ไม่หาย), relay back-off 1→30 วิแล้วลองใหม่, API ไม่กระทบ |
| ไม่หาย + consumer เห็นแถวที่ commit แล้วเสมอ | อีเมล: claim = tx สั้น (ล็อกแถวด้วย `SKIP LOCKED` → mark `Queued` → **commit**) แล้วจึง publish โดยไม่ถือล็อก DB; ถ้า broker ไม่ ack → `ReleaseAsync` คืนแถวเป็นสถานะเดิม (compare-and-swap ด้วย stamp ของ claim ห้ามทับผลที่ consumer บันทึกไปแล้ว); แถว `Queued` เกิน `QueuedStaleAfterMinutes` (15) ไม่มีผลลัพธ์ → claim/publish ใหม่ | crash ระหว่าง claim กับ publish = ช้า 15 นาที (ไม่หาย); publish ซ้ำ = consumer idempotent · **ทำไมไม่ publish ใน tx เดียวกับล็อก:** consumer อาจรับ record ก่อน relay commit, อ่านแถวสถานะเก่า แล้ว UPDATE ทับ `Queued` — end-to-end test กับ Kafka จริงเจอบั๊กนี้ |
| ไม่ส่งซ้ำ | consumer idempotent 2 ชั้น: สถานะแถวใน DB + Redis claim/delivered; บันทึก "delivered" ลง Redis **ก่อน** `SaveChanges` | ซ้ำได้เฉพาะ crash ระหว่าง "SMTP รับเมล" กับ "เขียน Redis หนึ่งคำสั่ง" (หน้าต่างแคบมาก — ยอมรับ, บันทึกไว้) |
| retry | state อยู่ใน DB เหมือนเดิม (backoff 1,2,4,8 นาที, สูงสุด 5 ครั้ง ใน `EMAIL_OUTBOX_MESSAGE`) → ถึงเวลา relay publish ใหม่ | หมดสิทธิ์ = `Failed` + `NextRetryAtUtc = null` = dead letter (metric `notification.email.exhausted` + log Error); แถวนั้นคือ record ให้คนตามต่อ |
| poison | payload อ่านไม่ได้ → ส่ง DLQ ทันที + commit offset ไป record ถัดไป (ไม่ค้าง partition) | DLQ publish ไม่สำเร็จ = **ไม่** store offset, consumer restart (ไม่ทิ้ง record เงียบ ๆ) |
| ลำดับ / partition | key = message id → ทุกครั้งที่ publish message เดียวกัน (ครั้งแรก, retry, claim ซ้ำ) ลง partition เดียวและถูกอ่านตามลำดับ · **ไม่**ใช้อีเมลผู้รับ (หรือ hash ของมัน) เป็น key เพราะ hash ของอีเมลเดาย้อนได้จากรายชื่อ — ลำดับข้ามอีเมลคนละฉบับของคนเดียวกันไม่เคยรับประกัน (retry/republish สลับลำดับอยู่แล้ว) | — |
| PII | claim check (record = แค่ message id) + key เป็น message id → ลิงก์ reset password / อีเมลผู้ใช้ไม่เข้า broker (retention 7 วัน); DLQ `reason` เป็นชื่อชนิด exception เท่านั้น ไม่ใส่ข้อความ exception (อาจมี host/ค่า) — รายละเอียดอยู่ใน log | — |
| Redis ล่ม | fail-open ทุกจุด (guard/throttle/cache) → ส่งต่อโดยยึดสถานะแถวใน DB | ได้ at-least-once ที่หน้าต่างซ้ำกว้างขึ้นเล็กน้อย ไม่เคยทำอีเมลหาย |

**Redis ใช้ทำอะไร (3 อย่าง ไม่ใช่ source of truth)**: (1) `IEmailDeliveryGuard` — claim/delivered ต่อ message id (claim มี token เจ้าของ: ผู้ถือที่ claim หมดอายุแล้วปล่อย claim ของคนที่เข้ามาแทนไม่ได้ — compare-and-delete ฝั่ง Redis); (2) `IEmailSendThrottle` — งบ `MaxEmailsPerMinute` ต่อนาทีที่ใช้ร่วมทุก consumer instance (ประกาศถึงผู้เรียนหลักพัน = Kafka รับ burst, SMTP ไม่โดนยิงรัว; 0 = ไม่จำกัด); (3) `IUnreadNotificationCounter` — cache-aside ของจำนวนที่ยังไม่อ่าน (badge กระดิ่งถูกถามทุกหน้า) invalidate โดย event `inapp` + ตอน mark read, TTL `UnreadCountCacheSeconds` (30) เป็นตาข่ายนิรภัย

**สองทางส่งใช้โค้ดเดียวกัน**: Hangfire job `email-outbox-send` (Transport=`Database`) กับ Kafka consumer เรียก `EmailDeliveryHandler.DeliverAsync` ตัวเดียว → ใช้ Redis claim เดียวกัน → ช่วง rolling deploy หรือ host ที่ตั้งค่าไม่ตรงกัน (host หนึ่งยัง `Database` อีก host เป็น `Kafka`) ก็ไม่ส่งซ้ำ — **ข้อแม้:** ต้องเป็น host ที่รันโค้ดเวอร์ชันนี้ทั้งคู่ (binary เก่าไม่มี claim) และ Redis ต้องตอบ (ล่ม = fail-open ตามเดิม) จึง deploy โค้ดใหม่ให้ครบทุก host ก่อนค่อยพลิก `Transport`; `Transport=Kafka` แล้ว job จะ stand down และ adopt แถว `Queued` ที่ค้างเมื่อสลับกลับ `Database`

**รันที่ไหน**: relay + consumer เป็น `BackgroundService` ใน host เดียวกับที่รัน Hangfire server — `Siri.Api` เมื่อ `Hangfire:ServerInApi=true` (default), `Siri.Workers` เสมอ (`AddHangfireForApi` / `Program.cs` เรียก `AddNotificationDelivery`) · รันสอง host พร้อมกันปลอดภัย (relay claim ด้วย `SKIP LOCKED`, consumer อยู่ group เดียวกันแบ่ง partition)

**Namespace ต่อ environment**: `Kafka:TopicPrefix` (default `siriupskill`) ขึ้นหน้าทุก topic/group → dev, staging, prod ใช้ cluster เดียวกันได้โดยไม่อ่าน record ของกัน · `Kafka:ReplicationFactor` = จำนวนสำเนา (1 บน single node)

**สังเกตการณ์**: meter `Siri.Notification` (`notification.relay.published|failures`, `notification.email.delivered|failed|exhausted|skipped|throttled`, `notification.inapp.events`) และ activity source `Siri.Messaging` (consumer ต่อ trace จาก header `traceparent` ของ producer) · Workers เปิดทั้งสองใน OpenTelemetry แล้ว · สิ่งที่ควร alert: `exhausted > 0`, `relay.failures` ขึ้นต่อเนื่อง, consumer lag ของ group `*.notification.email`

**Backlog ยาว ≠ record หาย (stale sweep ที่รู้ตัว)**: แถว `Queued` เกิน `QueuedStaleAfterMinutes` (ขั้นต่ำ 10, default 15) จะถูก claim ใหม่ **เฉพาะเมื่อไม่มี consumer ทำ record เสร็จเลยในช่วงเวลาเดียวกัน** — consumer เขียน heartbeat ลง Redis (`notify:email:heartbeat:{group}`, ไม่เกินวินาทีละครั้งต่อ process) ทุกครั้งที่จัดการ record ได้ ถ้า heartbeat ยังสด แปลว่าแถวที่ค้างแค่ต่อคิวอยู่ใน Kafka (เช่น ประกาศถึงผู้เรียนหมื่นคน + SMTP ช้า หรือชนงบ `MaxEmailsPerMinute`) จึงไม่ publish ซ้ำทั้งหางคิวทุกรอบ · ถ้า record หายจริง (relay ตายระหว่าง claim กับ publish) แถวนั้นรอจน consumer เงียบไปครบช่วง stale แล้วถูกกวาด (ช้าแต่ไม่หาย) · Redis ล่ม = ไม่รู้ความคืบหน้า → กวาดตามปกติ (fail-open) · ถ้าต้องการให้แน่ใจว่าแถวหลังคิวไม่ถูก claim ซ้ำเลย ให้ตั้ง `QueuedStaleAfterMinutes` เกินเวลาที่ burst ใหญ่สุดใช้ระบาย (ผู้รับ ÷ อัตราส่ง)

**ไม่เลือก**: publish ตรงจาก handler (dual write) · CDC/Debezium (infra หนักเกินสำหรับ VPS เดียว) · Redis Streams เป็น backbone (ไม่มี retention/replay/consumer-group semantics ระดับที่ต้องการ และ Redis ถูกกำหนดให้ fail-open ไม่ใช่ที่เก็บของที่ห้ามหาย)
