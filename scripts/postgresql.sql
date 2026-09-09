CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'NOTIFY') THEN
            CREATE SCHEMA "NOTIFY";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'LEARNING') THEN
            CREATE SCHEMA "LEARNING";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'CMS') THEN
            CREATE SCHEMA "CMS";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'COMMERCE') THEN
            CREATE SCHEMA "COMMERCE";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'CATALOG') THEN
            CREATE SCHEMA "CATALOG";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ANALYTICS') THEN
            CREATE SCHEMA "ANALYTICS";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'COMMUNITY') THEN
            CREATE SCHEMA "COMMUNITY";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'PAYOUT') THEN
            CREATE SCHEMA "PAYOUT";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'MEDIA') THEN
            CREATE SCHEMA "MEDIA";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'IDENTITY') THEN
            CREATE SCHEMA "IDENTITY";
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "NOTIFY"."ANNOUNCEMENTS" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "INSTRUCTOR_ID" uuid NOT NULL,
        "TITLE" character varying(300) NOT NULL,
        "BODY" text NOT NULL,
        "SEND_EMAIL" boolean NOT NULL,
        "SCHEDULED_AT_UTC" timestamp(3) with time zone,
        "SENT_AT_UTC" timestamp(3) with time zone,
        "RECIPIENT_COUNT" integer NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_ANNOUNCEMENTS" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."ASSIGNMENTS" (
        "ASSIGNMENT_ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "INSTRUCTIONS" character varying(4000) NOT NULL,
        "DUE_DAYS" integer,
        "MAX_FILE_SIZE_MB" integer NOT NULL,
        "ALLOWED_EXTENSIONS" character varying(500) NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_ASSIGNMENTS" PRIMARY KEY ("ASSIGNMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CMS"."BANNERS" (
        "BANNER_ID" uuid NOT NULL,
        "PLACEMENT" character varying(100) NOT NULL,
        "IMAGE_URL" character varying(1000) NOT NULL,
        "MOBILE_IMAGE_URL" character varying(1000),
        "LINK_URL" character varying(1000),
        "TITLE" character varying(200) NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "STARTS_AT_UTC" timestamp(3) with time zone,
        "ENDS_AT_UTC" timestamp(3) with time zone,
        "IS_ACTIVE" boolean NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_BANNERS" PRIMARY KEY ("BANNER_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."BUNDLES" (
        "BUNDLE_ID" uuid NOT NULL,
        "SLUG" character varying(200) NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "DESCRIPTION" character varying(4000),
        "PRICE" numeric(18,2) NOT NULL,
        "IS_ACTIVE" boolean NOT NULL,
        "STARTS_AT_UTC" timestamp(3) with time zone,
        "ENDS_AT_UTC" timestamp(3) with time zone,
        CONSTRAINT "PK_BUNDLES" PRIMARY KEY ("BUNDLE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."CARTS" (
        "CART_ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "UPDATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_CARTS" PRIMARY KEY ("CART_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."CATEGORIES" (
        "ID" uuid NOT NULL,
        "PARENT_ID" uuid,
        "SLUG" character varying(100) NOT NULL,
        "NAME_TH" character varying(200) NOT NULL,
        "NAME_EN" character varying(200) NOT NULL,
        "ICON_KEY" character varying(100),
        "SORT_ORDER" integer NOT NULL,
        "IS_ACTIVE" boolean NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_CATEGORIES" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_CATEGORIES_CATEGORIES_PARENT_ID" FOREIGN KEY ("PARENT_ID") REFERENCES "CATALOG"."CATEGORIES" ("ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "NOTIFY"."CONTACT_MESSAGES" (
        "ID" uuid NOT NULL,
        "NAME" character varying(100) NOT NULL,
        "EMAIL" character varying(255) NOT NULL,
        "SUBJECT" character varying(200) NOT NULL,
        "MESSAGE" character varying(4000) NOT NULL,
        "STATUS" character varying(50) NOT NULL,
        "RESOLVED_AT_UTC" timestamp with time zone,
        "RESOLVED_BY" uuid,
        "ADMIN_NOTES" character varying(1000),
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        "IS_DELETED" boolean NOT NULL DEFAULT FALSE,
        "DELETED_AT_UTC" timestamp with time zone,
        CONSTRAINT "PK_CONTACT_MESSAGES" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSE_REVIEWS" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "RATING" integer NOT NULL,
        "COMMENT" character varying(2000),
        "IS_PUBLISHED" boolean NOT NULL DEFAULT TRUE,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSE_REVIEWS" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "ANALYTICS"."DAILY_COURSE_STATS" (
        "DATE" date NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "VIEWS" integer NOT NULL,
        "ENROLLMENTS" integer NOT NULL,
        "REVENUE" numeric(18,2) NOT NULL,
        "COMPLETION_RATE" numeric(5,2) NOT NULL,
        CONSTRAINT "PK_DAILY_COURSE_STATS" PRIMARY KEY ("DATE", "COURSE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMUNITY"."DISCUSSIONS" (
        "DISCUSSION_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "EPISODE_ID" uuid,
        "USER_ID" uuid NOT NULL,
        "PARENT_ID" uuid,
        "BODY" character varying(4000) NOT NULL,
        "IS_INSTRUCTOR_ANSWER" boolean NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "UPVOTE_COUNT" integer NOT NULL,
        "IS_DELETED" boolean NOT NULL,
        "DELETED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_DISCUSSIONS" PRIMARY KEY ("DISCUSSION_ID"),
        CONSTRAINT "FK_DISCUSSIONS_DISCUSSIONS_PARENT_ID" FOREIGN KEY ("PARENT_ID") REFERENCES "COMMUNITY"."DISCUSSIONS" ("DISCUSSION_ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "NOTIFY"."EMAIL_OUTBOX" (
        "ID" uuid NOT NULL,
        "TO_EMAIL" character varying(256) NOT NULL,
        "SUBJECT" character varying(300) NOT NULL,
        "BODY_HTML" text NOT NULL,
        "TEMPLATE_KEY" character varying(100),
        "STATUS" character varying(32) NOT NULL,
        "ATTEMPTS" integer NOT NULL,
        "NEXT_RETRY_AT_UTC" timestamp(3) with time zone,
        "SENT_AT_UTC" timestamp(3) with time zone,
        "LAST_ERROR" character varying(2000),
        CONSTRAINT "PK_EMAIL_OUTBOX" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."ENROLLMENTS" (
        "ENROLLMENT_ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "ORDER_ID" uuid,
        "SOURCE" character varying(32) NOT NULL,
        "ENROLLED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "EXPIRES_AT_UTC" timestamp(3) with time zone,
        "STATUS" character varying(32) NOT NULL,
        "PROGRESS_PERCENT" numeric(5,2) NOT NULL,
        "COMPLETED_AT_UTC" timestamp(3) with time zone,
        "LAST_ACCESSED_AT_UTC" timestamp(3) with time zone,
        "ROW_VERSION" bytea NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_ENROLLMENTS" PRIMARY KEY ("ENROLLMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "ANALYTICS"."EPISODE_DROP_OFF" (
        "DATE" date NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "START_COUNT" integer NOT NULL,
        "COMPLETE_COUNT" integer NOT NULL,
        "AVG_WATCH_PERCENT" numeric(5,2) NOT NULL,
        CONSTRAINT "PK_EPISODE_DROP_OFF" PRIMARY KEY ("DATE", "EPISODE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CMS"."FEATURE_FLAGS" (
        "FEATURE_FLAG_ID" uuid NOT NULL,
        "KEY" character varying(100) NOT NULL,
        "NAME" character varying(200) NOT NULL,
        "DESCRIPTION" character varying(1000),
        "IS_ENABLED" boolean NOT NULL,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_FEATURE_FLAGS" PRIMARY KEY ("FEATURE_FLAG_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."FLASH_SALES" (
        "FLASH_SALE_ID" uuid NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "STARTS_AT_UTC" timestamp(3) with time zone NOT NULL,
        "ENDS_AT_UTC" timestamp(3) with time zone NOT NULL,
        "IS_ACTIVE" boolean NOT NULL,
        CONSTRAINT "PK_FLASH_SALES" PRIMARY KEY ("FLASH_SALE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "PAYOUT"."INSTRUCTOR_PAYOUT_ACCOUNTS" (
        "INSTRUCTOR_PAYOUT_ACCOUNT_ID" uuid NOT NULL,
        "INSTRUCTOR_ID" uuid NOT NULL,
        "BANK_CODE" character varying(20) NOT NULL,
        "ACCOUNT_NO_ENCRYPTED" character varying(500) NOT NULL,
        "ACCOUNT_NAME" character varying(200) NOT NULL,
        "TAX_ID" character varying(500),
        "TAX_PAYER_TYPE" character varying(32) NOT NULL,
        "VERIFIED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_INSTRUCTOR_PAYOUT_ACCOUNTS" PRIMARY KEY ("INSTRUCTOR_PAYOUT_ACCOUNT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."INSTRUCTOR_PROFILES" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "DISPLAY_NAME" character varying(200) NOT NULL,
        "HEADLINE" character varying(200),
        "BIO" character varying(2000) NOT NULL,
        "AVATAR_URL" character varying(1000),
        "REVENUE_SHARE_PERCENT" numeric(5,2) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "APPROVED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_INSTRUCTOR_PROFILES" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."LEARNING_PATHS" (
        "ID" uuid NOT NULL,
        "SLUG" character varying(200) NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "DESCRIPTION" character varying(2000),
        "IS_ACTIVE" boolean NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_LEARNING_PATHS" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "MEDIA"."MEDIA_ASSETS" (
        "MEDIA_ASSET_ID" uuid NOT NULL,
        "PROVIDER" character varying(50) NOT NULL,
        "PROVIDER_ASSET_ID" character varying(200) NOT NULL,
        "PLAYBACK_ID" character varying(200),
        "STATUS" character varying(32) NOT NULL,
        "DURATION_SECONDS" integer,
        "DRM_ENABLED" boolean NOT NULL,
        "THUMBNAIL_URL" character varying(1000),
        "UPLOADED_BY_USER_ID" uuid NOT NULL,
        "READY_AT_UTC" timestamp(3) with time zone,
        "ERROR_MESSAGE" character varying(2000),
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_MEDIA_ASSETS" PRIMARY KEY ("MEDIA_ASSET_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CMS"."MENU_ITEMS" (
        "MENU_ITEM_ID" uuid NOT NULL,
        "PARENT_ID" uuid,
        "LABEL" character varying(100) NOT NULL,
        "URL" character varying(1000) NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "IS_ACTIVE" boolean NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_MENU_ITEMS" PRIMARY KEY ("MENU_ITEM_ID"),
        CONSTRAINT "FK_MENU_ITEMS_MENU_ITEMS_PARENT_ID" FOREIGN KEY ("PARENT_ID") REFERENCES "CMS"."MENU_ITEMS" ("MENU_ITEM_ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "NOTIFY"."NOTIFICATIONS" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "TYPE" character varying(64) NOT NULL,
        "TITLE" character varying(300) NOT NULL,
        "BODY" character varying(2000) NOT NULL,
        "LINK_URL" character varying(1000),
        "READ_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_NOTIFICATIONS" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."ORDERS" (
        "ORDER_ID" uuid NOT NULL,
        "ORDER_NO" character varying(32) NOT NULL,
        "USER_ID" uuid NOT NULL,
        "SUBTOTAL_AMOUNT" numeric(18,2) NOT NULL,
        "DISCOUNT_AMOUNT" numeric(18,2) NOT NULL,
        "TAX_AMOUNT" numeric(18,2) NOT NULL,
        "TOTAL_AMOUNT" numeric(18,2) NOT NULL,
        "CURRENCY" char(3) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "PROMO_CODE_ID" uuid,
        "PAID_AT_UTC" timestamp(3) with time zone,
        "ROW_VERSION" bytea NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_ORDERS" PRIMARY KEY ("ORDER_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "PAYOUT"."PAYOUT_BATCHES" (
        "PAYOUT_BATCH_ID" uuid NOT NULL,
        "PERIOD_KEY" char(7) NOT NULL,
        "TOTAL_AMOUNT" numeric(18,2) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "EXECUTED_AT_UTC" timestamp(3) with time zone,
        "EXECUTED_BY_USER_ID" uuid,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_PAYOUT_BATCHES" PRIMARY KEY ("PAYOUT_BATCH_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "MEDIA"."PLAYBACK_SESSIONS" (
        "PLAYBACK_SESSION_ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "SESSION_ID" uuid NOT NULL,
        "ISSUED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "EXPIRES_AT_UTC" timestamp(3) with time zone NOT NULL,
        "IP_ADDRESS" character varying(64),
        "DEVICE_ID" character varying(200),
        CONSTRAINT "PK_PLAYBACK_SESSIONS" PRIMARY KEY ("PLAYBACK_SESSION_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CMS"."POSTS" (
        "POST_ID" uuid NOT NULL,
        "SLUG" character varying(200) NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "EXCERPT" character varying(500) NOT NULL,
        "CONTENT_HTML" text NOT NULL,
        "COVER_IMAGE_URL" character varying(1000),
        "AUTHOR_USER_ID" uuid NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "PUBLISHED_AT_UTC" timestamp(3) with time zone,
        "SEO_TITLE" character varying(200),
        "SEO_DESCRIPTION" character varying(500),
        "IS_DELETED" boolean NOT NULL,
        "DELETED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_POSTS" PRIMARY KEY ("POST_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."PROMO_CODES" (
        "PROMO_CODE_ID" uuid NOT NULL,
        "CODE" character varying(32) NOT NULL,
        "DISCOUNT_TYPE" character varying(32) NOT NULL,
        "DISCOUNT_VALUE" numeric(18,2) NOT NULL,
        "MAX_REDEMPTIONS" integer NOT NULL,
        "REDEEMED_COUNT" integer NOT NULL,
        "MAX_PER_USER" integer NOT NULL,
        "MIN_ORDER_AMOUNT" numeric(18,2) NOT NULL,
        "STARTS_AT_UTC" timestamp(3) with time zone NOT NULL,
        "ENDS_AT_UTC" timestamp(3) with time zone NOT NULL,
        "SCOPE" character varying(32) NOT NULL,
        "SCOPE_REF_ID" uuid,
        "IS_ACTIVE" boolean NOT NULL,
        "ROW_VERSION" bytea NOT NULL,
        CONSTRAINT "PK_PROMO_CODES" PRIMARY KEY ("PROMO_CODE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."QUIZZES" (
        "QUIZ_ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "PASSING_SCORE_PERCENT" integer NOT NULL,
        "MAX_ATTEMPTS" integer NOT NULL,
        "IS_ACTIVE" boolean NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_QUIZZES" PRIMARY KEY ("QUIZ_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CMS"."REDIRECTS" (
        "REDIRECT_ID" uuid NOT NULL,
        "FROM_PATH" character varying(500) NOT NULL,
        "TO_PATH" character varying(1000) NOT NULL,
        "STATUS_CODE" integer NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_REDIRECTS" PRIMARY KEY ("REDIRECT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "PAYOUT"."REVENUE_SPLITS" (
        "REVENUE_SPLIT_ID" uuid NOT NULL,
        "ORDER_ITEM_ID" uuid NOT NULL,
        "INSTRUCTOR_ID" uuid NOT NULL,
        "GROSS_AMOUNT" numeric(18,2) NOT NULL,
        "PAYMENT_FEE_AMOUNT" numeric(18,2) NOT NULL,
        "PLATFORM_FEE_AMOUNT" numeric(18,2) NOT NULL,
        "INSTRUCTOR_AMOUNT" numeric(18,2) NOT NULL,
        "REVENUE_SHARE_PERCENT" numeric(5,2) NOT NULL,
        "PERIOD_KEY" char(7) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "PAYOUT_BATCH_ITEM_ID" uuid,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_REVENUE_SPLITS" PRIMARY KEY ("REVENUE_SPLIT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."ROLES" (
        "ID" uuid NOT NULL,
        "NAME" character varying(50) NOT NULL,
        CONSTRAINT "PK_ROLES" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."STRIPE_WEBHOOK_EVENTS" (
        "STRIPE_WEBHOOK_EVENT_ID" uuid NOT NULL,
        "STRIPE_EVENT_ID" character varying(255) NOT NULL,
        "EVENT_TYPE" character varying(100) NOT NULL,
        "PAYLOAD_JSON" text NOT NULL,
        "RECEIVED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "PROCESSED_AT_UTC" timestamp(3) with time zone,
        "PROCESS_RESULT" character varying(500),
        CONSTRAINT "PK_STRIPE_WEBHOOK_EVENTS" PRIMARY KEY ("STRIPE_WEBHOOK_EVENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."USERS" (
        "ID" uuid NOT NULL,
        "EMAIL" character varying(256) NOT NULL,
        "NORMALIZED_EMAIL" character varying(256) NOT NULL,
        "PASSWORD_HASH" character varying(512) NOT NULL,
        "DISPLAY_NAME" character varying(200) NOT NULL,
        "AVATAR_URL" character varying(1000),
        "PHONE_NUMBER" character varying(32),
        "STATUS" character varying(32) NOT NULL,
        "EMAIL_CONFIRMED_AT_UTC" timestamp(3) with time zone,
        "TWO_FACTOR_ENABLED" boolean NOT NULL,
        "LAST_LOGIN_AT_UTC" timestamp(3) with time zone,
        "MAX_CONCURRENT_SESSIONS_OVERRIDE" integer,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_USERS" PRIMARY KEY ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."WISHLISTS" (
        "USER_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_WISHLISTS" PRIMARY KEY ("USER_ID", "COURSE_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."ASSIGNMENT_SUBMISSIONS" (
        "ASSIGNMENT_SUBMISSION_ID" uuid NOT NULL,
        "ASSIGNMENT_ID" uuid NOT NULL,
        "ENROLLMENT_ID" uuid NOT NULL,
        "STORAGE_KEY" character varying(500) NOT NULL,
        "NOTE" character varying(2000),
        "SUBMITTED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "SCORE" numeric(5,2),
        "FEEDBACK" character varying(2000),
        "GRADED_BY_USER_ID" uuid,
        "GRADED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_ASSIGNMENT_SUBMISSIONS" PRIMARY KEY ("ASSIGNMENT_SUBMISSION_ID"),
        CONSTRAINT "FK_ASSIGNMENT_SUBMISSIONS_ASSIGNMENTS_ASSIGNMENT_ID" FOREIGN KEY ("ASSIGNMENT_ID") REFERENCES "LEARNING"."ASSIGNMENTS" ("ASSIGNMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."BUNDLE_ITEMS" (
        "BUNDLE_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        CONSTRAINT "PK_BUNDLE_ITEMS" PRIMARY KEY ("BUNDLE_ID", "COURSE_ID"),
        CONSTRAINT "FK_BUNDLE_ITEMS_BUNDLES_BUNDLE_ID" FOREIGN KEY ("BUNDLE_ID") REFERENCES "COMMERCE"."BUNDLES" ("BUNDLE_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."CART_ITEMS" (
        "CART_ITEM_ID" uuid NOT NULL,
        "CART_ID" uuid NOT NULL,
        "ITEM_TYPE" character varying(32) NOT NULL,
        "REF_ID" uuid NOT NULL,
        "ADDED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_CART_ITEMS" PRIMARY KEY ("CART_ITEM_ID"),
        CONSTRAINT "FK_CART_ITEMS_CARTS_CART_ID" FOREIGN KEY ("CART_ID") REFERENCES "COMMERCE"."CARTS" ("CART_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMUNITY"."REPORTS" (
        "REPORT_ID" uuid NOT NULL,
        "DISCUSSION_ID" uuid NOT NULL,
        "REPORTED_BY_USER_ID" uuid NOT NULL,
        "REASON" character varying(500) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "RESOLVED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_REPORTS" PRIMARY KEY ("REPORT_ID"),
        CONSTRAINT "FK_REPORTS_DISCUSSIONS_DISCUSSION_ID" FOREIGN KEY ("DISCUSSION_ID") REFERENCES "COMMUNITY"."DISCUSSIONS" ("DISCUSSION_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."CERTIFICATES" (
        "CERTIFICATE_ID" uuid NOT NULL,
        "ENROLLMENT_ID" uuid NOT NULL,
        "SERIAL_NO" character varying(50) NOT NULL,
        "VERIFY_CODE" character varying(50) NOT NULL,
        "ISSUED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "PDF_STORAGE_KEY" character varying(500),
        "REVOKED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_CERTIFICATES" PRIMARY KEY ("CERTIFICATE_ID"),
        CONSTRAINT "FK_CERTIFICATES_ENROLLMENTS_ENROLLMENT_ID" FOREIGN KEY ("ENROLLMENT_ID") REFERENCES "LEARNING"."ENROLLMENTS" ("ENROLLMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."EPISODE_PROGRESS" (
        "EPISODE_PROGRESS_ID" uuid NOT NULL,
        "ENROLLMENT_ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "LAST_POSITION_SECONDS" integer NOT NULL,
        "WATCHED_SECONDS" integer NOT NULL,
        "IS_COMPLETED" boolean NOT NULL,
        "COMPLETED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_EPISODE_PROGRESS" PRIMARY KEY ("EPISODE_PROGRESS_ID"),
        CONSTRAINT "FK_EPISODE_PROGRESS_ENROLLMENTS_ENROLLMENT_ID" FOREIGN KEY ("ENROLLMENT_ID") REFERENCES "LEARNING"."ENROLLMENTS" ("ENROLLMENT_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."WATCH_EVENTS" (
        "WATCH_EVENT_ID" bigint GENERATED BY DEFAULT AS IDENTITY,
        "ENROLLMENT_ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "EVENT_TYPE" character varying(32) NOT NULL,
        "POSITION_SECONDS" integer NOT NULL,
        "OCCURRED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_WATCH_EVENTS" PRIMARY KEY ("WATCH_EVENT_ID"),
        CONSTRAINT "FK_WATCH_EVENTS_ENROLLMENTS_ENROLLMENT_ID" FOREIGN KEY ("ENROLLMENT_ID") REFERENCES "LEARNING"."ENROLLMENTS" ("ENROLLMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."FLASH_SALE_ITEMS" (
        "FLASH_SALE_ITEM_ID" uuid NOT NULL,
        "FLASH_SALE_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "SALE_PRICE" numeric(18,2) NOT NULL,
        CONSTRAINT "PK_FLASH_SALE_ITEMS" PRIMARY KEY ("FLASH_SALE_ITEM_ID"),
        CONSTRAINT "FK_FLASH_SALE_ITEMS_FLASH_SALES_FLASH_SALE_ID" FOREIGN KEY ("FLASH_SALE_ID") REFERENCES "COMMERCE"."FLASH_SALES" ("FLASH_SALE_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSES" (
        "ID" uuid NOT NULL,
        "SLUG" character varying(200) NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "SUBTITLE" character varying(300),
        "DESCRIPTION" character varying(4000),
        "INSTRUCTOR_ID" uuid NOT NULL,
        "CATEGORY_ID" uuid NOT NULL,
        "LEVEL" character varying(32) NOT NULL,
        "LANGUAGE" character varying(32) NOT NULL,
        "THUMBNAIL_URL" character varying(1000),
        "TRAILER_MEDIA_ASSET_ID" uuid,
        "PRICE" numeric(18,2) NOT NULL,
        "COMPARE_PRICE" numeric(18,2),
        "CURRENCY" char(3) NOT NULL,
        "ACCESS_DURATION_DAYS" integer,
        "STATUS" character varying(32) NOT NULL,
        "PUBLISHED_AT_UTC" timestamp(3) with time zone,
        "REJECTION_REASON" character varying(1000),
        "TOTAL_DURATION_SECONDS" integer NOT NULL,
        "EPISODE_COUNT" integer NOT NULL,
        "RATING_AVERAGE" numeric(3,2) NOT NULL,
        "RATING_COUNT" integer NOT NULL,
        "ENROLLMENT_COUNT" integer NOT NULL,
        "SEO_TITLE" character varying(200),
        "SEO_DESCRIPTION" character varying(500),
        "ROW_VERSION" bytea NOT NULL,
        "IS_DELETED" boolean NOT NULL,
        "DELETED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSES" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_COURSES_INSTRUCTOR_PROFILES_INSTRUCTOR_ID" FOREIGN KEY ("INSTRUCTOR_ID") REFERENCES "CATALOG"."INSTRUCTOR_PROFILES" ("ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "MEDIA"."MEDIA_UPLOAD_SESSIONS" (
        "MEDIA_UPLOAD_SESSION_ID" uuid NOT NULL,
        "MEDIA_ASSET_ID" uuid NOT NULL,
        "UPLOAD_URL" character varying(1000) NOT NULL,
        "EXPIRES_AT_UTC" timestamp(3) with time zone NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_MEDIA_UPLOAD_SESSIONS" PRIMARY KEY ("MEDIA_UPLOAD_SESSION_ID"),
        CONSTRAINT "FK_MEDIA_UPLOAD_SESSIONS_MEDIA_ASSETS_MEDIA_ASSET_ID" FOREIGN KEY ("MEDIA_ASSET_ID") REFERENCES "MEDIA"."MEDIA_ASSETS" ("MEDIA_ASSET_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."ORDER_ITEMS" (
        "ORDER_ITEM_ID" uuid NOT NULL,
        "ORDER_ID" uuid NOT NULL,
        "COURSE_ID" uuid,
        "TITLE_SNAPSHOT" character varying(200) NOT NULL,
        "UNIT_PRICE" numeric(18,2) NOT NULL,
        "LINE_TOTAL" numeric(18,2) NOT NULL,
        CONSTRAINT "PK_ORDER_ITEMS" PRIMARY KEY ("ORDER_ITEM_ID"),
        CONSTRAINT "FK_ORDER_ITEMS_ORDERS_ORDER_ID" FOREIGN KEY ("ORDER_ID") REFERENCES "COMMERCE"."ORDERS" ("ORDER_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."PAYMENTS" (
        "PAYMENT_ID" uuid NOT NULL,
        "ORDER_ID" uuid NOT NULL,
        "METHOD" character varying(32) NOT NULL,
        "PROVIDER" character varying(32) NOT NULL,
        "PROVIDER_PAYMENT_INTENT_ID" character varying(255) NOT NULL,
        "AMOUNT" numeric(18,2) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "SUCCEEDED_AT_UTC" timestamp(3) with time zone,
        "FAILURE_REASON" character varying(500),
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_PAYMENTS" PRIMARY KEY ("PAYMENT_ID"),
        CONSTRAINT "FK_PAYMENTS_ORDERS_ORDER_ID" FOREIGN KEY ("ORDER_ID") REFERENCES "COMMERCE"."ORDERS" ("ORDER_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."TAX_INVOICES" (
        "TAX_INVOICE_ID" uuid NOT NULL,
        "ORDER_ID" uuid NOT NULL,
        "TAX_ID_ENCRYPTED" character varying(500) NOT NULL,
        "BUYER_NAME" character varying(200) NOT NULL,
        "INVOICE_NO" character varying(50) NOT NULL,
        "ISSUED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "PDF_STORAGE_KEY" character varying(1000),
        "STATUS" character varying(32) NOT NULL,
        CONSTRAINT "PK_TAX_INVOICES" PRIMARY KEY ("TAX_INVOICE_ID"),
        CONSTRAINT "FK_TAX_INVOICES_ORDERS_ORDER_ID" FOREIGN KEY ("ORDER_ID") REFERENCES "COMMERCE"."ORDERS" ("ORDER_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "PAYOUT"."PAYOUT_BATCH_ITEMS" (
        "PAYOUT_BATCH_ITEM_ID" uuid NOT NULL,
        "BATCH_ID" uuid NOT NULL,
        "INSTRUCTOR_ID" uuid NOT NULL,
        "AMOUNT" numeric(18,2) NOT NULL,
        "WITHHOLDING_TAX_PERCENT" numeric(5,2) NOT NULL,
        "WITHHOLDING_TAX_AMOUNT" numeric(18,2) NOT NULL,
        "NET_AMOUNT" numeric(18,2) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "TRANSFER_REF" character varying(200),
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_PAYOUT_BATCH_ITEMS" PRIMARY KEY ("PAYOUT_BATCH_ITEM_ID"),
        CONSTRAINT "FK_PAYOUT_BATCH_ITEMS_PAYOUT_BATCHES_BATCH_ID" FOREIGN KEY ("BATCH_ID") REFERENCES "PAYOUT"."PAYOUT_BATCHES" ("PAYOUT_BATCH_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."PROMO_REDEMPTIONS" (
        "PROMO_REDEMPTION_ID" uuid NOT NULL,
        "PROMO_CODE_ID" uuid NOT NULL,
        "ORDER_ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "REDEEMED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_PROMO_REDEMPTIONS" PRIMARY KEY ("PROMO_REDEMPTION_ID"),
        CONSTRAINT "FK_PROMO_REDEMPTIONS_ORDERS_ORDER_ID" FOREIGN KEY ("ORDER_ID") REFERENCES "COMMERCE"."ORDERS" ("ORDER_ID"),
        CONSTRAINT "FK_PROMO_REDEMPTIONS_PROMO_CODES_PROMO_CODE_ID" FOREIGN KEY ("PROMO_CODE_ID") REFERENCES "COMMERCE"."PROMO_CODES" ("PROMO_CODE_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."QUIZ_ATTEMPTS" (
        "QUIZ_ATTEMPT_ID" uuid NOT NULL,
        "QUIZ_ID" uuid NOT NULL,
        "ENROLLMENT_ID" uuid NOT NULL,
        "ATTEMPT_NO" integer NOT NULL,
        "SCORE_PERCENT" numeric(5,2),
        "IS_PASSED" boolean NOT NULL,
        "STARTED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "SUBMITTED_AT_UTC" timestamp(3) with time zone,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_QUIZ_ATTEMPTS" PRIMARY KEY ("QUIZ_ATTEMPT_ID"),
        CONSTRAINT "FK_QUIZ_ATTEMPTS_QUIZZES_QUIZ_ID" FOREIGN KEY ("QUIZ_ID") REFERENCES "LEARNING"."QUIZZES" ("QUIZ_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."QUIZ_QUESTIONS" (
        "QUIZ_QUESTION_ID" uuid NOT NULL,
        "QUIZ_ID" uuid NOT NULL,
        "TYPE" character varying(32) NOT NULL,
        "TEXT" character varying(1000) NOT NULL,
        "EXPLANATION" character varying(2000),
        "POINTS" integer NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        CONSTRAINT "PK_QUIZ_QUESTIONS" PRIMARY KEY ("QUIZ_QUESTION_ID"),
        CONSTRAINT "FK_QUIZ_QUESTIONS_QUIZZES_QUIZ_ID" FOREIGN KEY ("QUIZ_ID") REFERENCES "LEARNING"."QUIZZES" ("QUIZ_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."SECURITY_AUDITS" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid,
        "EVENT_TYPE" character varying(100) NOT NULL,
        "DETAIL" text,
        "IP_ADDRESS" character varying(64),
        "OCCURRED_AT_UTC" timestamp(3) with time zone NOT NULL,
        CONSTRAINT "PK_SECURITY_AUDITS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_SECURITY_AUDITS_USERS_USER_ID" FOREIGN KEY ("USER_ID") REFERENCES "IDENTITY"."USERS" ("ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."USER_ROLES" (
        "USER_ID" uuid NOT NULL,
        "ROLE_ID" uuid NOT NULL,
        CONSTRAINT "PK_USER_ROLES" PRIMARY KEY ("USER_ID", "ROLE_ID"),
        CONSTRAINT "FK_USER_ROLES_ROLES_ROLE_ID" FOREIGN KEY ("ROLE_ID") REFERENCES "IDENTITY"."ROLES" ("ID") ON DELETE CASCADE,
        CONSTRAINT "FK_USER_ROLES_USERS_USER_ID" FOREIGN KEY ("USER_ID") REFERENCES "IDENTITY"."USERS" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."USER_SECURITY_TOKENS" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "TOKEN_HASH" character varying(256) NOT NULL,
        "PURPOSE" character varying(32) NOT NULL,
        "EXPIRES_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CONSUMED_AT_UTC" timestamp(3) with time zone,
        CONSTRAINT "PK_USER_SECURITY_TOKENS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_USER_SECURITY_TOKENS_USERS_USER_ID" FOREIGN KEY ("USER_ID") REFERENCES "IDENTITY"."USERS" ("ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."USER_SESSIONS" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "DEVICE_ID" character varying(200) NOT NULL,
        "DEVICE_NAME" character varying(200),
        "USER_AGENT" character varying(500),
        "IP_ADDRESS" character varying(64),
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "LAST_SEEN_AT_UTC" timestamp(3) with time zone NOT NULL,
        "REVOKED_AT_UTC" timestamp(3) with time zone,
        "REVOKE_REASON" character varying(200),
        CONSTRAINT "PK_USER_SESSIONS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_USER_SESSIONS_USERS_USER_ID" FOREIGN KEY ("USER_ID") REFERENCES "IDENTITY"."USERS" ("ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSE_OUTCOMES" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "TEXT" character varying(500) NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSE_OUTCOMES" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_COURSE_OUTCOMES_COURSES_COURSE_ID" FOREIGN KEY ("COURSE_ID") REFERENCES "CATALOG"."COURSES" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSE_REQUIREMENTS" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "TEXT" character varying(500) NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSE_REQUIREMENTS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_COURSE_REQUIREMENTS_COURSES_COURSE_ID" FOREIGN KEY ("COURSE_ID") REFERENCES "CATALOG"."COURSES" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSE_SECTIONS" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSE_SECTIONS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_COURSE_SECTIONS_COURSES_COURSE_ID" FOREIGN KEY ("COURSE_ID") REFERENCES "CATALOG"."COURSES" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."LEARNING_PATH_ITEMS" (
        "PATH_ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_LEARNING_PATH_ITEMS" PRIMARY KEY ("PATH_ID", "COURSE_ID"),
        CONSTRAINT "FK_LEARNING_PATH_ITEMS_COURSES_COURSE_ID" FOREIGN KEY ("COURSE_ID") REFERENCES "CATALOG"."COURSES" ("ID") ON DELETE RESTRICT,
        CONSTRAINT "FK_LEARNING_PATH_ITEMS_LEARNING_PATHS_PATH_ID" FOREIGN KEY ("PATH_ID") REFERENCES "CATALOG"."LEARNING_PATHS" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."PAYMENT_OPS_QUEUE" (
        "PAYMENT_OPS_QUEUE_ID" uuid NOT NULL,
        "PAYMENT_ID" uuid NOT NULL,
        "REASON" character varying(200) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "ASSIGNED_TO_USER_ID" uuid,
        "RESOLVED_BY_USER_ID" uuid,
        "RESOLVED_AT_UTC" timestamp(3) with time zone,
        "NOTE" character varying(1000),
        CONSTRAINT "PK_PAYMENT_OPS_QUEUE" PRIMARY KEY ("PAYMENT_OPS_QUEUE_ID"),
        CONSTRAINT "FK_PAYMENT_OPS_QUEUE_PAYMENTS_PAYMENT_ID" FOREIGN KEY ("PAYMENT_ID") REFERENCES "COMMERCE"."PAYMENTS" ("PAYMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "COMMERCE"."REFUNDS" (
        "REFUND_ID" uuid NOT NULL,
        "PAYMENT_ID" uuid NOT NULL,
        "AMOUNT" numeric(18,2) NOT NULL,
        "REASON" character varying(1000) NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "REQUESTED_BY_USER_ID" uuid NOT NULL,
        "REQUESTED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "DECIDED_BY_USER_ID" uuid,
        "DECIDED_AT_UTC" timestamp(3) with time zone,
        "DECISION_NOTE" character varying(1000),
        "STRIPE_REFUND_ID" character varying(255),
        "COMPLETED_AT_UTC" timestamp(3) with time zone,
        CONSTRAINT "PK_REFUNDS" PRIMARY KEY ("REFUND_ID"),
        CONSTRAINT "FK_REFUNDS_PAYMENTS_PAYMENT_ID" FOREIGN KEY ("PAYMENT_ID") REFERENCES "COMMERCE"."PAYMENTS" ("PAYMENT_ID")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."QUIZ_ATTEMPT_ANSWERS" (
        "QUIZ_ATTEMPT_ANSWER_ID" uuid NOT NULL,
        "ATTEMPT_ID" uuid NOT NULL,
        "QUESTION_ID" uuid NOT NULL,
        "SELECTED_OPTION_IDS" text NOT NULL,
        "IS_CORRECT" boolean NOT NULL,
        CONSTRAINT "PK_QUIZ_ATTEMPT_ANSWERS" PRIMARY KEY ("QUIZ_ATTEMPT_ANSWER_ID"),
        CONSTRAINT "FK_QUIZ_ATTEMPT_ANSWERS_QUIZ_ATTEMPTS_ATTEMPT_ID" FOREIGN KEY ("ATTEMPT_ID") REFERENCES "LEARNING"."QUIZ_ATTEMPTS" ("QUIZ_ATTEMPT_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "LEARNING"."QUIZ_OPTIONS" (
        "QUIZ_OPTION_ID" uuid NOT NULL,
        "QUESTION_ID" uuid NOT NULL,
        "TEXT" character varying(500) NOT NULL,
        "IS_CORRECT" boolean NOT NULL,
        "SORT_ORDER" integer NOT NULL,
        CONSTRAINT "PK_QUIZ_OPTIONS" PRIMARY KEY ("QUIZ_OPTION_ID"),
        CONSTRAINT "FK_QUIZ_OPTIONS_QUIZ_QUESTIONS_QUESTION_ID" FOREIGN KEY ("QUESTION_ID") REFERENCES "LEARNING"."QUIZ_QUESTIONS" ("QUIZ_QUESTION_ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "IDENTITY"."REFRESH_TOKENS" (
        "ID" uuid NOT NULL,
        "USER_ID" uuid NOT NULL,
        "SESSION_ID" uuid NOT NULL,
        "TOKEN_HASH" character varying(256) NOT NULL,
        "EXPIRES_AT_UTC" timestamp(3) with time zone NOT NULL,
        "REVOKED_AT_UTC" timestamp(3) with time zone,
        "REPLACED_BY_TOKEN_ID" uuid,
        CONSTRAINT "PK_REFRESH_TOKENS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_REFRESH_TOKENS_REFRESH_TOKENS_REPLACED_BY_TOKEN_ID" FOREIGN KEY ("REPLACED_BY_TOKEN_ID") REFERENCES "IDENTITY"."REFRESH_TOKENS" ("ID") ON DELETE RESTRICT,
        CONSTRAINT "FK_REFRESH_TOKENS_USERS_USER_ID" FOREIGN KEY ("USER_ID") REFERENCES "IDENTITY"."USERS" ("ID") ON DELETE RESTRICT,
        CONSTRAINT "FK_REFRESH_TOKENS_USER_SESSIONS_SESSION_ID" FOREIGN KEY ("SESSION_ID") REFERENCES "IDENTITY"."USER_SESSIONS" ("ID") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."COURSE_EPISODES" (
        "ID" uuid NOT NULL,
        "COURSE_ID" uuid NOT NULL,
        "SECTION_ID" uuid NOT NULL,
        "TITLE" character varying(200) NOT NULL,
        "DESCRIPTION" character varying(2000),
        "SORT_ORDER" integer NOT NULL,
        "MEDIA_ASSET_ID" uuid,
        "DURATION_SECONDS" integer,
        "IS_FREE_PREVIEW" boolean NOT NULL,
        "STATUS" character varying(32) NOT NULL,
        "CREATED_AT_UTC" timestamp(3) with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp(3) with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_COURSE_EPISODES" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_COURSE_EPISODES_COURSES_COURSE_ID" FOREIGN KEY ("COURSE_ID") REFERENCES "CATALOG"."COURSES" ("ID"),
        CONSTRAINT "FK_COURSE_EPISODES_COURSE_SECTIONS_SECTION_ID" FOREIGN KEY ("SECTION_ID") REFERENCES "CATALOG"."COURSE_SECTIONS" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE TABLE "CATALOG"."EPISODE_ATTACHMENTS" (
        "ID" uuid NOT NULL,
        "EPISODE_ID" uuid NOT NULL,
        "FILE_NAME" character varying(255) NOT NULL,
        "STORAGE_KEY" character varying(500) NOT NULL,
        "CONTENT_TYPE" character varying(100) NOT NULL,
        "SIZE_BYTES" bigint NOT NULL,
        "CREATED_AT_UTC" timestamp with time zone NOT NULL,
        "CREATED_BY" uuid,
        "UPDATED_AT_UTC" timestamp with time zone,
        "UPDATED_BY" uuid,
        CONSTRAINT "PK_EPISODE_ATTACHMENTS" PRIMARY KEY ("ID"),
        CONSTRAINT "FK_EPISODE_ATTACHMENTS_COURSE_EPISODES_EPISODE_ID" FOREIGN KEY ("EPISODE_ID") REFERENCES "CATALOG"."COURSE_EPISODES" ("ID") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    INSERT INTO "IDENTITY"."ROLES" ("ID", "NAME")
    VALUES ('00000000-0000-0000-0000-000000000001', 'Learner');
    INSERT INTO "IDENTITY"."ROLES" ("ID", "NAME")
    VALUES ('00000000-0000-0000-0000-000000000002', 'Instructor');
    INSERT INTO "IDENTITY"."ROLES" ("ID", "NAME")
    VALUES ('00000000-0000-0000-0000-000000000003', 'Admin');
    INSERT INTO "IDENTITY"."ROLES" ("ID", "NAME")
    VALUES ('00000000-0000-0000-0000-000000000004', 'SuperAdmin');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ANNOUNCEMENTS_COURSE_ID" ON "NOTIFY"."ANNOUNCEMENTS" ("COURSE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ANNOUNCEMENTS_INSTRUCTOR_ID" ON "NOTIFY"."ANNOUNCEMENTS" ("INSTRUCTOR_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ASSIGNMENT_SUBMISSIONS_ASSIGNMENT_ID_STATUS" ON "LEARNING"."ASSIGNMENT_SUBMISSIONS" ("ASSIGNMENT_ID", "STATUS");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ASSIGNMENT_SUBMISSIONS_ENROLLMENT_ID" ON "LEARNING"."ASSIGNMENT_SUBMISSIONS" ("ENROLLMENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ASSIGNMENTS_EPISODE_ID" ON "LEARNING"."ASSIGNMENTS" ("EPISODE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_BANNERS_PLACEMENT_IS_ACTIVE_SORT_ORDER" ON "CMS"."BANNERS" ("PLACEMENT", "IS_ACTIVE", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_BUNDLES_SLUG" ON "COMMERCE"."BUNDLES" ("SLUG");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_CART_ITEMS_CART_ID" ON "COMMERCE"."CART_ITEMS" ("CART_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_CARTS_USER_ID" ON "COMMERCE"."CARTS" ("USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_CATEGORIES_PARENT_ID_SORT_ORDER" ON "CATALOG"."CATEGORIES" ("PARENT_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_CATEGORIES_SLUG" ON "CATALOG"."CATEGORIES" ("SLUG");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_CERTIFICATES_ENROLLMENT_ID" ON "LEARNING"."CERTIFICATES" ("ENROLLMENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_CERTIFICATES_SERIAL_NO" ON "LEARNING"."CERTIFICATES" ("SERIAL_NO");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_CERTIFICATES_VERIFY_CODE" ON "LEARNING"."CERTIFICATES" ("VERIFY_CODE");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_CONTACT_MESSAGES_CREATED_AT_UTC" ON "NOTIFY"."CONTACT_MESSAGES" ("CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_CONTACT_MESSAGES_IS_DELETED" ON "NOTIFY"."CONTACT_MESSAGES" ("IS_DELETED");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_CONTACT_MESSAGES_STATUS" ON "NOTIFY"."CONTACT_MESSAGES" ("STATUS");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSE_EPISODES_COURSE_ID" ON "CATALOG"."COURSE_EPISODES" ("COURSE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_COURSE_EPISODES_SECTION_ID_SORT_ORDER" ON "CATALOG"."COURSE_EPISODES" ("SECTION_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSE_OUTCOMES_COURSE_ID_SORT_ORDER" ON "CATALOG"."COURSE_OUTCOMES" ("COURSE_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSE_REQUIREMENTS_COURSE_ID_SORT_ORDER" ON "CATALOG"."COURSE_REQUIREMENTS" ("COURSE_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSE_REVIEWS_COURSE_ID_CREATED_AT_UTC" ON "CATALOG"."COURSE_REVIEWS" ("COURSE_ID", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_COURSE_REVIEWS_COURSE_ID_USER_ID" ON "CATALOG"."COURSE_REVIEWS" ("COURSE_ID", "USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSE_SECTIONS_COURSE_ID_SORT_ORDER" ON "CATALOG"."COURSE_SECTIONS" ("COURSE_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSES_DESCRIPTION" ON "CATALOG"."COURSES" USING gin ("DESCRIPTION" gin_trgm_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSES_INSTRUCTOR_ID" ON "CATALOG"."COURSES" ("INSTRUCTOR_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_COURSES_SLUG" ON "CATALOG"."COURSES" ("SLUG") WHERE "IS_DELETED" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSES_STATUS_CATEGORY_ID_PUBLISHED_AT_UTC" ON "CATALOG"."COURSES" ("STATUS", "CATEGORY_ID", "PUBLISHED_AT_UTC") INCLUDE ("TITLE", "SLUG", "PRICE", "RATING_AVERAGE", "THUMBNAIL_URL");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSES_SUBTITLE" ON "CATALOG"."COURSES" USING gin ("SUBTITLE" gin_trgm_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_COURSES_TITLE" ON "CATALOG"."COURSES" USING gin ("TITLE" gin_trgm_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_DAILY_COURSE_STATS_COURSE_ID_DATE" ON "ANALYTICS"."DAILY_COURSE_STATS" ("COURSE_ID", "DATE");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_DISCUSSIONS_EPISODE_ID_STATUS_CREATED_AT_UTC" ON "COMMUNITY"."DISCUSSIONS" ("EPISODE_ID", "STATUS", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_DISCUSSIONS_PARENT_ID" ON "COMMUNITY"."DISCUSSIONS" ("PARENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_EMAIL_OUTBOX_STATUS_NEXT_RETRY_AT_UTC" ON "NOTIFY"."EMAIL_OUTBOX" ("STATUS", "NEXT_RETRY_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ENROLLMENTS_COURSE_ID_STATUS" ON "LEARNING"."ENROLLMENTS" ("COURSE_ID", "STATUS");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ENROLLMENTS_MY_COURSES" ON "LEARNING"."ENROLLMENTS" ("USER_ID", "STATUS") INCLUDE ("COURSE_ID", "PROGRESS_PERCENT", "LAST_ACCESSED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_ENROLLMENTS_USER_ID_COURSE_ID" ON "LEARNING"."ENROLLMENTS" ("USER_ID", "COURSE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_EPISODE_ATTACHMENTS_EPISODE_ID_CREATED_AT_UTC" ON "CATALOG"."EPISODE_ATTACHMENTS" ("EPISODE_ID", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_EPISODE_DROP_OFF_EPISODE_ID_DATE" ON "ANALYTICS"."EPISODE_DROP_OFF" ("EPISODE_ID", "DATE");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_EPISODE_PROGRESS_RESUME" ON "LEARNING"."EPISODE_PROGRESS" ("ENROLLMENT_ID", "EPISODE_ID") INCLUDE ("LAST_POSITION_SECONDS", "IS_COMPLETED");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_FEATURE_FLAGS_KEY" ON "CMS"."FEATURE_FLAGS" ("KEY");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_FLASH_SALE_ITEMS_FLASH_SALE_ID_COURSE_ID" ON "COMMERCE"."FLASH_SALE_ITEMS" ("FLASH_SALE_ID", "COURSE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_INSTRUCTOR_PAYOUT_ACCOUNTS_INSTRUCTOR_ID" ON "PAYOUT"."INSTRUCTOR_PAYOUT_ACCOUNTS" ("INSTRUCTOR_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_INSTRUCTOR_PROFILES_STATUS_CREATED_AT_UTC" ON "CATALOG"."INSTRUCTOR_PROFILES" ("STATUS", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_INSTRUCTOR_PROFILES_USER_ID" ON "CATALOG"."INSTRUCTOR_PROFILES" ("USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_LEARNING_PATH_ITEMS_COURSE_ID" ON "CATALOG"."LEARNING_PATH_ITEMS" ("COURSE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_LEARNING_PATH_ITEMS_PATH_ID_SORT_ORDER" ON "CATALOG"."LEARNING_PATH_ITEMS" ("PATH_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_LEARNING_PATHS_IS_ACTIVE_SORT_ORDER" ON "CATALOG"."LEARNING_PATHS" ("IS_ACTIVE", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_LEARNING_PATHS_SLUG" ON "CATALOG"."LEARNING_PATHS" ("SLUG");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_MEDIA_ASSETS_PROVIDER_PROVIDER_ASSET_ID" ON "MEDIA"."MEDIA_ASSETS" ("PROVIDER", "PROVIDER_ASSET_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_MEDIA_UPLOAD_SESSIONS_MEDIA_ASSET_ID" ON "MEDIA"."MEDIA_UPLOAD_SESSIONS" ("MEDIA_ASSET_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_MENU_ITEMS_PARENT_ID_SORT_ORDER" ON "CMS"."MENU_ITEMS" ("PARENT_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_NOTIFICATIONS_USER_ID_CREATED_AT_UTC" ON "NOTIFY"."NOTIFICATIONS" ("USER_ID", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_NOTIFICATIONS_USER_ID_READ_AT_UTC" ON "NOTIFY"."NOTIFICATIONS" ("USER_ID", "READ_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ORDER_ITEMS_ORDER_ID" ON "COMMERCE"."ORDER_ITEMS" ("ORDER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_ORDERS_ORDER_NO" ON "COMMERCE"."ORDERS" ("ORDER_NO");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_ORDERS_USER_HISTORY" ON "COMMERCE"."ORDERS" ("USER_ID", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PAYMENT_OPS_QUEUE_PAYMENT_ID" ON "COMMERCE"."PAYMENT_OPS_QUEUE" ("PAYMENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PAYMENTS_ORDER_ID" ON "COMMERCE"."PAYMENTS" ("ORDER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_PAYMENTS_PROVIDER_PAYMENT_INTENT_ID" ON "COMMERCE"."PAYMENTS" ("PROVIDER_PAYMENT_INTENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PAYOUT_BATCH_ITEMS_BATCH_ID" ON "PAYOUT"."PAYOUT_BATCH_ITEMS" ("BATCH_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PAYOUT_BATCH_ITEMS_INSTRUCTOR_ID" ON "PAYOUT"."PAYOUT_BATCH_ITEMS" ("INSTRUCTOR_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PAYOUT_BATCHES_PERIOD_KEY" ON "PAYOUT"."PAYOUT_BATCHES" ("PERIOD_KEY");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PLAYBACK_SESSIONS_ISSUED_AT_UTC" ON "MEDIA"."PLAYBACK_SESSIONS" ("ISSUED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PLAYBACK_SESSIONS_USER_ID_ISSUED_AT_UTC" ON "MEDIA"."PLAYBACK_SESSIONS" ("USER_ID", "ISSUED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_POSTS_SLUG" ON "CMS"."POSTS" ("SLUG") WHERE "IS_DELETED" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_POSTS_STATUS_PUBLISHED_AT_UTC" ON "CMS"."POSTS" ("STATUS", "PUBLISHED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_PROMO_CODES_CODE" ON "COMMERCE"."PROMO_CODES" ("CODE");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PROMO_REDEMPTIONS_ORDER_ID" ON "COMMERCE"."PROMO_REDEMPTIONS" ("ORDER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_ORDER_ID" ON "COMMERCE"."PROMO_REDEMPTIONS" ("PROMO_CODE_ID", "ORDER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_USER_ID" ON "COMMERCE"."PROMO_REDEMPTIONS" ("PROMO_CODE_ID", "USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_QUIZ_ATTEMPT_ANSWERS_ATTEMPT_ID_QUESTION_ID" ON "LEARNING"."QUIZ_ATTEMPT_ANSWERS" ("ATTEMPT_ID", "QUESTION_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_QUIZ_ATTEMPTS_QUIZ_ID_ENROLLMENT_ID_ATTEMPT_NO" ON "LEARNING"."QUIZ_ATTEMPTS" ("QUIZ_ID", "ENROLLMENT_ID", "ATTEMPT_NO");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_QUIZ_OPTIONS_QUESTION_ID_SORT_ORDER" ON "LEARNING"."QUIZ_OPTIONS" ("QUESTION_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_QUIZ_QUESTIONS_QUIZ_ID_SORT_ORDER" ON "LEARNING"."QUIZ_QUESTIONS" ("QUIZ_ID", "SORT_ORDER");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_QUIZZES_EPISODE_ID" ON "LEARNING"."QUIZZES" ("EPISODE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_REDIRECTS_FROM_PATH" ON "CMS"."REDIRECTS" ("FROM_PATH");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REFRESH_TOKENS_REPLACED_BY_TOKEN_ID" ON "IDENTITY"."REFRESH_TOKENS" ("REPLACED_BY_TOKEN_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REFRESH_TOKENS_SESSION_ID" ON "IDENTITY"."REFRESH_TOKENS" ("SESSION_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_REFRESH_TOKENS_TOKEN_HASH" ON "IDENTITY"."REFRESH_TOKENS" ("TOKEN_HASH");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REFRESH_TOKENS_USER_ID" ON "IDENTITY"."REFRESH_TOKENS" ("USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REFUNDS_PAYMENT_ID" ON "COMMERCE"."REFUNDS" ("PAYMENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REFUNDS_REQUESTED_BY_USER" ON "COMMERCE"."REFUNDS" ("REQUESTED_BY_USER_ID", "REQUESTED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_REFUNDS_STRIPE_REFUND_ID" ON "COMMERCE"."REFUNDS" ("STRIPE_REFUND_ID") WHERE "STRIPE_REFUND_ID" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REPORTS_DISCUSSION_ID" ON "COMMUNITY"."REPORTS" ("DISCUSSION_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REPORTS_STATUS_CREATED_AT_UTC" ON "COMMUNITY"."REPORTS" ("STATUS", "CREATED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REVENUE_SPLITS_ORDER_ITEM_ID" ON "PAYOUT"."REVENUE_SPLITS" ("ORDER_ITEM_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REVENUE_SPLITS_PAYOUT" ON "PAYOUT"."REVENUE_SPLITS" ("INSTRUCTOR_ID", "PERIOD_KEY", "STATUS");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_REVENUE_SPLITS_PAYOUT_BATCH_ITEM_ID" ON "PAYOUT"."REVENUE_SPLITS" ("PAYOUT_BATCH_ITEM_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_ROLES_NAME" ON "IDENTITY"."ROLES" ("NAME");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_SECURITY_AUDITS_USER_ID" ON "IDENTITY"."SECURITY_AUDITS" ("USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_STRIPE_WEBHOOK_EVENTS_STRIPE_EVENT_ID" ON "COMMERCE"."STRIPE_WEBHOOK_EVENTS" ("STRIPE_EVENT_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_TAX_INVOICES_INVOICE_NO" ON "COMMERCE"."TAX_INVOICES" ("INVOICE_NO");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_TAX_INVOICES_ORDER_ID" ON "COMMERCE"."TAX_INVOICES" ("ORDER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_USER_ROLES_ROLE_ID" ON "IDENTITY"."USER_ROLES" ("ROLE_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_USER_SECURITY_TOKENS_ACTIVE" ON "IDENTITY"."USER_SECURITY_TOKENS" ("USER_ID", "PURPOSE") WHERE "CONSUMED_AT_UTC" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_USER_SECURITY_TOKENS_TOKEN_HASH" ON "IDENTITY"."USER_SECURITY_TOKENS" ("TOKEN_HASH");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_USER_SESSIONS_ACTIVE" ON "IDENTITY"."USER_SESSIONS" ("USER_ID") WHERE "REVOKED_AT_UTC" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_USER_SESSIONS_USER_ID_REVOKED_AT_UTC" ON "IDENTITY"."USER_SESSIONS" ("USER_ID", "REVOKED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE UNIQUE INDEX "IX_USERS_NORMALIZED_EMAIL" ON "IDENTITY"."USERS" ("NORMALIZED_EMAIL");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_WATCH_EVENTS_ENROLLMENT_ID_OCCURRED_AT_UTC" ON "LEARNING"."WATCH_EVENTS" ("ENROLLMENT_ID", "OCCURRED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_WATCH_EVENTS_OCCURRED_AT_UTC" ON "LEARNING"."WATCH_EVENTS" ("OCCURRED_AT_UTC");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    CREATE INDEX "IX_WISHLISTS_USER_ID" ON "CATALOG"."WISHLISTS" ("USER_ID");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905161924_InitialCreatePostgres') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905161924_InitialCreatePostgres', '10.0.11');
    END IF;
END $EF$;
COMMIT;

