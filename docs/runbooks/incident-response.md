# Production Incident Response Runbook (P7-06)

This runbook outlines standard operating procedures for triaging, mitigating, and recovering from production incidents on the SiriUpSkill platform.

## 1. Severity Levels & SLA

- **Sev 1 (Critical Outage)**: Core functionality unavailable (users cannot login, watch courses, or checkout).
  - *Response Time*: < 15 minutes.
  - *Target Resolution*: < 1 hour.
- **Sev 2 (Degraded Performance)**: High latency, non-critical features down (comments, certificate export, analytics delayed).
  - *Response Time*: < 30 minutes.
  - *Target Resolution*: < 4 hours.
- **Sev 3 (Minor Defect / Warning)**: Isolated error, UI glitch, background batch retryable warning.
  - *Response Time*: Next business day.

---

## 2. Standard Triage Checklist

1. **Verify Health Probes**:
   ```bash
   curl -I https://api.siriupskill.com/health
   curl -I https://api.siriupskill.com/health/ready
   ```
2. **Inspect Docker Container Status**:
   ```bash
   docker ps -a
   docker compose -f docker-compose.prod.yml logs --tail=100 api
   docker compose -f docker-compose.prod.yml logs --tail=100 workers
   ```
3. **Check Resource Utilization**:
   ```bash
   docker stats --no-stream
   df -h
   free -m
   ```

---

## 3. Rollback Procedures

If a deployment introduces critical bugs or breaking schema changes:

1. **Revert Frontend**:
   ```bash
   docker compose -f docker-compose.prod.yml pull frontend:previous
   docker compose -f docker-compose.prod.yml up -d frontend
   ```
2. **Revert Backend API & Workers**:
   ```bash
   docker compose -f docker-compose.prod.yml stop api workers
   docker compose -f docker-compose.prod.yml up -d api:previous workers:previous
   ```
3. **Database Migration Rollback**:
   ```bash
   dotnet ef database update <PreviousMigrationName> --project backend/src/Siri.Persistence --startup-project backend/src/Siri.Api
   ```

---

## 4. Specific Outage Playbooks

### A. Redis Cache / Session Outage
- **Symptom**: Fast concurrent login checks failing or output cache returning misses.
- **Remedy**:
  - Redis restart: `docker compose -f docker-compose.prod.yml restart redis`
  - Fallback: Application is designed with MSSQL as authoritative fallback (`ISessionRegistry` writes are opportunistic and do not block core authentication).

### B. Stripe Webhook Synchronization Failures
- **Symptom**: Payments succeed in Stripe but orders stay in `PendingPayment` status.
- **Remedy**:
  - Open Stripe Dashboard -> Developers -> Webhooks.
  - Inspect failed events and error codes.
  - Click **Resend Event** to replay webhooks once API service is confirmed healthy.
  - The backend webhook handler is idempotent (`OrderPlacedEventHandler` / `StripeWebhookEndpoint` handles duplicate event IDs safely).
