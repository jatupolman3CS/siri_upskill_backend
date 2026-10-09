# SIRI UpSkill — Database Design (MSSQL)

## กติกาทั่วไป

| เรื่อง | มาตรฐาน |
|-------|---------|
| PK | `uniqueidentifier` ค่า **UUIDv7** (sequential — ไม่ทำ index fragment เหมือน v4) |
| เวลา | `datetime2(3)` เก็บ **UTC** เสมอ, ชื่อคอลัมน์ลงท้าย `...AtUtc` |
| เงิน | `decimal(18,2)` + `Currency char(3)` — ห้าม `float`/`money` |
| ข้อความ | `nvarchar` เสมอ (ต้องรองรับภาษาไทย), ไม่ใช้ `varchar` |
| Soft delete | `IsDeleted bit` + `DeletedAtUtc` เฉพาะตารางที่ต้องกู้คืนได้ (Course, Post, Discussion) |
| Concurrency | `RowVersion rowversion` บนตารางที่แก้พร้อมกันได้ (Course, Order, Enrollment) |
| Audit | ทุกตารางหลักมี `CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy` (ใส่ผ่าน EF interceptor) |
| Delete rule | ตารางเกี่ยวกับเงิน (Order/Payment/RevenueSplit) **ห้าม cascade delete** และห้ามลบจริง |
| Schema | แยกตาม module: `identity`, `catalog`, `media`, `learning`, `commerce`, `payout`, `cms`, `community`, `notify`, `analytics` |
| Collation | `Thai_100_CI_AS_SC_UTF8` (เรียงภาษาไทยถูก + รองรับ emoji) |

