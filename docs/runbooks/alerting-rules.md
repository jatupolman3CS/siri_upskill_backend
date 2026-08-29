# Production Alerting Rules & Monitoring Thresholds (P7-06)

This document defines the production alerting thresholds, severities, and notification channels configured in Prometheus, Grafana, and OpenTelemetry for SiriUpSkill.

## 1. Alerting Matrix

| Alert Name | Metric / Expression | Evaluation Window | Severity | Action & Escalation |
|---|---|---|---|---|
| **HighHttp5xxErrorRate** | `sum(rate(http_requests_total{status=~"5.."}[5m])) / sum(rate(http_requests_total[5m])) > 0.01` | 2 minutes | **Critical (Sev 1)** | On-call engineer paged immediately; check application error logs via Vector / Grafana Loki. |
| **ApiLatencyDegraded** | `histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le)) > 0.5` | 5 minutes | **High (Sev 2)** | Investigate slow queries, connection pool saturation, or external API timeouts. |
| **DbConnectionPoolExhaustion** | `db_connection_pool_active / db_connection_pool_max > 0.80` | 2 minutes | **High (Sev 2)** | Check for uncommitted transactions or slow queries leaking connections. |
| **RedisMemorySaturation** | `redis_memory_used_bytes / redis_memory_max_bytes > 0.80` | 5 minutes | **Warning (Sev 3)** | Verify LRU eviction policy; investigate session or cache bloat. |
| **DiskSpaceCritical** | `node_filesystem_free_bytes / node_filesystem_size_bytes < 0.15` | 5 minutes | **Critical (Sev 1)** | Rotate / archive application logs and temporary media files. |
| **HangfireJobFailures** | `increase(hangfire_jobs_failed_total[15m]) > 5` | 15 minutes | **High (Sev 2)** | Inspect Hangfire Dashboard (`/hangfire`) and retry failed background jobs. |
| **StripeWebhookFailures** | `increase(stripe_webhooks_failed_total[10m]) > 0` | 5 minutes | **Critical (Sev 1)** | Replay failed webhooks from Stripe Dashboard; check signature and secret key. |

---

## 2. Notification Channels

1. **Slack / Discord Webhook**: `#alerts-production` channel for all Sev 1, Sev 2, and Sev 3 alerts.
2. **SMS / Phone Call (PagerDuty / Opsgenie)**: Sev 1 (Critical) incidents requiring immediate response.
3. **Email**: `devops@siriupskill.com` for warning-level alerts and daily summary digests.
