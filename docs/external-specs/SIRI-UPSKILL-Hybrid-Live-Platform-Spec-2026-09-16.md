# Product & System Specification: SIRI UPSKILL Platform (Hybrid Live + VOD)

> **แหล่งที่มา:** เจ้าของโปรเจ็คส่งสเปคนี้เข้ามา 2026-09-16 ผ่านคำสั่ง `/goal planing and design base on exiting proudct`
> เก็บไว้ **ตามต้นฉบับ (verbatim)** เพื่อใช้อ้างอิง — การ reconcile กับสถาปัตยกรรม/การตัดสินใจเดิม
> อยู่ที่ `docs/DECISIONS.md` **D-21** และแผนงานอยู่ที่ `docs/HYBRID_LIVE.md` + `docs/TASKS.md` §P11/§P12
> ถ้าสเปคนี้ขัดกับ `REQUIREMENTS.md`/`DECISIONS.md` ให้ยึดตามผลการ reconcile ใน D-21 ไม่ใช่ไฟล์นี้

---

**Overview:** A hybrid e-learning platform (Google Meet Live + Bunny Stream VOD), featuring Stripe checkout, auto Google Calendar sync, catch-up mode for latecomers, and AI-powered video chapters/summaries.

## 1. Business Requirements (BR)
### 1.1 Customer / Student
* **BR-STU-01 (Discovery):** View course details, hybrid schedules, and syllabus via social-friendly landing pages.
* **BR-STU-02 (Checkout):** Instant purchase via Stripe (Credit Card/PromptPay). Auto-grant access upon success.
* **BR-STU-03 (Calendar Sync):** Auto-receive Google Calendar invites containing Google Meet links for upcoming live sessions.
* **BR-STU-04 (Catch-up Mode):** Late enrollees instantly access past VODs. Attendees can rewatch completed sessions.
* **BR-STU-05 (Playback):** Adaptive HLS streaming, resume playback (saves last watched timestamp), and AI clickable chapters.
* **BR-STU-06 (AI Study):** Access AI-generated summaries and personalized watch-plans to catch up before the next live class.

### 1.2 Merchant / Instructor
* **BR-INS-01 (Course Builder):** Create courses, defining start/end times for multiple dynamic live sessions.
* **BR-INS-02 (Auto Meet):** System automatically generates Google Meet URLs per session (no manual link creation).
* **BR-INS-03 (VOD Management):** Upload recorded videos directly to Bunny Stream to convert past live sessions into VODs.
* **BR-INS-04 (AI Assistant):** Generate marketing copy and session summaries via AI for social media promotion.
* **BR-INS-05 (Dashboard):** Real-time monitoring of enrollments, student rosters, and sales revenue.

### 1.3 Admin
* **BR-ADM-01 (RBAC):** Manage Role-Based Access Control, suspend/unlock accounts.
* **BR-ADM-02 (Billing Audit):** Monitor Stripe webhooks, transactions, and process refunds.
* **BR-ADM-03 (Logs):** Monitor Bunny Stream bandwidth quotas and system audit logs.

## 2. Technical Requirements (TR)
### 2.1 Stack & Architecture
* **Frontend:** Angular (SPA, using `@tus/tus-client` for direct uploads, Bunny Player SDK).
* **Backend:** .NET Core Web API (Clean Architecture / Modular Monolith).
* **Database:** PostgreSQL
* **Workers:** Hangfire or .NET `IHostedService` for async queues.

### 2.2 Core Database Schema
* `Users`: UserId, Email, Role.
* `Courses`: CourseId, Title, Price, Type (Live/VOD/Hybrid).
* `CourseSessions`: SessionId, Start/EndDateTime, GoogleMeetUrl, BunnyVideoId.
* `Enrollments`: EnrollmentId, UserId, PaymentStatus, StripeSessionId.
* `VideoProgress`: UserId, SessionId, LastWatchedSecond.
* `CourseAISummaries`: SessionId, ChaptersJson, SummaryText.

### 2.3 Integration Workflows
#### A. Stripe Payment
* **Flow:** .NET creates Checkout Session -> Angular redirects -> Webhook (`checkout.session.completed`) verifies signature -> updates `Enrollment` status -> triggers Calendar Job. (Must implement Idempotency).

#### B. Google Workspace (Calendar & Meet)
* **Flow:** OAuth2 / Service Account integration. Upon session creation, pass `conferenceData` to generate Meet links.
* **Latecomer Logic:** On payment success, worker iterates sessions. Future sessions -> append user email to Calendar `attendees`. Past sessions -> skip calendar, unlock VOD access only.

#### C. Bunny Stream VOD Pipeline
* **Direct Upload:** Angular requests upload token -> .NET calls Bunny API -> Angular uploads directly to Bunny via Tus protocol (bypassing backend bandwidth).
* **Webhook & DRM:** Bunny webhook triggers on encode completion to update `BunnyVideoId`. Playback requires .NET to generate short-lived SHA256 HMAC signed URLs (Domain Restricted).
* **Tracking:** Frontend captures `timeupdate` every 15s, sending API updates for resume playback.
