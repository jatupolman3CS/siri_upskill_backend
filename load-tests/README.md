# SiriUpSkill Load & Performance Testing Suite (P7-01)

This directory contains automated **k6** load testing scripts, profiles, and runner scripts designed to benchmark and stress-test the SiriUpSkill platform against launch targets.

## Performance & Scalability Targets

| Metric | Launch Target | Test Scenario | Threshold |
|---|---|---|---|
| **Concurrent Viewers** | 5,000 active streams | `viewer-stream.js` | Heartbeat p95 < 150ms, Read p95 < 200ms, Error < 0.5% |
| **Burst Checkouts** | 500 orders/minute | `checkout-burst.js` | Checkout p95 < 500ms, Error < 1% |
| **Catalog Search Throughput** | 200 searches/sec | `search-burst.js` | Search p95 < 150ms, Error < 0.1% |

---

## Prerequisites

Install [k6](https://k6.io/):

- **Windows**: `winget install k6` or `choco install k6`
- **macOS**: `brew install k6`
- **Linux**: `sudo apt-get install k6` or via official apt repository

---

## Running the Scenarios

### Using PowerShell (Windows)
```powershell
# Run default search burst test
.\load-tests\run-load-tests.ps1 -Scenario search -BaseUrl http://localhost:5190

# Run checkout burst test
.\load-tests\run-load-tests.ps1 -Scenario checkout -BaseUrl http://localhost:5190

# Run full concurrent viewer test (5,000 VUs)
.\load-tests\run-load-tests.ps1 -Scenario viewer -BaseUrl http://localhost:5190

# Run all test scenarios sequentially
.\load-tests\run-load-tests.ps1 -Scenario all -BaseUrl http://localhost:5190
```

### Using Bash (Linux / macOS)
```bash
chmod +x load-tests/run-load-tests.sh
./load-tests/run-load-tests.sh search http://localhost:5190
./load-tests/run-load-tests.sh checkout http://localhost:5190
./load-tests/run-load-tests.sh viewer http://localhost:5190
./load-tests/run-load-tests.sh all http://localhost:5190
```

---

## Architectural Protections & Bottleneck Mitigations

1. **Output Caching & Full-Text Search**:
   - Course listings and search queries leverage ASP.NET Core Output Caching (`.CacheOutput()`) with Redis backplane and tag-based cache eviction on course publication/updates.
2. **Video CDN Offloading**:
   - Video streaming bytes are served directly by BunnyCDN Edge Edge POPs and signed token validation. The backend API handles only authentication, token minting, and lightweight heartbeat pings.
3. **Database Connection Pooling & Indexing**:
   - All high-frequency query paths (courses by status/category, user orders, enrollments, user sessions) are covered with composite indexes (see migration `20260827100000_AddProductionOptimizationIndexes`).
4. **Rate Limiting**:
   - High-risk endpoints (login, password reset, checkout, search) are guarded by ASP.NET Core RateLimiter policies (`auth`, `search`, `payment`).
