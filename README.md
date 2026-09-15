# SIRI UpSkill — Backend API

**A course-commerce backend where video protection is a first-class requirement, not an afterthought.**

SIRI UpSkill is an e-learning marketplace in the SkillLane/FutureSkill mould: learners buy long-term access to video courses, instructors self-publish and get paid a revenue share, and the platform is responsible for making sure paid video isn't trivially copied or shared. This repository is the ASP.NET Core API + background workers behind it — a modular monolith with signed/short-lived playback tokens, webhook-verified payments, and ownership checks enforced on every endpoint that touches money or access rights.

> Frontend (Angular) lives in the sibling repository [`siri_upskill_ui`](https://github.com/jatupolman3CS/siri_upskill_ui).

## Key features

**Learner-facing**
- Course catalog with Thai-aware full-text search (`pg_trgm`), category tree, instructor/price/rating filters
- Course detail with syllabus, free-preview episodes, and learning outcomes
- Checkout via Stripe (PromptPay QR), webhook-driven order fulfilment and enrollment
- Signed, short-lived playback sessions behind Bunny Stream, gated by active enrollment + device/session limits
- Progress tracking, quizzes/assignments, discussion threads per episode

**Instructor-facing**
- Course builder (sections/episodes, TUS resumable video upload, autosave)
- Submit-for-review / approve / reject publishing workflow
- Payout accounts with encrypted bank/tax details and a scheduled revenue-split payout run

**Platform / admin**
- Role-based auth (JWT + refresh-token rotation + reuse detection + concurrent-session eviction)
- Admin moderation for courses, instructors and CMS content (sanitized HTML)
- Announcements, analytics, and an auditable revenue-split ledger

## Architecture

Modular monolith, one Visual Studio project per bounded context, wired together only through explicit `Contracts/` interfaces (enforced by an architecture test suite, not just convention):

| Module | Responsibility |
|---|---|
| `Siri.Modules.Identity` | Auth, sessions, device management |
| `Siri.Modules.Catalog` | Courses, categories, search, instructor applications |
| `Siri.Modules.Commerce` | Orders, pricing, Stripe checkout/webhooks, promo codes |
| `Siri.Modules.Media` | Video asset lifecycle, playback-token issuance (Bunny Stream) |
| `Siri.Modules.Learning` | Enrollments, progress, quizzes, assignments, certificates |
| `Siri.Modules.Payout` | Revenue split, instructor payout batches |
| `Siri.Modules.Cms` | Banners, blog/articles (sanitized on write and read) |
| `Siri.Modules.Community` | Discussion threads per episode |
| `Siri.Modules.Analytics` | Instructor/admin reporting |
| `Siri.Modules.Notification` | Email outbox + Hangfire delivery |

Two data-access styles coexist by deliberate choice, not drift: the original modules (Identity/Catalog/Notification) use a vertical-slice `{UseCase}Handler` per command/query; the modules added later (Commerce/Media/Learning/Payout/Cms/Community/Analytics) use Repository + Service per aggregate. Both are documented in `.claude/rules/backend.md` and enforced the same way — no god services, no cross-module reach into another module's `Domain`/`Infrastructure`.

Endpoints are ASP.NET Core MVC controllers under `src/Siri.Api/Controllers/**`, default-deny (every action must explicitly declare `[Authorize]` or `[AllowAnonymous]`), returning `Result<T>` mapped to RFC 9457 `ProblemDetails` on failure.

## Project status

Actively developed as a portfolio/capstone project, tracked task-by-task in [`docs/TASKS.md`](docs/TASKS.md) (190 tracked items: 46 verified done, 58 implemented pending QA sign-off, 28 partial, the rest planned). It is **not** a finished production deployment — see that file's "สถานะจริงของงานที่ยังไม่ปิด" section for exactly what's outstanding before it could go live, and [`docs/DECISIONS.md`](docs/DECISIONS.md) for the architectural trade-offs made along the way (why Stripe over a card-only PSP, why PostgreSQL over the original SQL Server choice, why MVC controllers over Minimal API, etc.).

## Architecture & code quality notes

Honest self-review, not a polish claim. What holds up well: no `.Result`/`.Wait()`/`async void` anywhere in `src/`, every async method threads a `CancellationToken`, and the "pricing logic lives only in `Commerce/Domain/Pricing`" rule genuinely holds — a full-tree search found zero price math duplicated outside `PricingEngine.cs`. The two coexisting data-access patterns (vertical-slice for Identity/Catalog/Notification, Repository+Service for the seven modules added later) are applied consistently per module, matching what's documented in `.claude/rules/backend.md`, not architectural drift.

Concrete debts, prioritized:

- **A duplicate routing layer survives the 2026-09-01 Minimal-API → MVC Controllers migration.** `Program.cs` only wires `MapControllers()` in production, but 36 `*Endpoints.cs` files are still compiled in and still exercised directly by ~23 integration tests that bypass the controllers. It's parallel surface area, not simple dead code — cleanup is tracked as `X-22`.
- **Revenue-split money math has two trust boundaries in the Payout module.** `RevenueSplitContract` correctly derives instructor/platform amounts from the catalog price + configured share percent; `RevenueSplitService.CreateAsync` (reachable from `AdminPayoutController`) accepts those same amounts as raw caller-supplied numbers with no recomputation. The single-source-of-truth pricing discipline that Commerce enforces strictly hasn't yet been extended to this path.
- **`InstructorCoursesController` (504 lines, 18 actions)** mixes course, section and episode management in one file — the rest of the controllers layer averages ~110 lines; a split into per-resource controllers is a natural next refactor.
- **`COURSE.cs` (605 lines)** carries publish-workflow, denormalized stat recalculation and media-attachment logic together on one aggregate root. Well-documented and invariant-protecting, but a `CourseStatsRecalculator` extraction would shrink it.
- **`PaymentOpsQueueService`/`PayoutBatchService`** are trending toward multiple responsibilities per class (queueing + retry + batch computation + tax certificates) — still single-aggregate per the project's own rule, worth watching as Payout grows.

Not a defect: the UPPERCASE `ENTITY`/`COLUMN` naming spanning every module (including Identity/Catalog/Notification since the 2026-08-29 standardization) is a deliberate, fully-documented convention, applied consistently.

## Windows development (no Docker required)

From `C:\ProjectSiriUpSkill`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\siri_upskill_backend\scripts\dev.ps1 Start
```

Requires PostgreSQL 18, .NET 10 SDK and Node/npm already installed. The script uses the adjacent `siri_upskill_ui` repository. If PostgreSQL is installed elsewhere, pass `-PostgresBin 'C:\your\PostgreSQL\bin'` to both Start and Stop.

- Web: http://localhost:4202 (ports 4200 and 4201 belong to other projects on this machine).
- API / Swagger: http://localhost:5190/swagger.
- Development email inbox: http://localhost:8025. Confirmation emails arrive after the worker's next one-minute cycle.
- PostgreSQL: `127.0.0.1:5433`, database `SIRIUPSKILL`, user `siriupskill_dev`; generated password is stored in `.dev/settings.json` and referenced by `.env`.
- Cache/session store: `127.0.0.1:6380`, using [Microsoft Garnet](https://microsoft.github.io/garnet/docs/getting-started), a Redis-protocol-compatible server for native Windows development.

The script builds .NET, applies migrations, seeds 20 courses and sample accounts, then starts API, workers and Angular with source watching. It downloads pinned Garnet and Mailpit releases into `.dev/tools` and checks SHA-256 hashes. PostgreSQL data, captured email, process IDs and logs stay under the ignored `.dev` directory. Stop retains this data.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\siri_upskill_backend\scripts\dev.ps1 Status
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\siri_upskill_backend\scripts\dev.ps1 Stop
# Start again after backend code changes; add -NoBuild only when binaries are current.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\siri_upskill_backend\scripts\dev.ps1 Start
```

| Account | Email | Development password |
| --- | --- | --- |
| Learner | learner1.seed@example.test | LocalOnlyLearner123! |
| Instructor | instructor1.seed@example.test | LocalOnlyLearner123! |
| Admin | admin@example.test | LocalOnlyAdmin123! |

Native development uses `.env` directly. The launcher also passes these settings to both hosts above stale user-secrets and environment values. It creates a separate PostgreSQL cluster; the existing PostgreSQL service and its databases are preserved. Email goes to local Mailpit. Stripe test keys can be set in `.env`.

Sample courses use the existing public HLS test clip by default, so classroom playback/progress can be tested without a Bunny account. This requires internet access. The launcher switches only the two known sample media assets in `SIRIUPSKILL`; uploaded media remains on its configured provider. To test actual Bunny media, set `SIRI_DEV_SAMPLE_VIDEO=false` in `.env`, configure `VideoProvider__TokenAuthenticationKey` with the **CDN Token Authentication Key** (different from the library API key), and Stop/Start. The seeded video ID must exist in that library. Bunny signing follows the [directory-token specification](https://bunny.net/docs/cdn/security/token-authentication/advanced) so HLS segment requests retain authorization.

For checkout without an external payment provider, log in as admin and create a 100% discount code, then use it as a learner. This exercises the actual order/enrollment transaction. The local database on this machine already has **`DEVFREE`** (100% discount) for testing. This code is test data, not automatically created in new databases.

### Stripe PromptPay test payments

Stripe webhook forwarding is enabled in this machine's `.env` when configured. A fresh setup leaves it disabled until a Stripe test key is available. To enable it, set:

```dotenv
SIRI_DEV_STRIPE_WEBHOOKS=true
```

Keep `Payment__Stripe__SecretKey` and `Payment__Stripe__PublishableKey` set to your test keys, then Stop/Start using `scripts/dev.ps1`. The launcher downloads a pinned official Stripe CLI with checksum verification, obtains its local webhook signing secret, updates only `Payment__Stripe__WebhookSecret` in `.env`, and starts a tracked test-mode listener. API, workers and the listener use the same secret. `Status` includes the listener and `Stop` stops it too. No public webhook URL or deployment is needed; events are forwarded to `http://localhost:5190/api/commerce/webhooks/stripe`.

Choose a course without a 100% discount and click Pay. Scan the displayed **test** QR using a QR reader to open Stripe's test payment page, then select **Authorize Test Payment**. Checkout polls the payment status and automatically opens the success page after the webhook grants access. Stripe CLI logs are in `.dev/logs/stripe.log` and `stripe.err.log`; they can contain the signing secret, so keep them private. Set `SIRI_DEV_STRIPE_WEBHOOKS=false` to start dev without connecting the listener to Stripe.

See Stripe's [local webhook listener documentation](https://github.com/stripe/stripe-cli/wiki/listen-command) for how forwarding works. Use sample videos while testing payments; actual Bunny configuration is independent.

Development allows 100 authentication requests per minute so local login, confirmation and refresh tests can run together; other environments keep the existing limit of 5.

Verified on this machine: start/stop with data retained, API health, 20-course catalog, browser registration, Mailpit delivery by workers, email confirmation, password login, free checkout/enrollment, success-page refresh, classroom content and actual playback of the public sample clip. Playback saved a 12-second position and resumed there after reload. A real Stripe sandbox PromptPay payment created through the checkout UI reached `succeeded`; Stripe CLI forwarded its webhook with HTTP 200, the order became Paid, the browser redirected automatically and one Active enrollment appeared. Replaying the same event did not duplicate access; an invalid signature returned HTTP 400. Real Bunny playback remains optional and needs the correct CDN token key. Bunny signing regression tests passed (21 tests).

### Instructor video uploads

Log in with the instructor account above, open a draft course in Course Builder, and choose a video in either the lesson's upload control or the basic-information trailer control. Uploads require valid `VideoProvider__LibraryId` and `VideoProvider__ApiKey` in the ignored development override. They use [Bunny's TUS protocol](https://bunny.net/docs/stream/tus-resumable-uploads) directly from the browser with short-lived signed headers; the application bearer token is not sent to Bunny.

The progress indicator measures transferred bytes. After transfer, the UI shows Processing until Bunny confirms Ready or Failed. API status polling and the background worker both update the stored asset. Saving the draft retains the media reference across reloads; duration comes from the provider. Newly created lesson IDs are returned by autosave so later saves update the same lessons. Removing a video also removes its draft reference.

Keep `SIRI_DEV_SAMPLE_VIDEO=true` to continue learning with the sample clip. Real uploads still go to Bunny. Playing an uploaded video requires the correct CDN Token Authentication Key independently of successful uploading; the sample switch does not replace uploaded media with a sample.

Verified upload regression: a generated 3-second clip was uploaded from the instructor browser using TUS POST 201 / PATCH 204, reached Ready with the provider's duration, and remained attached after page reload and a development restart. PostgreSQL regressions cover ownership, creating/reordering lessons, removing media, stale-save rollback, and processing after upload-session completion. No database migration is needed for these fixes.

The separate trailer test also transferred real bytes and retained its reference and Processing state after reload; Bunny still reported processing at the last check, so that clip's eventual Ready transition has not been verified. Browser checks confirmed that a newly created section and lesson kept their IDs after subsequent autosaves. Validation: .NET build succeeded without warnings, 817 unit tests, 5 architecture tests, 14 PostgreSQL upload/draft tests, and 362 Angular tests passed; Angular lint/build passed with bundle-size and CommonJS warnings.

Course submission and approval now require all lesson videos and the optional trailer to be owned, Ready, and have positive duration. This passed real PostgreSQL regressions and the instructor-to-admin browser flow. Bunny callbacks now verify the exact request bytes using the library Read-Only API key and the `X-BunnyStream-Signature-*` headers, check the library ID, and refresh provider status instead of trusting callback status/duration. Signed replay, tampered/unsigned requests, foreign libraries and oversized bodies were checked through the local MVC API. See [Bunny signature documentation](https://bunny.net/docs/stream/webhooks). No external webhook URL was configured; provider polling continues to support local development.

## Local integrated stack with Docker (alternative)

Place this repository beside `../siri_upskill_ui`. Install Docker Engine and Compose 2.24.4+.

From this repository:

```powershell
docker compose --env-file .env.example -f docker-compose.dev.yml up -d --build
```

- Web: http://localhost:8080
- Captured email: http://localhost:8025
- API: http://localhost:5190
- PostgreSQL for local tooling: localhost:5433, database `SIRIUPSKILL`, user `siriupskill_app`, password `LOCAL_ONLY_database_password`.
- Redis for local tooling: localhost:6380, password `LOCAL_ONLY_redis_password`.

Seeded accounts: `admin@example.test` / `LocalOnlyAdmin123!`, `learner1.seed@example.test` and `instructor1.seed@example.test` / `LocalOnlyLearner123!`. These sample passwords are used only by the development Compose file.

The local stack starts migrations and seeding as one-off services before the API and workers. Development emails go to Mailpit; open the confirmation/reset links there. No real SMTP credentials are required. The development project uses separate volumes from production.

```powershell
docker compose --env-file .env.example -f docker-compose.dev.yml logs --tail 100 api workers
docker compose --env-file .env.example -f docker-compose.dev.yml down
```

### Optional Stripe and Bunny testing

The sample stack contains no functioning external-provider credentials. Put the following dedicated development variables in `.env`, then use it instead of `.env.example` with `--env-file`:

- `SIRI_DEV_STRIPE_SECRET_KEY` (`sk_test_...`), `SIRI_DEV_STRIPE_PUBLISHABLE_KEY`, `SIRI_DEV_STRIPE_WEBHOOK_SECRET`.
- `SIRI_DEV_VIDEO_LIBRARY_ID`, `SIRI_DEV_VIDEO_API_KEY`, `SIRI_DEV_VIDEO_READONLY_API_KEY`, `SIRI_DEV_VIDEO_PULL_ZONE`, `SIRI_DEV_VIDEO_CDN_HOSTNAME`, `SIRI_DEV_VIDEO_TOKEN_KEY`.

Also copy the non-secret bootstrap variable names from `.env.example` (their values are overridden in the development stack). Configure Stripe's test webhook forwarding to `http://localhost:5190/api/commerce/webhooks/stripe`. Upload/link videos from your own Bunny library; seeded sample media references are not a replacement for a configured library.

### Run .NET and Angular directly

Start PostgreSQL and Redis locally first. Set `ConnectionStrings:Default` and `Redis:ConnectionString` using user-secrets for the API and workers, or an ignored development dotenv file. Use an Npgsql connection string, for example `Host=localhost;Port=5433;Database=SIRIUPSKILL;Username=siriupskill_app;Password=LOCAL_ONLY_database_password` for the development stack.

```powershell
dotnet build SiriUpSkill.sln
dotnet run --project src/Siri.Api --launch-profile http -- --migrate
dotnet run --project src/Siri.Api --launch-profile http
```

Run workers in another terminal so email outbox messages are processed:

```powershell
dotnet run --project src/Siri.Workers -- --environment Development
```

From the UI repository run `npm ci` and `npm start`. The browser proxy and SSR default point at API port 5190. Seeding is optional when running directly: set `Identity:Seed:AdminPassword` and `Identity:Seed:TestUserPassword`, then run the API with `--seed`.

## Configuration precedence

JSON defaults < dotenv < user-secrets < environment variables < CLI arguments. Development and QA load only `.env`. Production loads only `.env_prd` and `.env.production`. Other environments (including IntegrationTest) do not discover dotenv files.

Select the host environment through the launch profile, real environment variables or `--environment`; a value inside a dotenv file does not select the host environment. The API and workers use the same loader.

## PostgreSQL scripts

`scripts/postgresql.sql` is generated from the checked-in migration, uses quoted PostgreSQL identifiers and is idempotent. Regenerate it after any future migration:

```powershell
dotnet ef migrations script --idempotent --project src/Siri.Persistence --startup-project src/Siri.Api --output scripts/postgresql.sql
```

Apply via the explicit API `--migrate` command or run the SQL with `psql -v ON_ERROR_STOP=1 -f scripts/postgresql.sql`. Neither path copies data from an existing SQL Server database.

## Production

`docker-compose.prod.yml` describes a fresh PostgreSQL installation and uses your private `.env.production`. Keep existing data-volume names/credentials aligned when adapting it to an existing deployment.

Fill in:

- `POSTGRES_PASSWORD` for the PostgreSQL bootstrap administrator, `SIRI_DATABASE_PASSWORD` for the restricted application role, and `REDIS_PASSWORD`. Use distinct generated values; base64 avoids connection-string quoting ambiguities.
- Real JWT signing/encryption keys, Stripe live/webhook keys, Bunny configuration and payer information required by the production guard.
- `Email__Provider=Smtp` and nested `Email__Smtp__*` settings so registration/reset email is delivered.
- `SITE_ADDRESS` and `API_ADDRESS` as hostnames, plus matching HTTPS CORS, SEO, confirmation and reset URLs.

Compose supplies internal PostgreSQL/Redis connection strings. A direct `dotnet` production launch also needs an Npgsql `ConnectionStrings__Default` value. The existing private file still uses SQL Server syntax and needs updating before that launch.

```powershell
docker compose --env-file .env.production -f docker-compose.prod.yml config --quiet
docker compose --env-file .env.production -f docker-compose.prod.yml up -d --build
```

Caddy routes browser `/api/*` calls to the API and other pages to Node SSR. SSR uses `API_INTERNAL_URL=http://api:5000` and the configured allowed hostname. PostgreSQL and Redis are not exposed publicly. Back up an existing database before applying migrations.

The two Compose files intentionally separate local testing from production. No deployment or production database modification was performed during integration.

## Tests

```powershell
dotnet test tests/Siri.UnitTests
dotnet test tests/Siri.ArchitectureTests
dotnet test tests/Siri.IntegrationTests
```

The complete integration suite requires Docker for PostgreSQL 17 and Redis containers. The fulfillment tests can alternatively use an explicitly supplied local, disposable PostgreSQL database:

```powershell
$env:SIRI_TEST_POSTGRES_CONNECTION = 'Host=127.0.0.1;Port=55439;Database=siri_test_integration;Username=siri_test_admin'
dotnet test tests/Siri.IntegrationTests --filter FullyQualifiedName~PaymentFulfillmentIntegrationTests
```

This override accepts only loopback IPs and database names starting with `siri_test_`. The test fixture applies migrations and writes test rows there.

The active completion checklist is in [docs/TASKS.md](docs/TASKS.md). Instructor quiz/assignment authoring, grading and actual resource/submission file uploads remain unfinished; the checklist distinguishes verified flows from remaining work.

Anonymous free preview is verified in Chromium: a guest played the sample clip without login or enrollment/discussion requests, while a paid episode returned HTTP 403 and no player. Preview eligibility now also requires the course to be Published. The dev UI is at http://localhost:4202 and API at http://localhost:5190; no database migration is required for these fixes.
