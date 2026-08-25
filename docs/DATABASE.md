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
notify.Notifications(Id, UserId, Type, Title, Body, LinkUrl, ReadAtUtc, CreatedAtUtc)
notify.EmailOutbox(Id, ToEmail, Subject, BodyHtml, TemplateKey, Status,
                   Attempts, NextRetryAtUtc, SentAtUtc, LastError)

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
