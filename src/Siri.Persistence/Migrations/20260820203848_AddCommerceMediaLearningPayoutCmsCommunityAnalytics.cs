using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommerceMediaLearningPayoutCmsCommunityAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "LEARNING");

            migrationBuilder.EnsureSchema(
                name: "cms");

            migrationBuilder.EnsureSchema(
                name: "COMMERCE");

            migrationBuilder.EnsureSchema(
                name: "ANALYTICS");

            migrationBuilder.EnsureSchema(
                name: "community");

            migrationBuilder.EnsureSchema(
                name: "PAYOUT");

            migrationBuilder.EnsureSchema(
                name: "MEDIA");

            migrationBuilder.CreateTable(
                name: "ASSIGNMENTS",
                schema: "LEARNING",
                columns: table => new
                {
                    ASSIGNMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    INSTRUCTIONS = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DUE_DAYS = table.Column<int>(type: "int", nullable: true),
                    MAX_FILE_SIZE_MB = table.Column<int>(type: "int", nullable: false),
                    ALLOWED_EXTENSIONS = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ASSIGNMENTS", x => x.ASSIGNMENT_ID);
                });

            migrationBuilder.CreateTable(
                name: "BANNERS",
                schema: "cms",
                columns: table => new
                {
                    BANNER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PLACEMENT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IMAGE_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MOBILE_IMAGE_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LINK_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    STARTS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ENDS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BANNERS", x => x.BANNER_ID);
                });

            migrationBuilder.CreateTable(
                name: "BUNDLES",
                schema: "COMMERCE",
                columns: table => new
                {
                    BUNDLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SLUG = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PRICE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    STARTS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ENDS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUNDLES", x => x.BUNDLE_ID);
                });

            migrationBuilder.CreateTable(
                name: "CARTS",
                schema: "COMMERCE",
                columns: table => new
                {
                    CART_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CARTS", x => x.CART_ID);
                });

            migrationBuilder.CreateTable(
                name: "DAILY_COURSE_STATS",
                schema: "ANALYTICS",
                columns: table => new
                {
                    DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VIEWS = table.Column<int>(type: "int", nullable: false),
                    ENROLLMENTS = table.Column<int>(type: "int", nullable: false),
                    REVENUE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    COMPLETION_RATE = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DAILY_COURSE_STATS", x => new { x.DATE, x.COURSE_ID });
                });

            migrationBuilder.CreateTable(
                name: "DISCUSSIONS",
                schema: "community",
                columns: table => new
                {
                    DISCUSSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PARENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BODY = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IS_INSTRUCTOR_ANSWER = table.Column<bool>(type: "bit", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UPVOTE_COUNT = table.Column<int>(type: "int", nullable: false),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                    DELETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DISCUSSIONS", x => x.DISCUSSION_ID);
                    table.ForeignKey(
                        name: "FK_DISCUSSIONS_DISCUSSIONS_PARENT_ID",
                        column: x => x.PARENT_ID,
                        principalSchema: "community",
                        principalTable: "DISCUSSIONS",
                        principalColumn: "DISCUSSION_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ENROLLMENTS",
                schema: "LEARNING",
                columns: table => new
                {
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SOURCE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ENROLLED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EXPIRES_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PROGRESS_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    COMPLETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LAST_ACCESSED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ENROLLMENTS", x => x.ENROLLMENT_ID);
                });

            migrationBuilder.CreateTable(
                name: "EPISODE_DROP_OFF",
                schema: "ANALYTICS",
                columns: table => new
                {
                    DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    START_COUNT = table.Column<int>(type: "int", nullable: false),
                    COMPLETE_COUNT = table.Column<int>(type: "int", nullable: false),
                    AVG_WATCH_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EPISODE_DROP_OFF", x => new { x.DATE, x.EPISODE_ID });
                });

            migrationBuilder.CreateTable(
                name: "FLASH_SALES",
                schema: "COMMERCE",
                columns: table => new
                {
                    FLASH_SALE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    STARTS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ENDS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FLASH_SALES", x => x.FLASH_SALE_ID);
                });

            migrationBuilder.CreateTable(
                name: "INSTRUCTOR_PAYOUT_ACCOUNTS",
                schema: "PAYOUT",
                columns: table => new
                {
                    INSTRUCTOR_PAYOUT_ACCOUNT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    INSTRUCTOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BANK_CODE = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ACCOUNT_NO_ENCRYPTED = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ACCOUNT_NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TAX_ID = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VERIFIED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INSTRUCTOR_PAYOUT_ACCOUNTS", x => x.INSTRUCTOR_PAYOUT_ACCOUNT_ID);
                });

            migrationBuilder.CreateTable(
                name: "MEDIA_ASSETS",
                schema: "MEDIA",
                columns: table => new
                {
                    MEDIA_ASSET_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PROVIDER = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PROVIDER_ASSET_ID = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PLAYBACK_ID = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DURATION_SECONDS = table.Column<int>(type: "int", nullable: true),
                    DRM_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    THUMBNAIL_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UPLOADED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    READY_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ERROR_MESSAGE = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MEDIA_ASSETS", x => x.MEDIA_ASSET_ID);
                });

            migrationBuilder.CreateTable(
                name: "MENU_ITEMS",
                schema: "cms",
                columns: table => new
                {
                    MENU_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PARENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LABEL = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MENU_ITEMS", x => x.MENU_ITEM_ID);
                    table.ForeignKey(
                        name: "FK_MENU_ITEMS_MENU_ITEMS_PARENT_ID",
                        column: x => x.PARENT_ID,
                        principalSchema: "cms",
                        principalTable: "MENU_ITEMS",
                        principalColumn: "MENU_ITEM_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ORDERS",
                schema: "COMMERCE",
                columns: table => new
                {
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_NO = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SUBTOTAL_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DISCOUNT_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TAX_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TOTAL_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CURRENCY = table.Column<string>(type: "char(3)", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PROMO_CODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PAID_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDERS", x => x.ORDER_ID);
                });

            migrationBuilder.CreateTable(
                name: "PAYOUT_BATCHES",
                schema: "PAYOUT",
                columns: table => new
                {
                    PAYOUT_BATCH_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PERIOD_KEY = table.Column<string>(type: "char(7)", nullable: false),
                    TOTAL_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EXECUTED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    EXECUTED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYOUT_BATCHES", x => x.PAYOUT_BATCH_ID);
                });

            migrationBuilder.CreateTable(
                name: "PLAYBACK_SESSIONS",
                schema: "MEDIA",
                columns: table => new
                {
                    PLAYBACK_SESSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ISSUED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EXPIRES_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IP_ADDRESS = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DEVICE_ID = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PLAYBACK_SESSIONS", x => x.PLAYBACK_SESSION_ID);
                });

            migrationBuilder.CreateTable(
                name: "POSTS",
                schema: "cms",
                columns: table => new
                {
                    POST_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SLUG = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EXCERPT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CONTENT_HTML = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    COVER_IMAGE_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AUTHOR_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PUBLISHED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    SEO_TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SEO_DESCRIPTION = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                    DELETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POSTS", x => x.POST_ID);
                });

            migrationBuilder.CreateTable(
                name: "PROMO_CODES",
                schema: "COMMERCE",
                columns: table => new
                {
                    PROMO_CODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CODE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DISCOUNT_TYPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DISCOUNT_VALUE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MAX_REDEMPTIONS = table.Column<int>(type: "int", nullable: false),
                    REDEEMED_COUNT = table.Column<int>(type: "int", nullable: false),
                    MAX_PER_USER = table.Column<int>(type: "int", nullable: false),
                    MIN_ORDER_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    STARTS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ENDS_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SCOPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SCOPE_REF_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMO_CODES", x => x.PROMO_CODE_ID);
                });

            migrationBuilder.CreateTable(
                name: "QUIZZES",
                schema: "LEARNING",
                columns: table => new
                {
                    QUIZ_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PASSING_SCORE_PERCENT = table.Column<int>(type: "int", nullable: false),
                    MAX_ATTEMPTS = table.Column<int>(type: "int", nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUIZZES", x => x.QUIZ_ID);
                });

            migrationBuilder.CreateTable(
                name: "REDIRECTS",
                schema: "cms",
                columns: table => new
                {
                    REDIRECT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FROM_PATH = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TO_PATH = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    STATUS_CODE = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REDIRECTS", x => x.REDIRECT_ID);
                });

            migrationBuilder.CreateTable(
                name: "REVENUE_SPLITS",
                schema: "PAYOUT",
                columns: table => new
                {
                    REVENUE_SPLIT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    INSTRUCTOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GROSS_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PAYMENT_FEE_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PLATFORM_FEE_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    INSTRUCTOR_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PERIOD_KEY = table.Column<string>(type: "char(7)", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REVENUE_SPLITS", x => x.REVENUE_SPLIT_ID);
                });

            migrationBuilder.CreateTable(
                name: "STRIPE_WEBHOOK_EVENTS",
                schema: "COMMERCE",
                columns: table => new
                {
                    STRIPE_WEBHOOK_EVENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    STRIPE_EVENT_ID = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EVENT_TYPE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PAYLOAD_JSON = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RECEIVED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PROCESSED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PROCESS_RESULT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STRIPE_WEBHOOK_EVENTS", x => x.STRIPE_WEBHOOK_EVENT_ID);
                });

            migrationBuilder.CreateTable(
                name: "ASSIGNMENT_SUBMISSIONS",
                schema: "LEARNING",
                columns: table => new
                {
                    ASSIGNMENT_SUBMISSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ASSIGNMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    STORAGE_KEY = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NOTE = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SUBMITTED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SCORE = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FEEDBACK = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GRADED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GRADED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ASSIGNMENT_SUBMISSIONS", x => x.ASSIGNMENT_SUBMISSION_ID);
                    table.ForeignKey(
                        name: "FK_ASSIGNMENT_SUBMISSIONS_ASSIGNMENTS_ASSIGNMENT_ID",
                        column: x => x.ASSIGNMENT_ID,
                        principalSchema: "LEARNING",
                        principalTable: "ASSIGNMENTS",
                        principalColumn: "ASSIGNMENT_ID");
                });

            migrationBuilder.CreateTable(
                name: "BUNDLE_ITEMS",
                schema: "COMMERCE",
                columns: table => new
                {
                    BUNDLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUNDLE_ITEMS", x => new { x.BUNDLE_ID, x.COURSE_ID });
                    table.ForeignKey(
                        name: "FK_BUNDLE_ITEMS_BUNDLES_BUNDLE_ID",
                        column: x => x.BUNDLE_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "BUNDLES",
                        principalColumn: "BUNDLE_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CART_ITEMS",
                schema: "COMMERCE",
                columns: table => new
                {
                    CART_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CART_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ITEM_TYPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    REF_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ADDED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CART_ITEMS", x => x.CART_ITEM_ID);
                    table.ForeignKey(
                        name: "FK_CART_ITEMS_CARTS_CART_ID",
                        column: x => x.CART_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "CARTS",
                        principalColumn: "CART_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "REPORTS",
                schema: "community",
                columns: table => new
                {
                    REPORT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DISCUSSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REPORTED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RESOLVED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REPORTS", x => x.REPORT_ID);
                    table.ForeignKey(
                        name: "FK_REPORTS_DISCUSSIONS_DISCUSSION_ID",
                        column: x => x.DISCUSSION_ID,
                        principalSchema: "community",
                        principalTable: "DISCUSSIONS",
                        principalColumn: "DISCUSSION_ID");
                });

            migrationBuilder.CreateTable(
                name: "CERTIFICATES",
                schema: "LEARNING",
                columns: table => new
                {
                    CERTIFICATE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SERIAL_NO = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VERIFY_CODE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ISSUED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PDF_STORAGE_KEY = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    REVOKED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CERTIFICATES", x => x.CERTIFICATE_ID);
                    table.ForeignKey(
                        name: "FK_CERTIFICATES_ENROLLMENTS_ENROLLMENT_ID",
                        column: x => x.ENROLLMENT_ID,
                        principalSchema: "LEARNING",
                        principalTable: "ENROLLMENTS",
                        principalColumn: "ENROLLMENT_ID");
                });

            migrationBuilder.CreateTable(
                name: "EPISODE_PROGRESS",
                schema: "LEARNING",
                columns: table => new
                {
                    EPISODE_PROGRESS_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LAST_POSITION_SECONDS = table.Column<int>(type: "int", nullable: false),
                    WATCHED_SECONDS = table.Column<int>(type: "int", nullable: false),
                    IS_COMPLETED = table.Column<bool>(type: "bit", nullable: false),
                    COMPLETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EPISODE_PROGRESS", x => x.EPISODE_PROGRESS_ID);
                    table.ForeignKey(
                        name: "FK_EPISODE_PROGRESS_ENROLLMENTS_ENROLLMENT_ID",
                        column: x => x.ENROLLMENT_ID,
                        principalSchema: "LEARNING",
                        principalTable: "ENROLLMENTS",
                        principalColumn: "ENROLLMENT_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WATCH_EVENTS",
                schema: "LEARNING",
                columns: table => new
                {
                    WATCH_EVENT_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EVENT_TYPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    POSITION_SECONDS = table.Column<int>(type: "int", nullable: false),
                    OCCURRED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WATCH_EVENTS", x => x.WATCH_EVENT_ID);
                    table.ForeignKey(
                        name: "FK_WATCH_EVENTS_ENROLLMENTS_ENROLLMENT_ID",
                        column: x => x.ENROLLMENT_ID,
                        principalSchema: "LEARNING",
                        principalTable: "ENROLLMENTS",
                        principalColumn: "ENROLLMENT_ID");
                });

            migrationBuilder.CreateTable(
                name: "FLASH_SALE_ITEMS",
                schema: "COMMERCE",
                columns: table => new
                {
                    FLASH_SALE_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FLASH_SALE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SALE_PRICE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FLASH_SALE_ITEMS", x => x.FLASH_SALE_ITEM_ID);
                    table.ForeignKey(
                        name: "FK_FLASH_SALE_ITEMS_FLASH_SALES_FLASH_SALE_ID",
                        column: x => x.FLASH_SALE_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "FLASH_SALES",
                        principalColumn: "FLASH_SALE_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MEDIA_UPLOAD_SESSIONS",
                schema: "MEDIA",
                columns: table => new
                {
                    MEDIA_UPLOAD_SESSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MEDIA_ASSET_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UPLOAD_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EXPIRES_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MEDIA_UPLOAD_SESSIONS", x => x.MEDIA_UPLOAD_SESSION_ID);
                    table.ForeignKey(
                        name: "FK_MEDIA_UPLOAD_SESSIONS_MEDIA_ASSETS_MEDIA_ASSET_ID",
                        column: x => x.MEDIA_ASSET_ID,
                        principalSchema: "MEDIA",
                        principalTable: "MEDIA_ASSETS",
                        principalColumn: "MEDIA_ASSET_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ORDER_ITEMS",
                schema: "COMMERCE",
                columns: table => new
                {
                    ORDER_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TITLE_SNAPSHOT = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UNIT_PRICE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LINE_TOTAL = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDER_ITEMS", x => x.ORDER_ITEM_ID);
                    table.ForeignKey(
                        name: "FK_ORDER_ITEMS_ORDERS_ORDER_ID",
                        column: x => x.ORDER_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "ORDERS",
                        principalColumn: "ORDER_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENTS",
                schema: "COMMERCE",
                columns: table => new
                {
                    PAYMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    METHOD = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PROVIDER = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PROVIDER_PAYMENT_INTENT_ID = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SUCCEEDED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    FAILURE_REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENTS", x => x.PAYMENT_ID);
                    table.ForeignKey(
                        name: "FK_PAYMENTS_ORDERS_ORDER_ID",
                        column: x => x.ORDER_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "ORDERS",
                        principalColumn: "ORDER_ID");
                });

            migrationBuilder.CreateTable(
                name: "TAX_INVOICES",
                schema: "COMMERCE",
                columns: table => new
                {
                    TAX_INVOICE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TAX_ID_ENCRYPTED = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BUYER_NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    INVOICE_NO = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ISSUED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PDF_STORAGE_KEY = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TAX_INVOICES", x => x.TAX_INVOICE_ID);
                    table.ForeignKey(
                        name: "FK_TAX_INVOICES_ORDERS_ORDER_ID",
                        column: x => x.ORDER_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "ORDERS",
                        principalColumn: "ORDER_ID");
                });

            migrationBuilder.CreateTable(
                name: "PAYOUT_BATCH_ITEMS",
                schema: "PAYOUT",
                columns: table => new
                {
                    PAYOUT_BATCH_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BATCH_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    INSTRUCTOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WITHHOLDING_TAX_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NET_AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TRANSFER_REF = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYOUT_BATCH_ITEMS", x => x.PAYOUT_BATCH_ITEM_ID);
                    table.ForeignKey(
                        name: "FK_PAYOUT_BATCH_ITEMS_PAYOUT_BATCHES_BATCH_ID",
                        column: x => x.BATCH_ID,
                        principalSchema: "PAYOUT",
                        principalTable: "PAYOUT_BATCHES",
                        principalColumn: "PAYOUT_BATCH_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PROMO_REDEMPTIONS",
                schema: "COMMERCE",
                columns: table => new
                {
                    PROMO_REDEMPTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PROMO_CODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORDER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REDEEMED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMO_REDEMPTIONS", x => x.PROMO_REDEMPTION_ID);
                    table.ForeignKey(
                        name: "FK_PROMO_REDEMPTIONS_ORDERS_ORDER_ID",
                        column: x => x.ORDER_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "ORDERS",
                        principalColumn: "ORDER_ID");
                    table.ForeignKey(
                        name: "FK_PROMO_REDEMPTIONS_PROMO_CODES_PROMO_CODE_ID",
                        column: x => x.PROMO_CODE_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "PROMO_CODES",
                        principalColumn: "PROMO_CODE_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QUIZ_ATTEMPTS",
                schema: "LEARNING",
                columns: table => new
                {
                    QUIZ_ATTEMPT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QUIZ_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ENROLLMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ATTEMPT_NO = table.Column<int>(type: "int", nullable: false),
                    SCORE_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    IS_PASSED = table.Column<bool>(type: "bit", nullable: false),
                    STARTED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SUBMITTED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUIZ_ATTEMPTS", x => x.QUIZ_ATTEMPT_ID);
                    table.ForeignKey(
                        name: "FK_QUIZ_ATTEMPTS_QUIZZES_QUIZ_ID",
                        column: x => x.QUIZ_ID,
                        principalSchema: "LEARNING",
                        principalTable: "QUIZZES",
                        principalColumn: "QUIZ_ID");
                });

            migrationBuilder.CreateTable(
                name: "QUIZ_QUESTIONS",
                schema: "LEARNING",
                columns: table => new
                {
                    QUIZ_QUESTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QUIZ_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TYPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EXPLANATION = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    POINTS = table.Column<int>(type: "int", nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUIZ_QUESTIONS", x => x.QUIZ_QUESTION_ID);
                    table.ForeignKey(
                        name: "FK_QUIZ_QUESTIONS_QUIZZES_QUIZ_ID",
                        column: x => x.QUIZ_ID,
                        principalSchema: "LEARNING",
                        principalTable: "QUIZZES",
                        principalColumn: "QUIZ_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_OPS_QUEUE",
                schema: "COMMERCE",
                columns: table => new
                {
                    PAYMENT_OPS_QUEUE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PAYMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ASSIGNED_TO_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RESOLVED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RESOLVED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    NOTE = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_OPS_QUEUE", x => x.PAYMENT_OPS_QUEUE_ID);
                    table.ForeignKey(
                        name: "FK_PAYMENT_OPS_QUEUE_PAYMENTS_PAYMENT_ID",
                        column: x => x.PAYMENT_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "PAYMENTS",
                        principalColumn: "PAYMENT_ID");
                });

            migrationBuilder.CreateTable(
                name: "REFUNDS",
                schema: "COMMERCE",
                columns: table => new
                {
                    REFUND_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PAYMENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AMOUNT = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    REQUESTED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REQUESTED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    DECIDED_BY_USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DECIDED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DECISION_NOTE = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    STRIPE_REFUND_ID = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    COMPLETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REFUNDS", x => x.REFUND_ID);
                    table.ForeignKey(
                        name: "FK_REFUNDS_PAYMENTS_PAYMENT_ID",
                        column: x => x.PAYMENT_ID,
                        principalSchema: "COMMERCE",
                        principalTable: "PAYMENTS",
                        principalColumn: "PAYMENT_ID");
                });

            migrationBuilder.CreateTable(
                name: "QUIZ_ATTEMPT_ANSWERS",
                schema: "LEARNING",
                columns: table => new
                {
                    QUIZ_ATTEMPT_ANSWER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ATTEMPT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QUESTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SELECTED_OPTION_IDS = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IS_CORRECT = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUIZ_ATTEMPT_ANSWERS", x => x.QUIZ_ATTEMPT_ANSWER_ID);
                    table.ForeignKey(
                        name: "FK_QUIZ_ATTEMPT_ANSWERS_QUIZ_ATTEMPTS_ATTEMPT_ID",
                        column: x => x.ATTEMPT_ID,
                        principalSchema: "LEARNING",
                        principalTable: "QUIZ_ATTEMPTS",
                        principalColumn: "QUIZ_ATTEMPT_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QUIZ_OPTIONS",
                schema: "LEARNING",
                columns: table => new
                {
                    QUIZ_OPTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QUESTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IS_CORRECT = table.Column<bool>(type: "bit", nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUIZ_OPTIONS", x => x.QUIZ_OPTION_ID);
                    table.ForeignKey(
                        name: "FK_QUIZ_OPTIONS_QUIZ_QUESTIONS_QUESTION_ID",
                        column: x => x.QUESTION_ID,
                        principalSchema: "LEARNING",
                        principalTable: "QUIZ_QUESTIONS",
                        principalColumn: "QUIZ_QUESTION_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ASSIGNMENT_SUBMISSIONS_ASSIGNMENT_ID_STATUS",
                schema: "LEARNING",
                table: "ASSIGNMENT_SUBMISSIONS",
                columns: new[] { "ASSIGNMENT_ID", "STATUS" });

            migrationBuilder.CreateIndex(
                name: "IX_ASSIGNMENT_SUBMISSIONS_ENROLLMENT_ID",
                schema: "LEARNING",
                table: "ASSIGNMENT_SUBMISSIONS",
                column: "ENROLLMENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ASSIGNMENTS_EPISODE_ID",
                schema: "LEARNING",
                table: "ASSIGNMENTS",
                column: "EPISODE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BANNERS_PLACEMENT_IS_ACTIVE_SORT_ORDER",
                schema: "cms",
                table: "BANNERS",
                columns: new[] { "PLACEMENT", "IS_ACTIVE", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_BUNDLES_SLUG",
                schema: "COMMERCE",
                table: "BUNDLES",
                column: "SLUG",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CART_ITEMS_CART_ID",
                schema: "COMMERCE",
                table: "CART_ITEMS",
                column: "CART_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CARTS_USER_ID",
                schema: "COMMERCE",
                table: "CARTS",
                column: "USER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CERTIFICATES_ENROLLMENT_ID",
                schema: "LEARNING",
                table: "CERTIFICATES",
                column: "ENROLLMENT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CERTIFICATES_SERIAL_NO",
                schema: "LEARNING",
                table: "CERTIFICATES",
                column: "SERIAL_NO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CERTIFICATES_VERIFY_CODE",
                schema: "LEARNING",
                table: "CERTIFICATES",
                column: "VERIFY_CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DAILY_COURSE_STATS_COURSE_ID_DATE",
                schema: "ANALYTICS",
                table: "DAILY_COURSE_STATS",
                columns: new[] { "COURSE_ID", "DATE" });

            migrationBuilder.CreateIndex(
                name: "IX_DISCUSSIONS_EPISODE_ID_STATUS_CREATED_AT_UTC",
                schema: "community",
                table: "DISCUSSIONS",
                columns: new[] { "EPISODE_ID", "STATUS", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_DISCUSSIONS_PARENT_ID",
                schema: "community",
                table: "DISCUSSIONS",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ENROLLMENTS_COURSE_ID_STATUS",
                schema: "LEARNING",
                table: "ENROLLMENTS",
                columns: new[] { "COURSE_ID", "STATUS" });

            migrationBuilder.CreateIndex(
                name: "IX_ENROLLMENTS_MY_COURSES",
                schema: "LEARNING",
                table: "ENROLLMENTS",
                columns: new[] { "USER_ID", "STATUS" })
                .Annotation("SqlServer:Include", new[] { "COURSE_ID", "PROGRESS_PERCENT", "LAST_ACCESSED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_ENROLLMENTS_USER_ID_COURSE_ID",
                schema: "LEARNING",
                table: "ENROLLMENTS",
                columns: new[] { "USER_ID", "COURSE_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EPISODE_DROP_OFF_EPISODE_ID_DATE",
                schema: "ANALYTICS",
                table: "EPISODE_DROP_OFF",
                columns: new[] { "EPISODE_ID", "DATE" });

            migrationBuilder.CreateIndex(
                name: "IX_EPISODE_PROGRESS_RESUME",
                schema: "LEARNING",
                table: "EPISODE_PROGRESS",
                columns: new[] { "ENROLLMENT_ID", "EPISODE_ID" },
                unique: true)
                .Annotation("SqlServer:Include", new[] { "LAST_POSITION_SECONDS", "IS_COMPLETED" });

            migrationBuilder.CreateIndex(
                name: "IX_FLASH_SALE_ITEMS_FLASH_SALE_ID_COURSE_ID",
                schema: "COMMERCE",
                table: "FLASH_SALE_ITEMS",
                columns: new[] { "FLASH_SALE_ID", "COURSE_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_INSTRUCTOR_PAYOUT_ACCOUNTS_INSTRUCTOR_ID",
                schema: "PAYOUT",
                table: "INSTRUCTOR_PAYOUT_ACCOUNTS",
                column: "INSTRUCTOR_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MEDIA_ASSETS_PROVIDER_PROVIDER_ASSET_ID",
                schema: "MEDIA",
                table: "MEDIA_ASSETS",
                columns: new[] { "PROVIDER", "PROVIDER_ASSET_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MEDIA_UPLOAD_SESSIONS_MEDIA_ASSET_ID",
                schema: "MEDIA",
                table: "MEDIA_UPLOAD_SESSIONS",
                column: "MEDIA_ASSET_ID");

            migrationBuilder.CreateIndex(
                name: "IX_MENU_ITEMS_PARENT_ID_SORT_ORDER",
                schema: "cms",
                table: "MENU_ITEMS",
                columns: new[] { "PARENT_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_ITEMS_ORDER_ID",
                schema: "COMMERCE",
                table: "ORDER_ITEMS",
                column: "ORDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ORDERS_ORDER_NO",
                schema: "COMMERCE",
                table: "ORDERS",
                column: "ORDER_NO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ORDERS_USER_HISTORY",
                schema: "COMMERCE",
                table: "ORDERS",
                columns: new[] { "USER_ID", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_OPS_QUEUE_PAYMENT_ID",
                schema: "COMMERCE",
                table: "PAYMENT_OPS_QUEUE",
                column: "PAYMENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_ORDER_ID",
                schema: "COMMERCE",
                table: "PAYMENTS",
                column: "ORDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_PROVIDER_PAYMENT_INTENT_ID",
                schema: "COMMERCE",
                table: "PAYMENTS",
                column: "PROVIDER_PAYMENT_INTENT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_BATCH_ITEMS_BATCH_ID",
                schema: "PAYOUT",
                table: "PAYOUT_BATCH_ITEMS",
                column: "BATCH_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_BATCH_ITEMS_INSTRUCTOR_ID",
                schema: "PAYOUT",
                table: "PAYOUT_BATCH_ITEMS",
                column: "INSTRUCTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_BATCHES_PERIOD_KEY",
                schema: "PAYOUT",
                table: "PAYOUT_BATCHES",
                column: "PERIOD_KEY");

            migrationBuilder.CreateIndex(
                name: "IX_PLAYBACK_SESSIONS_USER_ID_ISSUED_AT_UTC",
                schema: "MEDIA",
                table: "PLAYBACK_SESSIONS",
                columns: new[] { "USER_ID", "ISSUED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_POSTS_SLUG",
                schema: "cms",
                table: "POSTS",
                column: "SLUG",
                unique: true,
                filter: "[IS_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSTS_STATUS_PUBLISHED_AT_UTC",
                schema: "cms",
                table: "POSTS",
                columns: new[] { "STATUS", "PUBLISHED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_PROMO_CODES_CODE",
                schema: "COMMERCE",
                table: "PROMO_CODES",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROMO_REDEMPTIONS_ORDER_ID",
                schema: "COMMERCE",
                table: "PROMO_REDEMPTIONS",
                column: "ORDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_ORDER_ID",
                schema: "COMMERCE",
                table: "PROMO_REDEMPTIONS",
                columns: new[] { "PROMO_CODE_ID", "ORDER_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QUIZ_ATTEMPT_ANSWERS_ATTEMPT_ID_QUESTION_ID",
                schema: "LEARNING",
                table: "QUIZ_ATTEMPT_ANSWERS",
                columns: new[] { "ATTEMPT_ID", "QUESTION_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QUIZ_ATTEMPTS_QUIZ_ID_ENROLLMENT_ID_ATTEMPT_NO",
                schema: "LEARNING",
                table: "QUIZ_ATTEMPTS",
                columns: new[] { "QUIZ_ID", "ENROLLMENT_ID", "ATTEMPT_NO" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QUIZ_OPTIONS_QUESTION_ID_SORT_ORDER",
                schema: "LEARNING",
                table: "QUIZ_OPTIONS",
                columns: new[] { "QUESTION_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_QUIZ_QUESTIONS_QUIZ_ID_SORT_ORDER",
                schema: "LEARNING",
                table: "QUIZ_QUESTIONS",
                columns: new[] { "QUIZ_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_QUIZZES_EPISODE_ID",
                schema: "LEARNING",
                table: "QUIZZES",
                column: "EPISODE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REDIRECTS_FROM_PATH",
                schema: "cms",
                table: "REDIRECTS",
                column: "FROM_PATH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REFUNDS_PAYMENT_ID",
                schema: "COMMERCE",
                table: "REFUNDS",
                column: "PAYMENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REFUNDS_REQUESTED_BY_USER",
                schema: "COMMERCE",
                table: "REFUNDS",
                columns: new[] { "REQUESTED_BY_USER_ID", "REQUESTED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_REFUNDS_STRIPE_REFUND_ID",
                schema: "COMMERCE",
                table: "REFUNDS",
                column: "STRIPE_REFUND_ID",
                unique: true,
                filter: "[STRIPE_REFUND_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_REPORTS_DISCUSSION_ID",
                schema: "community",
                table: "REPORTS",
                column: "DISCUSSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REPORTS_STATUS_CREATED_AT_UTC",
                schema: "community",
                table: "REPORTS",
                columns: new[] { "STATUS", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_REVENUE_SPLITS_ORDER_ITEM_ID",
                schema: "PAYOUT",
                table: "REVENUE_SPLITS",
                column: "ORDER_ITEM_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REVENUE_SPLITS_PAYOUT",
                schema: "PAYOUT",
                table: "REVENUE_SPLITS",
                columns: new[] { "INSTRUCTOR_ID", "PERIOD_KEY", "STATUS" });

            migrationBuilder.CreateIndex(
                name: "IX_STRIPE_WEBHOOK_EVENTS_STRIPE_EVENT_ID",
                schema: "COMMERCE",
                table: "STRIPE_WEBHOOK_EVENTS",
                column: "STRIPE_EVENT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAX_INVOICES_INVOICE_NO",
                schema: "COMMERCE",
                table: "TAX_INVOICES",
                column: "INVOICE_NO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAX_INVOICES_ORDER_ID",
                schema: "COMMERCE",
                table: "TAX_INVOICES",
                column: "ORDER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WATCH_EVENTS_ENROLLMENT_ID_OCCURRED_AT_UTC",
                schema: "LEARNING",
                table: "WATCH_EVENTS",
                columns: new[] { "ENROLLMENT_ID", "OCCURRED_AT_UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ASSIGNMENT_SUBMISSIONS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "BANNERS",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "BUNDLE_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "CART_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "CERTIFICATES",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "DAILY_COURSE_STATS",
                schema: "ANALYTICS");

            migrationBuilder.DropTable(
                name: "EPISODE_DROP_OFF",
                schema: "ANALYTICS");

            migrationBuilder.DropTable(
                name: "EPISODE_PROGRESS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "FLASH_SALE_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "INSTRUCTOR_PAYOUT_ACCOUNTS",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "MEDIA_UPLOAD_SESSIONS",
                schema: "MEDIA");

            migrationBuilder.DropTable(
                name: "MENU_ITEMS",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "ORDER_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "PAYMENT_OPS_QUEUE",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "PAYOUT_BATCH_ITEMS",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "PLAYBACK_SESSIONS",
                schema: "MEDIA");

            migrationBuilder.DropTable(
                name: "POSTS",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "PROMO_REDEMPTIONS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "QUIZ_ATTEMPT_ANSWERS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "QUIZ_OPTIONS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "REDIRECTS",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "REFUNDS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "REPORTS",
                schema: "community");

            migrationBuilder.DropTable(
                name: "REVENUE_SPLITS",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "STRIPE_WEBHOOK_EVENTS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "TAX_INVOICES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "WATCH_EVENTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "ASSIGNMENTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "BUNDLES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "CARTS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "FLASH_SALES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "MEDIA_ASSETS",
                schema: "MEDIA");

            migrationBuilder.DropTable(
                name: "PAYOUT_BATCHES",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "PROMO_CODES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "QUIZ_ATTEMPTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "QUIZ_QUESTIONS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "PAYMENTS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "DISCUSSIONS",
                schema: "community");

            migrationBuilder.DropTable(
                name: "ENROLLMENTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "QUIZZES",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "ORDERS",
                schema: "COMMERCE");
        }
    }
}
