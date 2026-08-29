# Closed Beta Testing & Feedback Runbook (P7-10)

This checklist governs the Closed Beta phase with 50 invited learners and 5 core instructors prior to public launch.

## 1. Beta Cohort Setup

- [x] **50 Invited Learners**: Pre-registered or invited with dedicated sign-up invitation tokens.
- [x] **5 Seeded Instructors**: Active profiles with 5 diverse courses seeded across categories.
- [x] **Test Payment Sandbox**: Stripe test keys & simulated Thai PromptPay QR codes with 100% discount promo codes (`BETATEST100`).

---

## 2. Beta Testing Focus Areas

1. **Course Discovery & Video Streaming**:
   - Playback smoothness across desktop and mobile devices.
   - Quality switching (360p, 720p, 1080p, Auto) and BunnyCDN token authentication.
   - Resume playback and progress tracking accuracy.
2. **Interactive Assessments & Quizzes**:
   - Completing quizzes, receiving immediate score feedback, and passing thresholds.
   - Auto-issuance and verification of completion certificates (`/certificates/verify/{code}`).
3. **Commerce & Checkout**:
   - Order creation, VAT 7% invoice generation, coupon redemption.
   - Requesting refunds within the 7-day guarantee window.
4. **Community & Q&A Discussions**:
   - Posting questions, upvoting answers, and instructor pinning.
   - Real-time in-app and email notification delivery.
5. **Account Management & PDPA**:
   - Multi-device login and session revocation (`/account/devices`).
   - Self-service personal data export (`/account/privacy`).

---

## 3. Bug Triage & Feedback Collection

- Integrated feedback modal / widget directing bug reports to `beta@siriupskill.com`.
- Daily bug review during standup; Sev 1/2 fixes deployed with hotfix tags.
- Post-beta learner satisfaction survey (target CSAT > 90%).
