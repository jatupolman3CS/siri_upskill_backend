# Go-Live & Launch Readiness Checklist (P7-11)

This document contains the step-by-step procedure for public launch (Go-Live) of SiriUpSkill.

## 1. Pre-Launch Verification (T-24 Hours)

- [x] **Automated Tests**:
  - Backend unit tests passing 100% (`602/602 tests passed`).
  - Architecture layering tests passing 100% (`4/4 passed`).
  - Frontend Vitest suite passing 100% (`43/43 test files, 218/218 tests passed`).
  - Frontend production build compiles cleanly without errors.
- [x] **Security & Secret Validation**:
  - `ProductionConfigurationGuard` wired to fail-fast on insecure placeholders.
  - OWASP security headers (HSTS, CSP, X-Frame-Options, X-Content-Type-Options) active.
  - Sensitive financial records (Tax IDs, bank accounts) encrypted with AES-256-GCM.
- [x] **Load Test Validation**:
  - 5,000 concurrent viewers benchmarked (`viewer-stream.js`).
  - 500 checkouts/min flash burst verified (`checkout-burst.js`).
  - 200 req/sec catalog search throughput confirmed (`search-burst.js`).

---

## 2. Infrastructure & Deployment Cutover (T-0 Hours)

1. **DNS Cutover & SSL**:
   - Point `A` record for `siriupskill.com` and `api.siriupskill.com` to production VPS IP (`161.97.108.156`).
   - Caddy auto-issues Let's Encrypt TLS certificates upon first inbound handshake.
2. **Environment Configuration**:
   - Run `scripts/generate-production-secrets.ps1` or `.sh` to populate `.env.production`.
   - Update live Stripe keys (`sk_live_...`, `pk_live_...`, `whsec_...`).
   - Update live BunnyCDN pull zone and stream library credentials.
3. **Database Migration & Initial Seeding**:
   ```bash
   docker compose -f docker-compose.prod.yml up -d mssql redis
   dotnet ef database update --project backend/src/Siri.Persistence --startup-project backend/src/Siri.Api
   dotnet run --project backend/src/Siri.Api -- --seed
   ```
4. **Start Application Services**:
   ```bash
   docker compose -f docker-compose.prod.yml up -d
   ```

---

## 3. Post-Launch Smoke Testing (T+15 Minutes)

1. **Health Probes**:
   - `GET https://api.siriupskill.com/health` -> HTTP 200 OK.
   - `GET https://api.siriupskill.com/health/ready` -> HTTP 200 OK (DB & Redis connected).
2. **Registration & Auth**:
   - Register a new test user account, verify email confirmation link.
3. **Live Payment Verification**:
   - Execute small 20 THB test transaction with real credit card and PromptPay QR.
   - Verify webhook receives `checkout.session.completed` and order transitions to `Paid`.
4. **Video Streaming Verification**:
   - Open a video lesson, verify BunnyCDN token negotiation and 1080p playback.
5. **Tax Invoice Generation**:
   - Download PDF receipt & e-Tax invoice from order history.
6. **PDPA Self-Service**:
   - Test `/account/privacy` JSON data export.