> **อัปเดต 2026-08-20 (`docs/DECISIONS.md` D-17):** ตาราง/entity ใน 7 schema `MEDIA`/`LEARNING`/`COMMERCE`/`PAYOUT`/`CMS`/`COMMUNITY`/`ANALYTICS` **สร้างจริงแล้ว** (migration `AddCommerceMediaLearningPayoutCmsCommunityAnalytics`, ยังไม่ apply ขึ้น DB จริง — ดู CLAUDE.md) โดยใช้ชื่อ **UPPERCASE ทั้งหมด** (schema/table/column และ C# entity class/property) แทนที่ PascalCase ตามสเก็ตช์เดิมด้านล่าง — สเก็ตช์ยังคงไว้เพราะบอก "มีคอลัมน์อะไรบ้าง" ถูกต้อง แค่การสะกดชื่อไม่ตรง (เช่น `Orders.SubtotalAmount` ในสเก็ตช์ = `COMMERCE.ORDERS.SUBTOTAL_AMOUNT` จริง) กฎการแปลงชื่อเต็มอยู่ที่ `.claude/rules/database.md`'s "ชื่อ entity/DB แบบ UPPERCASE" ตารางที่ **ยังไม่ได้สร้าง** จากสเก็ตช์ด้านล่าง: `catalog.EpisodeAttachments`/`LearningPaths`/`LearningPathItems`, `notify.Announcements`/`Notifications` (ยังเป็น PascalCase ปกติเพราะอยู่ schema เดิมที่ไม่ใช่ 7 โมดูลใหม่ — จะสร้างเมื่อ task ที่เกี่ยวข้องเริ่มจริง)

---

## identity
```
Users(Id PK, Email, NormalizedEmail UQ, PasswordHash, DisplayName, AvatarUrl, PhoneNumber,
      Status, EmailConfirmedAt, TwoFactorEnabled, LastLoginAtUtc,
      MaxConcurrentSessionsOverride int NULL,   -- SE-03 per-account override; NULL = ใช้ system default
                                                 -- (Identity:Security:MaxConcurrentSessions, default 2)
      ...audit)
Roles(Id, Name UQ)                         -- Learner, Instructor, Admin, SuperAdmin
UserRoles(UserId, RoleId) PK(UserId,RoleId)
UserSessions(Id PK, UserId FK, DeviceId, DeviceName, UserAgent, IpAddress,
             CreatedAtUtc, LastSeenAtUtc, RevokedAtUtc, RevokeReason)
             IX(UserId, RevokedAtUtc)      -- ใช้บังคับ concurrent login limit
RefreshTokens(Id PK, UserId FK, SessionId FK, TokenHash UQ, ExpiresAtUtc,
              RevokedAtUtc, ReplacedByTokenId)
SecurityAudits(Id, UserId, EventType, Detail(json), IpAddress, OccurredAtUtc)
UserSecurityTokens(Id PK, UserId FK, TokenHash UQ, Purpose, ExpiresAtUtc, ConsumedAtUtc)
                   -- generic one-time token: Purpose = EmailConfirmation (P0-15, ใช้จริง, หมดอายุ 24 ชม.) |
                   -- PasswordReset (P0-21, ใช้จริงแล้ว — /forgot-password ออก token, /reset-password
                   -- ใช้ token, หมดอายุ 1 ชม. คำขอใหม่ invalidate token เก่าที่ยังไม่หมดอายุของ user เดียวกัน
                   -- เสมอ, reset สำเร็จ revoke session/refresh token ทั้งหมดของ user) — เหมือน RefreshTokens
                   -- เก็บแค่ TokenHash ไม่เก็บ raw token, hash ด้วย SHA-256 (ไม่ใช่ PBKDF2 เพราะ raw
                   -- token สุ่มจาก RandomNumberGenerator อยู่แล้ว ไม่ใช่รหัสผ่านที่มนุษย์เดาได้)
                   IX(UserId, Purpose) WHERE ConsumedAtUtc IS NULL   -- filtered, หาโทเคนที่ยังใช้ได้ของผู้ใช้+purpose
```

## catalog
```
Categories(Id PK, ParentId FK->self, Slug UQ, NameTh, NameEn, IconKey, SortOrder, IsActive)
InstructorProfiles(Id PK, UserId FK UQ, DisplayName, Headline, Bio, AvatarUrl,
                   RevenueSharePercent decimal(5,2) default 70.00, Status, ApprovedAtUtc)
Courses(Id PK, Slug UQ, Title, Subtitle, Description, InstructorId FK, CategoryId FK,
        Level, Language, ThumbnailUrl, TrailerMediaAssetId FK,
        Price decimal(18,2), ComparePrice, Currency,
        AccessDurationDays int NULL,          -- NULL = ตลอดชีพ
        Status,                                -- Draft|InReview|Published|Archived|Rejected
        PublishedAtUtc, TotalDurationSeconds, EpisodeCount,
        RatingAverage decimal(3,2), RatingCount, EnrollmentCount,   -- denormalized
        SeoTitle, SeoDescription, RowVersion, IsDeleted, ...audit)
        IX(Status, PublishedAtUtc) / IX(CategoryId, Status) / FULLTEXT(Title,Subtitle,Description)
CourseSections(Id PK, CourseId FK, Title, SortOrder)
CourseEpisodes(Id PK, CourseId FK, SectionId FK, Title, Description, SortOrder,
               MediaAssetId FK NULL, DurationSeconds, IsFreePreview bit, Status)
               UQ(SectionId, SortOrder)
EpisodeAttachments(Id PK, EpisodeId FK, FileName, StorageKey, ContentType, SizeBytes)
CourseOutcomes(Id, CourseId FK, Text, SortOrder)        -- "สิ่งที่จะได้เรียนรู้"
CourseRequirements(Id, CourseId FK, Text, SortOrder)
CourseTags(CourseId, TagId) / Tags(Id, Slug UQ, Name)
CourseReviews(Id PK, CourseId FK, UserId FK, Rating tinyint, Comment, Status,
              CreatedAtUtc) UQ(CourseId, UserId)
```

## media — สร้างจริงแล้วเป็น schema `MEDIA` UPPERCASE (D-17, ดูหมายเหตุด้านบน)
```
MediaAssets(Id PK, Provider, ProviderAssetId, PlaybackId, Status,
            DurationSeconds, OriginalFileName, SizeBytes, DrmEnabled bit,
            ThumbnailUrl, UploadedByUserId, CreatedAtUtc, ReadyAtUtc, ErrorMessage)
            IX(Provider, ProviderAssetId) UQ
MediaUploadSessions(Id PK, MediaAssetId FK, UploadUrl, ExpiresAtUtc, Status)
PlaybackSessions(Id PK, UserId, EpisodeId, SessionId, IssuedAtUtc, ExpiresAtUtc,
                 IpAddress, DeviceId)       -- ใช้สืบสวนกรณีรั่วไหล
```

## learning — สร้างจริงแล้วเป็น schema `LEARNING` UPPERCASE (D-17, ดูหมายเหตุด้านบน)
```
Enrollments(Id PK, UserId FK, CourseId FK, OrderId FK NULL, Source,   -- Purchase|Gift|Admin|Corporate (P9)
            EnrolledAtUtc, ExpiresAtUtc NULL, Status,
            ProgressPercent decimal(5,2), CompletedAtUtc, LastAccessedAtUtc, RowVersion)
            UQ(UserId, CourseId)  IX(CourseId, Status)
EpisodeProgress(Id PK, EnrollmentId FK, EpisodeId FK, LastPositionSeconds,
                WatchedSeconds, IsCompleted bit, CompletedAtUtc, UpdatedAtUtc)
                UQ(EnrollmentId, EpisodeId)
WatchEvents(Id bigint identity PK, EnrollmentId, EpisodeId, EventType,   -- play|pause|seek|ended|heartbeat
            PositionSeconds, OccurredAtUtc)
            -- volume สูง: partition ตามเดือน + purge > 12 เดือน หลัง rollup แล้ว
Quizzes(Id PK, EpisodeId FK, Title, PassingScorePercent, MaxAttempts, IsActive)
QuizQuestions(Id PK, QuizId FK, Type, Text, Explanation, Points, SortOrder)
QuizOptions(Id PK, QuestionId FK, Text, IsCorrect bit, SortOrder)
QuizAttempts(Id PK, QuizId FK, EnrollmentId FK, AttemptNo, ScorePercent,
             IsPassed bit, StartedAtUtc, SubmittedAtUtc)
QuizAttemptAnswers(Id PK, AttemptId FK, QuestionId FK, SelectedOptionIds(json), IsCorrect)
Assignments(Id PK, EpisodeId FK, Title, Instructions, DueDays, MaxFileSizeMb, AllowedExtensions)
AssignmentSubmissions(Id PK, AssignmentId FK, EnrollmentId FK, StorageKey, Note,
                      SubmittedAtUtc, Status, Score, Feedback, GradedByUserId, GradedAtUtc)
Certificates(Id PK, EnrollmentId FK UQ, SerialNo UQ, VerifyCode UQ,
             IssuedAtUtc, PdfStorageKey, RevokedAtUtc)
```

## commerce — สร้างจริงแล้วเป็น schema `COMMERCE` UPPERCASE (D-17, ดูหมายเหตุด้านบน) — `Refunds` เพิ่ม `DecidedByUserId`/`DecidedAtUtc`/`DecisionNote`/`StripeRefundId` เกินสเก็ตช์เดิม (approve/reject workflow) และเพิ่มตารางใหม่ `TaxInvoices` (ไม่มีในสเก็ตช์เดิม) — ทั้งสองจุดเป็น gap-fill ตาม D-17
```
Carts(Id PK, UserId FK UQ, UpdatedAtUtc) / CartItems(Id, CartId FK, ItemType, RefId, AddedAtUtc)
Orders(Id PK, OrderNo UQ, UserId FK, SubtotalAmount, DiscountAmount, TaxAmount,
       TotalAmount, Currency, Status,          -- Pending|AwaitingPayment|Paid|Failed|Cancelled|Refunded
       PromoCodeId FK NULL, CreatedAtUtc, PaidAtUtc, RowVersion)
OrderItems(Id PK, OrderId FK, ItemType, CourseId FK NULL, BundleId FK NULL,
           TitleSnapshot, UnitPrice, DiscountAmount, LineTotal,
           InstructorId, RevenueSharePercentSnapshot)   -- snapshot กันเรตเปลี่ยนย้อนหลัง
-- ⚠️ v1 ใช้ Stripe — PromptPay QR ผ่าน PaymentIntent + webhook (แก้ไข 2026-08-18 จาก EasySlip) — สเปคเต็มอยู่ใน PAYMENT.md
Payments(Id PK, OrderId FK, Method,               -- PromptPay | (อนาคต) Card
         Provider,                                -- 'Stripe'
         ProviderPaymentIntentId UQ,              -- ← ผูก 1:1 กับ Stripe PaymentIntent
         Amount, Status,                          -- Pending|Processing|Succeeded|Failed|Expired|Refunded
         SucceededAtUtc, FailureReason, CreatedAtUtc)
StripeWebhookEvents(Id PK, StripeEventId UQ,      -- ← กัน replay/ยิงซ้ำ ต้องอยู่ที่ระดับ DB เท่านั้น
                    EventType, PayloadJson,       -- raw JSON ไว้ dispute/ตรวจย้อนหลัง
                    ReceivedAtUtc, ProcessedAtUtc, ProcessResult)
PaymentOpsQueue(Id PK, PaymentId FK, Reason, Status,   -- จ่ายซ้ำ/จ่ายหลังหมดอายุ/refund ค้าง
                AssignedToUserId, ResolvedByUserId, ResolvedAtUtc, Note)
Refunds(Id PK, PaymentId FK, Amount, Reason, Status, RequestedByUserId, CreatedAtUtc, CompletedAtUtc)
PromoCodes(Id PK, Code UQ, DiscountType, DiscountValue, MaxRedemptions, RedeemedCount,
           MaxPerUser, MinOrderAmount, StartsAtUtc, EndsAtUtc, Scope, ScopeRefId, IsActive, RowVersion)
PromoRedemptions(Id PK, PromoCodeId FK, OrderId FK, UserId FK, RedeemedAtUtc) UQ(PromoCodeId, OrderId)
Bundles(Id PK, Slug UQ, Title, Description, Price, IsActive, StartsAtUtc, EndsAtUtc)
BundleItems(BundleId, CourseId) PK(BundleId, CourseId)
FlashSales(Id PK, Title, StartsAtUtc, EndsAtUtc, IsActive)
FlashSaleItems(Id PK, FlashSaleId FK, CourseId FK, SalePrice) UQ(FlashSaleId, CourseId)
```

## payout — สร้างจริงแล้วเป็น schema `PAYOUT` UPPERCASE (D-17, ดูหมายเหตุด้านบน)
```
RevenueSplits(Id PK, OrderItemId FK UQ, InstructorId FK, GrossAmount, PaymentFeeAmount,
              PlatformFeeAmount, InstructorAmount, PeriodKey char(7),   -- 'YYYY-MM'
              Status, CreatedAtUtc)          -- Pending|Payable|Paid|Reversed
              IX(InstructorId, PeriodKey, Status)
InstructorPayoutAccounts(Id PK, InstructorId FK, BankCode, AccountNoEncrypted,
                         AccountName, TaxId, VerifiedAtUtc)
PayoutBatches(Id PK, PeriodKey, TotalAmount, Status, CreatedAtUtc, ExecutedAtUtc, ExecutedBy)
PayoutBatchItems(Id PK, BatchId FK, InstructorId FK, Amount, WithholdingTaxAmount,
                 NetAmount, Status, TransferRef)
```

## cms / community / notify / analytics
`cms`/`community`/`analytics` สร้างจริงแล้วเป็น schema UPPERCASE (`CMS`/`COMMUNITY`/`ANALYTICS`, D-17, ดูหมายเหตุด้านบน) — `notify` ยังไม่แตะ (`Announcements`/`Notifications` ยังไม่สร้าง, มีแค่ `EmailOutbox` ที่สร้างมาตั้งแต่ P0-19 และยังเป็น PascalCase ปกติเหมือนเดิมเพราะ Notification ไม่ใช่ 1 ใน 7 โมดูลใหม่)
```
cms.Banners(Id, Placement, ImageUrl, MobileImageUrl, LinkUrl, Title, SortOrder,
            StartsAtUtc, EndsAtUtc, IsActive)
cms.MenuItems(Id, ParentId, Label, Url, SortOrder, IsActive)
cms.Posts(Id, Slug UQ, Title, Excerpt, ContentHtml, CoverImageUrl, AuthorUserId,
          Status, PublishedAtUtc, SeoTitle, SeoDescription, IsDeleted)
cms.Redirects(Id, FromPath UQ, ToPath, StatusCode)

community.Discussions(Id, CourseId, EpisodeId NULL, UserId, ParentId NULL, Body,
                      IsInstructorAnswer bit, Status, UpvoteCount, CreatedAtUtc, IsDeleted)
                      IX(EpisodeId, Status, CreatedAtUtc)
community.Reports(Id, DiscussionId, ReportedByUserId, Reason, Status, ResolvedAtUtc)

notify.Announcements(Id, CourseId, InstructorId, Title, Body, SendEmail bit,
                     ScheduledAtUtc, SentAtUtc, RecipientCount)
notify.Notifications(Id, UserId, Type, Title, Body, LinkUrl, ReadAtUtc, CreatedAtUtc,
                     PublishedAtUtc timestamptz(3) NULL)             -- D-23 (migration AddNotificationKafkaDelivery, ยังไม่ apply): outbox ของ event "มีแจ้งเตือนใหม่" · NULL = relay ยังไม่ประกาศ (migration backfill แถวเดิมเป็น `PUBLISHED_AT_UTC = CREATED_AT_UTC`) ·
                                                                     --   partial index IX_NOTIFICATIONS_UNPUBLISHED (ID) WHERE "PUBLISHED_AT_UTC" IS NULL
notify.EmailOutbox(Id, ToEmail, Subject, BodyHtml, TemplateKey, Status,
                   Attempts, NextRetryAtUtc, SentAtUtc, LastError,
                   CalendarIcs text NULL, CalendarMethod varchar(10) NULL,   -- P11-04 (migration AddEmailOutboxCalendarPart, ยังไม่ apply) ดูส่วน "ส่วนขยาย P11–P12"
                   QueuedAtUtc timestamptz(3) NULL)                          -- D-23 (AddNotificationKafkaDelivery): relay ส่งเข้า Kafka เมื่อไหร่ · Status ใหม่ 'Queued' (เก็บเป็น string เหมือนเดิม ไม่ต้อง migrate ค่า)
                                                                             --   Pending → Queued → Sent | Failed(retry → Queued อีกรอบ) · Failed + NextRetryAtUtc NULL = หมดสิทธิ์ (dead letter)

analytics.DailyCourseStats(Date, CourseId, Views, Enrollments, Revenue, CompletionRate) PK(Date,CourseId)
analytics.EpisodeDropOff(Date, EpisodeId, StartCount, CompleteCount, AvgWatchPercent) PK(Date,EpisodeId)
-- ทั้งสองตารางสร้างจาก WatchEvents ด้วย Hangfire job รายคืน (ไม่ query realtime)

dbo.OutboxMessages(Id, Type, Payload, OccurredAtUtc, ProcessedAtUtc, Attempts, Error)
```

## ส่วนขยาย P8–P10 (สเปค SiriLearn — D-16, 2026-08-19 · sketch เบื้องต้น รายละเอียดสรุปตอนเริ่ม phase)
```
-- P8 Growth & Engagement
identity.UserExternalLogins(Id PK, UserId FK, Provider,        -- Google|Facebook|Apple
                            ProviderUserId, EmailAtLink, LinkedAtUtc)
                            UQ(Provider, ProviderUserId)
learning.LearnerNotes(Id PK, EnrollmentId FK, EpisodeId FK, PositionSeconds,
                      Body nvarchar(2000), CreatedAtUtc, UpdatedAtUtc)
                      IX(EnrollmentId, EpisodeId)
catalog.LearningPaths(Id PK, Slug UQ, Title, Description, IsActive, SortOrder)
catalog.LearningPathItems(PathId FK, CourseId FK, SortOrder) PK(PathId, CourseId)
learning.XpEvents(Id bigint identity PK, UserId, SourceType,   -- EpisodeCompleted|QuizPassed|CourseCompleted
                  SourceRefId, Points, OccurredAtUtc)
                  UQ(UserId, SourceType, SourceRefId)          -- กันได้แต้มซ้ำที่ระดับ DB
learning.LeaderboardSnapshots(PeriodKey char(7), UserId, TotalXp, Rank)
                  PK(PeriodKey, UserId)                        -- rollup รายคืน ห้าม query สดจาก XpEvents
                  -- รายเดือน = 'YYYY-MM' · ตลอดกาล = sentinel 'ALL' (char(7) พอดี) rollup job เดียวกันเขียนทั้งสอง key

-- P9 B2B Corporate (module ใหม่ Siri.Modules.Corporate — รอ Q7 ก่อนสรุปส่วน license)
-- ห้าม hard delete/cascade บน licenses/assignments — เป็นข้อมูลสิทธิ์เรียน ใช้ Status แทน (กฎเหล็ก #5)
-- OrgRole เป็น per-org บน OrgMembers เท่านั้น ไม่ใช่ global role ใน identity.Roles — สิทธิ์ HR มาจาก membership ของ org นั้น
corporate.Organizations(Id PK, Name, TaxIdEncrypted NULL, Status, CreatedAtUtc)
corporate.OrgMembers(Id PK, OrgId FK, UserId FK, OrgRole,      -- HRAdmin|CorporateLearner
                     Department NULL, Status, InvitedAtUtc, JoinedAtUtc)
                     UQ(OrgId, UserId)
corporate.OrgCourseLicenses(Id PK, OrgId FK, CourseId FK, SeatCount,
                            Status,                            -- Active|Suspended|Revoked|Expired
                            ActivatedByUserId, ActivatedAtUtc, ExpiresAtUtc NULL)
                            -- นับที่นั่งที่ใช้ไปจาก COUNT(OrgCourseAssignments) แบบ atomic ที่ระดับ SQL
                            -- (UPDATE...WHERE เงื่อนไขนับ) ห้ามอ่านมาเช็คใน memory แล้วค่อยเขียน (database.md)
corporate.OrgCourseAssignments(Id PK, OrgId FK, CourseId FK, UserId FK,
                               IsMandatory bit, DueAtUtc NULL, EnrollmentId FK NULL,
                               AssignedByUserId, AssignedAtUtc)
                               UQ(OrgId, CourseId, UserId)

-- P10 Subscription (รอ Q6 ก่อนสรุปส่วน split — ห้าม hard delete/cascade เหมือนข้อมูลเงินทั้งหมด)
commerce.SubscriptionPlans(Id PK, Code UQ, Name, Price, Interval,   -- Monthly|Yearly
                           IsActive)
commerce.Subscriptions(Id PK, UserId FK, PlanId FK, ProviderSubscriptionId UQ,  -- Stripe subscription id
                       Status,                                  -- Active|PastDue|Cancelled|Expired
                       CurrentPeriodStartsAtUtc, CurrentPeriodEndsAtUtc, CancelledAtUtc NULL, RowVersion)
                       IX(UserId, Status)
-- entitlement ตอน playback = enrollment active หรือ subscription active (แก้ที่ P2-04 ใน task P10-04)
-- คอร์สร่วม subscription = flag บน Courses (เพิ่มตอน P10-02 — instructor opt-in ตาม Q6)
```

---

## ส่วนขยาย P11–P12 (Hybrid Live + AI Study — D-21, 2026-09-16 · P11-01/P11-11 (Catalog) **สร้างจริงแล้ว** · P11-03/04/05 (schema `LIVE` + Notification + Catalog) **สร้างจริงแล้ว 2026-10-07 แต่ migration ยังไม่ apply** · P12 ยังเป็น sketch ไม่สร้าง)

> แบบเต็ม `docs/HYBRID_LIVE.md` §1 · Catalog = PascalCase property/ตาราง UPPERCASE ตาม convention เดิมของโมดูล · `LIVE`/`MEDIA`/`LEARNING` = UPPERCASE ตาม D-17 · ห้าม hard delete/cascade บน invite/join log (เป็นสิทธิ์เรียน+forensics)
>
> **P11-01 เสร็จแล้ว (2026-09-16, Claude Code):** `CATALOG.COURSES.DELIVERY_FORMAT` + `CATALOG.COURSE_LIVE_SESSIONS` สร้างจริงตาม sketch ด้านล่างเป๊ะ ผ่าน migration `AddCourseDeliveryFormatAndLiveSessions` (apply ขึ้น DB จริงแล้ว 2026-09-16) — เพิ่ม index ที่ sketch เดิมไม่ได้เขียนไว้ 1 ตัว: `IX(RecordingEpisodeId)` (EF auto-generate ให้ FK ที่ไม่มี index ปิดทับอยู่แล้ว)
>
> **✅ Q10/Q13 ตอบแล้ว 2026-09-16 — แก้ sketch ด้านล่างให้ตรง**: Q10=B (ผู้สอนเชื่อม Google เอง) → ตัด `HOST_ACCOUNT`/แนวคิด service-account กลางออก เพิ่ม `LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS` ใหม่ · Q13.1/13.2 → เพิ่ม `EnrollmentDeadlineUtc`/`MaxSeats`/`SeatsUsed` บน `COURSES` (migration ใหม่ **ต่อจาก** P11-01 ไม่ใช่แก้ของเดิม เพราะ P11-01 apply ขึ้น DB จริงแล้ว — ห้ามแก้ migration ที่ apply แล้วตาม `database.md`) · **ไม่รวม** `GOOGLE_ATTENDEE_SYNC_ENABLED` ในบรรทัดด้านล่าง — คอลัมน์นั้นยังเป็นของ P11-04 ตรง ๆ (ไม่ติด Q แล้ว แค่ยังไม่ถึงคิวสร้าง) · **`AI_ENRICHMENT_ENABLED` ตัดออกจากแผนไปเลยในรอบนี้** (P12 เลื่อนทั้งเฟสตาม Q12 — ไม่สร้าง schema ล่วงหน้าให้ฟีเจอร์ที่ยังไม่มีคำสั่งเปิด)
```
-- P11 Catalog (แก้ตารางเดิม + ตารางใหม่ 1 ตัว) — migration AddCourseDeliveryFormatAndLiveSessions — สร้างจริงแล้ว, apply แล้ว
CATALOG.COURSES            + DELIVERY_FORMAT varchar(20) NOT NULL DEFAULT 'OnDemand'   -- OnDemand|Live|Hybrid (string enum)
CATALOG.COURSE_LIVE_SESSIONS(Id PK uuidv7, CourseId FK→COURSES Cascade, Title(200), Description text NULL,
                           StartsAtUtc timestamptz(3), EndsAtUtc timestamptz(3), SortOrder,
                           Status varchar(20),              -- Scheduled|Cancelled (Upcoming/Live/Ended คำนวณจาก IClock ไม่เก็บ)
                           CancelReason(500) NULL,
                           RecordingEpisodeId FK→COURSE_EPISODES NoAction NULL,   -- catch-up = ไม่ null + episode Ready
                           RowVersion bytea (ConcurrencyTokenInterceptor), audit)
                           IX(CourseId, StartsAtUtc) · IX(StartsAtUtc) WHERE Status='Scheduled' (job หา upcoming) · IX(RecordingEpisodeId) (FK)
                           -- ไม่ทับกันในคอร์สเดียว/ระยะ 15 นาที–8 ชม. บังคับที่ aggregate (unit test) ไม่ใช่ constraint DB

-- P11-11 Catalog เสร็จแล้ว (2026-09-16, Claude Code, docs/contracts/P11-11-enrollment-deadline-seat-cap.md)
-- migration AddCourseEnrollmentDeadlineAndSeatCap (ชื่อต่างจาก sketch เดิม "AddCourseEnrollmentPolicy" —
-- contract ที่ FROZEN ตั้งชื่อไว้ตรง ๆ ยึดตามนั้น) — สร้างจริงตาม sketch ด้านล่างเป๊ะ, additive ล้วน 3
-- AddColumn ไม่มี shadow property/FK ใหม่ — ยังไม่ apply ขึ้น DB จริง (รอคำสั่ง "migration database")
CATALOG.COURSES            + ENROLLMENT_DEADLINE_UTC timestamptz(3) NULL   -- null = ไม่ปิดรับสมัคร
                           + MAX_SEATS int NULL, SEATS_USED int NOT NULL DEFAULT 0   -- null MaxSeats = ไม่จำกัด
                           -- SEATS_USED เพิ่ม/ลดแบบ atomic ผ่าน ExecuteUpdateAsync จาก Commerce เท่านั้น
                           -- (ICatalogPriceContract.TryReserveSeatAsync/ReleaseSeatAsync, pattern เดียวกับ
                           -- PROMO_CODE.REDEEMED_COUNT) — เขียนตอนสร้าง/ยกเลิก/หมดอายุ order เท่านั้น ไม่ลดเมื่อ
                           -- refund หลังจ่ายเงินแล้ว (ตัดสินใจแล้ว ดูรายละเอียดใน contract §4.5)

-- ✅ P11-03 / P11-04 / P11-05 WP-A สร้างจริงแล้ว (2026-10-07, DATABASE agent) — 5 migration ตามลำดับนี้ **ยังไม่ apply ขึ้น DB จริง**
--    1 AddLiveMeetings → 2 AddEmailOutboxCalendarPart → 3 AddLiveSessionInvites → 4 AddCourseGoogleAttendeeSync → 5 AddLiveSessionJoinLogs
--    (แทนที่ sketch เดิมของ LIVE.* ที่เคยอยู่ตรงนี้ทั้งก้อน — ที่มา: docs/contracts/P11-03 §2, P11-04 §2, P11-05 §2 ซึ่ง FROZEN)
-- ทุกตาราง LIVE: entity UPPERCASE (property ของ IAuditable เป็น PascalCase), PK uuid UUIDv7, timestamptz(3) UTC, enum เก็บเป็น string,
--    ROW_VERSION bytea (ConcurrencyTokenInterceptor), **ไม่มี FK ข้าม schema/โมดูล** (SESSION_ID/USER_ID/COURSE_ID ชี้ไป CATALOG/IDENTITY โดยไม่มี constraint),
--    **ไม่มี cascade delete ทุกเส้น**, ไม่ hard delete invite/join log (เป็นหลักฐานว่าใครถูกเชิญ/เข้าห้อง)

-- migration 1: AddLiveMeetings (P11-03)
LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS(INSTRUCTOR_GOOGLE_ACCOUNT_ID PK,
                      INSTRUCTOR_USER_ID uuid UQ,         -- key ด้วย user id (ไม่ใช่ InstructorProfileId ตาม sketch เดิม) — 1 ผู้สอน = 1 บัญชี Google
                      GOOGLE_SUBJECT(64), GOOGLE_EMAIL(320),
                      REFRESH_TOKEN_ENCRYPTED text NULL,  -- ISensitiveDataProtector.Encrypt เท่านั้น · null หลัง revoke/disconnect · ห้าม plaintext
                      SCOPES(500), CONNECTED_AT_UTC, LAST_VALIDATED_AT_UTC NULL, REVOKED_AT_UTC NULL,
                      REVOKED_REASON(40) NULL,            -- invalid_grant | user_disconnected | scope_missing | insufficient_scope
                      ROW_VERSION, audit)
                      PK_INSTRUCTOR_GOOGLE_ACCOUNTS · UQ IX_INSTR_GOOGLE_ACCT_USER_ID
LIVE.SESSION_MEETINGS(SESSION_MEETING_ID PK,
                      SESSION_ID UQ,                      -- CATALOG.COURSE_LIVE_SESSIONS.Id (ไม่มี FK — แถวถูก stage ก่อนที่ session จะอยู่ใน DB)
                      INSTRUCTOR_USER_ID NULL,            -- denormalize ตอน job ประมวลผลครั้งแรก (ใช้ reset ตอนผู้สอน reconnect)
                      PROVIDER varchar(20) NULL,          -- GoogleMeet | Manual | Logging(dev เท่านั้น) · null = ยังไม่ตัดสิน
                      INSTRUCTOR_GOOGLE_ACCOUNT_ID FK→INSTRUCTOR_GOOGLE_ACCOUNTS NoAction NULL,   -- FK_SESSION_MEETINGS_GOOGLE_ACCT
                      PROVIDER_EVENT_ID(200) NULL,
                      MEET_URL_ENCRYPTED text NULL,       -- ลิงก์ห้อง = capability URL → เข้ารหัส (sketch เดิมเป็น MEET_URL(500) plaintext)
                      SYNC_STATUS varchar(20),            -- Pending | AwaitingLink | Synced | NeedsReconnect | Failed | PendingDelete | Deleted
                      ICS_SEQUENCE int NOT NULL DEFAULT 0, -- (sketch เดิมชื่อ SEQUENCE) เพิ่มเมื่อเวลา/ชื่อคาบเปลี่ยนหรือยกเลิก
                      ATTEMPTS int NOT NULL DEFAULT 0, NEXT_RETRY_AT_UTC NULL, LAST_SYNC_AT_UTC NULL,
                      ERROR(500) NULL,                    -- error code สั้น ๆ เท่านั้น — ห้ามมี token/URL/อีเมล (sketch เดิม 2000)
                      MEETING_ALERT_SENT_AT_UTC NULL,     -- กัน alert ผู้สอนซ้ำ ("วางลิงก์" ของ P11-03 — ตั้งทันทีที่เป็น AwaitingLink)
                      READINESS_ALERT_SENT_AT_UTC NULL,   -- P11-04 (migration AddSessionMeetingReadinessAlert) กัน alert "ห้องยังไม่พร้อม" ที่ T−24 ชม. ซ้ำ —
                                                          --   แยกจากข้างบนเพราะอันนั้นถูกใช้ไปแล้วหลายวันก่อน ไม่งั้น alert ใกล้วันสอนจะไม่มีวันส่ง
                      ATTENDEE_SYNC_ALERT_SENT_AT_UTC NULL, -- P11-04 WP-F (migration AddSessionMeetingAttendeeSyncAlert) กัน alert "เกินเพดาน attendee Google (Live:GoogleAttendeeCap)" ซ้ำ ·
                                                          --   เคลียร์เมื่อจำนวนกลับมาต่ำกว่าเพดาน
                      ROW_VERSION, audit)
                      PK_SESSION_MEETINGS · UQ IX_SESSION_MEETINGS_SESSION_ID ·
                      IX_SESSION_MEETINGS_SYNC_DUE (SYNC_STATUS, NEXT_RETRY_AT_UTC) WHERE "SYNC_STATUS" IN ('Pending','PendingDelete') ·
                      IX_SESSION_MEETINGS_INSTR_USER_ID (INSTRUCTOR_USER_ID) ·
                      IX_SESSION_MEETINGS_GOOGLE_ACCT_ID (INSTRUCTOR_GOOGLE_ACCOUNT_ID)   -- index ของ FK (EF สร้างให้เองอยู่แล้ว ตั้งชื่อให้ชัด — ไม่อยู่ในรายการ index ของ contract)
                      -- IsUsable (ไม่ใช่คอลัมน์) = MEET_URL_ENCRYPTED IS NOT NULL AND SYNC_STATUS <> 'Deleted' — ใช้ทั้ง publish gate และ join gate

-- migration 2: AddEmailOutboxCalendarPart (P11-04) — Notification, additive 2 AddColumn
NOTIFY.EMAIL_OUTBOX        + CALENDAR_ICS text NULL, CALENDAR_METHOD varchar(10) NULL   -- REQUEST | CANCEL | PUBLISH → MimeKit text/calendar · มาคู่กันเสมอ (domain บังคับ)

-- migration 3: AddLiveSessionInvites (P11-04)
LIVE.SESSION_INVITES(SESSION_INVITE_ID PK, SESSION_ID, USER_ID,
                      ROLE varchar(16),                   -- Learner | Instructor (enum LiveParticipantRole — ใช้ร่วมกับ SESSION_JOIN_LOGS) · sketch เดิมไม่มี
                      STATUS varchar(16),                 -- Pending | Invited | Cancelled | Skipped
                      ICS_SEQUENCE_SENT int NULL,         -- SEQUENCE ล่าสุดที่ส่ง (รวม CANCEL) — monotonic ต่อ invite
                      INVITE_SENT_AT_UTC NULL, CANCEL_SENT_AT_UTC NULL,
                      REMINDER_24H_SENT_AT_UTC NULL, REMINDER_1H_SENT_AT_UTC NULL,
                      GOOGLE_ATTENDEE_SYNCED_AT_UTC NULL,
                      ERROR(300) NULL,                    -- code สั้น เช่น no_contact — ห้ามมีอีเมล
                      ROW_VERSION, audit)                 -- ตัด ENROLLMENT_ID ออกจาก sketch เดิม (Live ไม่เห็น enrollment id ผ่าน contract)
                      PK_SESSION_INVITES · UQ IX_SESSION_INVITES_SESSION_USER (SESSION_ID, USER_ID) ·
                      IX_SESSION_INVITES_USER_ID (USER_ID) · IX_SESSION_INVITES_PENDING (SESSION_ID) WHERE "STATUS" = 'Pending'
                      -- ชื่อคอลัมน์ REMINDER_24H/1H: ApplyUppercaseNamingConventions แยก "ตัวเลข+ตัวพิมพ์ใหญ่" เป็น 24_H ได้ถ้าตั้งชื่ออัปเปอร์เคสตรง ๆ —
                      -- SessionInviteConfiguration จึงตั้ง HasColumnName แบบตัวพิมพ์เล็กโดยตั้งใจ (convention ทำเป็นตัวใหญ่ให้ทีหลัง) · LiveSchemaTests ล็อกชื่อไว้

-- migration 4: AddCourseGoogleAttendeeSync (P11-04) — Catalog, additive 1 AddColumn
CATALOG.COURSES            + GOOGLE_ATTENDEE_SYNC_ENABLED bool NOT NULL DEFAULT false   -- opt-in ต่อคอร์ส (Q11) — ส่งอีเมลผู้เรียนไป Google จึงต้องเป็นการตัดสินใจของผู้สอน

-- migration 5: AddLiveSessionJoinLogs (P11-05)
LIVE.SESSION_JOIN_LOGS(SESSION_JOIN_LOG_ID PK, SESSION_ID, COURSE_ID,   -- COURSE_ID denormalize ให้ refund (P11-12) เช็ค (USER_ID, COURSE_ID) ได้โดยไม่ join ข้ามโมดูล
                      USER_ID,                            -- จาก IUserContext เท่านั้น
                      ROLE varchar(16),                   -- Learner | Instructor — refund/KPI นับเฉพาะ Learner
                      AUTH_SESSION_ID uuid NULL,          -- claim sid ของ JWT (sketch เดิมเป็น varchar(200))
                      JOINED_AT_UTC,                      -- เวลาที่เปิดเผยลิงก์
                      IP_ADDRESS(64) NULL, USER_AGENT(300) NULL)
                      -- append-only เหมือน MEDIA.PLAYBACK_SESSIONS: ไม่ implement IAuditable/ISoftDelete, ไม่มี ROW_VERSION, entity ไม่มี method แก้ — มีแต่ SESSION_JOIN_LOG.Record(...)
                      PK_SESSION_JOIN_LOGS · IX_SESSION_JOIN_LOGS_SESSION_USER (SESSION_ID, USER_ID) · IX_SESSION_JOIN_LOGS_USER_COURSE (USER_ID, COURSE_ID)
                      -- ต่างจาก sketch เดิม: IX(SESSION_ID, JOINED_AT_UTC)/IX(USER_ID, JOINED_AT_UTC) → ตาม contract P11-05 §2
                      -- retention ของ IP/UA (scrub หลัง 12 เดือน) เป็น follow-up · USER_ID/COURSE_ID/SESSION_ID ต้องเก็บไว้ตามกฎ refund Q13.4

-- P11 Commerce: ไม่มีตารางใหม่ (PAYMENT.METHOD รองรับ Card แล้ว) · Config Payment:EnabledMethods

-- P12 (AI Study) — เลื่อนทั้งเฟส (Q12, 2026-09-16) — sketch ด้านล่างยังไม่สร้าง ไม่ผูกกับ P11 เลย เก็บไว้ revisit ได้ทันที
-- P12 Media (schema MEDIA) — migration AddMediaAiEnrichments
MEDIA.MEDIA_AI_ENRICHMENTS(MEDIA_AI_ENRICHMENT_ID PK, MEDIA_ASSET_ID FK→MEDIA_ASSETS NoAction UQ,
                      STATUS varchar(20),              -- Pending|Transcribing|Summarizing|Ready|Failed|Skipped
                      TRANSCRIPT_LANG(10), TRANSCRIPT_TEXT text NULL, CHAPTERS_JSON jsonb NULL, MOMENTS_JSON jsonb NULL,
                      SUMMARY_JSON jsonb NULL,         -- {summaryTh, keyPoints[], reviewQuestions[], suggestedTitle}
                      MODEL(100) NULL, ATTEMPTS int, ERROR(2000) NULL,
                      TRANSCRIBED_AT_UTC NULL, SUMMARIZED_AT_UTC NULL, audit)
MEDIA.AI_GENERATION_LOG(AI_GENERATION_LOG_ID bigint identity PK, PURPOSE varchar(40),   -- Summary|WatchPlan|MarketingCopy
                      MODEL(100), INPUT_TOKENS int, OUTPUT_TOKENS int, COST_USD decimal(10,4),
                      REF_TYPE(40), REF_ID uuid NULL, REQUESTED_BY_USER_ID uuid NULL, OCCURRED_AT_UTC)
                      IX(OCCURRED_AT_UTC)              -- budget guard: SUM(COST_USD) เดือนนี้ ก่อนเรียกทุกครั้ง

-- P12 Learning — migration AddStudyPlans
LEARNING.STUDY_PLANS(STUDY_PLAN_ID PK, ENROLLMENT_ID FK→ENROLLMENTS NoAction UQ, PLAN_JSON jsonb,
                      SOURCE varchar(20),              -- Llm|RuleBased
                      GENERATED_AT_UTC, VALID_UNTIL_UTC, PROGRESS_FINGERPRINT(64))   -- invalidate เมื่อ progress เปลี่ยน
```

## Index ที่ต้องมีตั้งแต่วันแรก (จาก query pattern จริง)
```sql
IX_Courses_Browse         (Status, CategoryId, PublishedAtUtc DESC) INCLUDE (Title, Slug, Price, RatingAverage, ThumbnailUrl)
IX_Enrollments_MyCourses  (UserId, Status) INCLUDE (CourseId, ProgressPercent, LastAccessedAtUtc)
IX_EpisodeProgress_Resume (EnrollmentId) INCLUDE (EpisodeId, LastPositionSeconds, IsCompleted)
IX_Orders_UserHistory     (UserId, CreatedAtUtc DESC)
IX_RevenueSplits_Payout   (InstructorId, PeriodKey, Status)
IX_UserSessions_Active    (UserId) WHERE RevokedAtUtc IS NULL      -- filtered index
IX_Discussions_Episode    (EpisodeId, Status, CreatedAtUtc DESC)
```

## Denormalization ที่ตั้งใจทำ (ต้อง sync ผ่าน domain event เท่านั้น)
`Courses.EpisodeCount`, `TotalDurationSeconds`, `RatingAverage`, `RatingCount`, `EnrollmentCount`
→ ห้าม UPDATE ตรงจาก handler อื่น ให้ผ่าน `CourseStatsUpdater` ที่เดียว + มี job reconcile รายคืน
