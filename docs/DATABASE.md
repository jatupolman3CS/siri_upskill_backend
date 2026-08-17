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

---

## identity
```
Users(Id PK, Email, NormalizedEmail UQ, PasswordHash, DisplayName, AvatarUrl, PhoneNumber,
      Status, EmailConfirmedAt, TwoFactorEnabled, LastLoginAtUtc, ...audit)
Roles(Id, Name UQ)                         -- Learner, Instructor, Admin, SuperAdmin
UserRoles(UserId, RoleId) PK(UserId,RoleId)
UserSessions(Id PK, UserId FK, DeviceId, DeviceName, UserAgent, IpAddress,
             CreatedAtUtc, LastSeenAtUtc, RevokedAtUtc, RevokeReason)
             IX(UserId, RevokedAtUtc)      -- ใช้บังคับ concurrent login limit
RefreshTokens(Id PK, UserId FK, SessionId FK, TokenHash UQ, ExpiresAtUtc,
              RevokedAtUtc, ReplacedByTokenId)
SecurityAudits(Id, UserId, EventType, Detail(json), IpAddress, OccurredAtUtc)
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

## media
```
MediaAssets(Id PK, Provider, ProviderAssetId, PlaybackId, Status,
            DurationSeconds, OriginalFileName, SizeBytes, DrmEnabled bit,
            ThumbnailUrl, UploadedByUserId, CreatedAtUtc, ReadyAtUtc, ErrorMessage)
            IX(Provider, ProviderAssetId) UQ
MediaUploadSessions(Id PK, MediaAssetId FK, UploadUrl, ExpiresAtUtc, Status)
PlaybackSessions(Id PK, UserId, EpisodeId, SessionId, IssuedAtUtc, ExpiresAtUtc,
                 IpAddress, DeviceId)       -- ใช้สืบสวนกรณีรั่วไหล
```

## learning
```
Enrollments(Id PK, UserId FK, CourseId FK, OrderId FK NULL, Source,   -- Purchase|Gift|Admin
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

## commerce
```
Carts(Id PK, UserId FK UQ, UpdatedAtUtc) / CartItems(Id, CartId FK, ItemType, RefId, AddedAtUtc)
Orders(Id PK, OrderNo UQ, UserId FK, SubtotalAmount, DiscountAmount, TaxAmount,
       TotalAmount, Currency, Status,          -- Pending|AwaitingPayment|Paid|Failed|Cancelled|Refunded
       PromoCodeId FK NULL, CreatedAtUtc, PaidAtUtc, RowVersion)
OrderItems(Id PK, OrderId FK, ItemType, CourseId FK NULL, BundleId FK NULL,
           TitleSnapshot, UnitPrice, DiscountAmount, LineTotal,
           InstructorId, RevenueSharePercentSnapshot)   -- snapshot กันเรตเปลี่ยนย้อนหลัง
-- ⚠️ v1 ใช้ PromptPay QR + ตรวจสลิปผ่าน EasySlip (ไม่มี gateway) — สเปคเต็มอยู่ใน PAYMENT.md
Payments(Id PK, OrderId FK, Method,               -- PromptPaySlip | (อนาคต) Card | Installment
         Amount, Status,                          -- Pending|UnderReview|Succeeded|Rejected|Expired
         VerifiedAtUtc, VerifiedBy, RejectReason, CreatedAtUtc, ExpiresAtUtc)
PaymentSlips(Id PK, PaymentId FK, OrderId FK, StorageKey, Provider, TransRef,
             SlipAmount, SlipDateUtc, SenderBank, SenderAccountMasked, SenderName,
             ReceiverBank, ReceiverAccountMasked, ReceiverName, RawResponse,
             VerifyResult, FailedRules, UploadedByUserId, UploadedAtUtc, ClientIp)
             UQ(Provider, TransRef)   -- ← กันใช้สลิปเดิมซ้ำ ต้องอยู่ที่ระดับ DB เท่านั้น
MerchantAccounts(Id PK, PromptPayId, AccountName, BankCode, AccountNoLast4, IsActive)
PaymentReviewQueue(Id PK, PaymentId FK, Reason, Status, AssignedToUserId,
                   ResolvedByUserId, ResolvedAtUtc, Note)
-- PaymentWebhookEvents: เตรียมไว้สำหรับตอนเปิด gateway จริง ยังไม่ใช้ใน v1
Refunds(Id PK, PaymentId FK, Amount, Reason, Status, RequestedByUserId, CreatedAtUtc, CompletedAtUtc)
PromoCodes(Id PK, Code UQ, DiscountType, DiscountValue, MaxRedemptions, RedeemedCount,
           MaxPerUser, MinOrderAmount, StartsAtUtc, EndsAtUtc, Scope, ScopeRefId, IsActive, RowVersion)
PromoRedemptions(Id PK, PromoCodeId FK, OrderId FK, UserId FK, RedeemedAtUtc) UQ(PromoCodeId, OrderId)
Bundles(Id PK, Slug UQ, Title, Description, Price, IsActive, StartsAtUtc, EndsAtUtc)
BundleItems(BundleId, CourseId) PK(BundleId, CourseId)
FlashSales(Id PK, Title, StartsAtUtc, EndsAtUtc, IsActive)
FlashSaleItems(Id PK, FlashSaleId FK, CourseId FK, SalePrice) UQ(FlashSaleId, CourseId)
```

## payout
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
