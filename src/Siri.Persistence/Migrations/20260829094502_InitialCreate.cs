using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "NOTIFY");

            migrationBuilder.EnsureSchema(
                name: "LEARNING");

            migrationBuilder.EnsureSchema(
                name: "CMS");

            migrationBuilder.EnsureSchema(
                name: "COMMERCE");

            migrationBuilder.EnsureSchema(
                name: "CATALOG");

            migrationBuilder.EnsureSchema(
                name: "ANALYTICS");

            migrationBuilder.EnsureSchema(
                name: "COMMUNITY");

            migrationBuilder.EnsureSchema(
                name: "PAYOUT");

            migrationBuilder.EnsureSchema(
                name: "MEDIA");

            migrationBuilder.EnsureSchema(
                name: "IDENTITY");

            migrationBuilder.CreateTable(
                name: "ANNOUNCEMENTS",
                schema: "NOTIFY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    INSTRUCTOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BODY = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SEND_EMAIL = table.Column<bool>(type: "bit", nullable: false),
                    SCHEDULED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    SENT_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RECIPIENT_COUNT = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ANNOUNCEMENTS", x => x.ID);
                });

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
                schema: "CMS",
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
                name: "CATEGORIES",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PARENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SLUG = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NAME_TH = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NAME_EN = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ICON_KEY = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATEGORIES", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CATEGORIES_CATEGORIES_PARENT_ID",
                        column: x => x.PARENT_ID,
                        principalSchema: "CATALOG",
                        principalTable: "CATEGORIES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CONTACT_MESSAGES",
                schema: "NOTIFY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SUBJECT = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MESSAGE = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RESOLVED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RESOLVED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ADMIN_NOTES = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DELETED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTACT_MESSAGES", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "COURSE_REVIEWS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RATING = table.Column<int>(type: "int", nullable: false),
                    COMMENT = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IS_PUBLISHED = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_REVIEWS", x => x.ID);
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
                schema: "COMMUNITY",
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
                        principalSchema: "COMMUNITY",
                        principalTable: "DISCUSSIONS",
                        principalColumn: "DISCUSSION_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EMAIL_OUTBOX",
                schema: "NOTIFY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TO_EMAIL = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SUBJECT = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BODY_HTML = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TEMPLATE_KEY = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ATTEMPTS = table.Column<int>(type: "int", nullable: false),
                    NEXT_RETRY_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    SENT_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LAST_ERROR = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMAIL_OUTBOX", x => x.ID);
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
                name: "FEATURE_FLAGS",
                schema: "CMS",
                columns: table => new
                {
                    FEATURE_FLAG_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KEY = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IS_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FEATURE_FLAGS", x => x.FEATURE_FLAG_ID);
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
                    TAX_ID = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TAX_PAYER_TYPE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
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
                name: "INSTRUCTOR_PROFILES",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DISPLAY_NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HEADLINE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BIO = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AVATAR_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    REVENUE_SHARE_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    APPROVED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INSTRUCTOR_PROFILES", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LEARNING_PATHS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SLUG = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEARNING_PATHS", x => x.ID);
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
                schema: "CMS",
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
                        principalSchema: "CMS",
                        principalTable: "MENU_ITEMS",
                        principalColumn: "MENU_ITEM_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATIONS",
                schema: "NOTIFY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TYPE = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BODY = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LINK_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    READ_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATIONS", x => x.ID);
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
                schema: "CMS",
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
                schema: "CMS",
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
                    REVENUE_SHARE_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PERIOD_KEY = table.Column<string>(type: "char(7)", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PAYOUT_BATCH_ITEM_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                name: "ROLES",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLES", x => x.ID);
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
                name: "USERS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NORMALIZED_EMAIL = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DISPLAY_NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AVATAR_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PHONE_NUMBER = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EMAIL_CONFIRMED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    TWO_FACTOR_ENABLED = table.Column<bool>(type: "bit", nullable: false),
                    LAST_LOGIN_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    MAX_CONCURRENT_SESSIONS_OVERRIDE = table.Column<int>(type: "int", nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "WISHLISTS",
                schema: "CATALOG",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WISHLISTS", x => new { x.USER_ID, x.COURSE_ID });
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
                schema: "COMMUNITY",
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
                        principalSchema: "COMMUNITY",
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
                name: "COURSES",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SLUG = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SUBTITLE = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    INSTRUCTOR_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CATEGORY_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LEVEL = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LANGUAGE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    THUMBNAIL_URL = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TRAILER_MEDIA_ASSET_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PRICE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    COMPARE_PRICE = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CURRENCY = table.Column<string>(type: "char(3)", nullable: false),
                    ACCESS_DURATION_DAYS = table.Column<int>(type: "int", nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PUBLISHED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    REJECTION_REASON = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TOTAL_DURATION_SECONDS = table.Column<int>(type: "int", nullable: false),
                    EPISODE_COUNT = table.Column<int>(type: "int", nullable: false),
                    RATING_AVERAGE = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: false),
                    RATING_COUNT = table.Column<int>(type: "int", nullable: false),
                    ENROLLMENT_COUNT = table.Column<int>(type: "int", nullable: false),
                    SEO_TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SEO_DESCRIPTION = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                    DELETED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSES", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSES_INSTRUCTOR_PROFILES_INSTRUCTOR_ID",
                        column: x => x.INSTRUCTOR_ID,
                        principalSchema: "CATALOG",
                        principalTable: "INSTRUCTOR_PROFILES",
                        principalColumn: "ID");
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
                    WITHHOLDING_TAX_PERCENT = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
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
                name: "SECURITY_AUDITS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EVENT_TYPE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DETAIL = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IP_ADDRESS = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OCCURRED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SECURITY_AUDITS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SECURITY_AUDITS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLES",
                schema: "IDENTITY",
                columns: table => new
                {
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ROLE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLES", x => new { x.USER_ID, x.ROLE_ID });
                    table.ForeignKey(
                        name: "FK_USER_ROLES_ROLES_ROLE_ID",
                        column: x => x.ROLE_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "ROLES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_USER_ROLES_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "USER_SECURITY_TOKENS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TOKEN_HASH = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PURPOSE = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EXPIRES_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CONSUMED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SECURITY_TOKENS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USER_SECURITY_TOKENS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "USER_SESSIONS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DEVICE_ID = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DEVICE_NAME = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    USER_AGENT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IP_ADDRESS = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LAST_SEEN_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    REVOKED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    REVOKE_REASON = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SESSIONS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USER_SESSIONS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "COURSE_OUTCOMES",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_OUTCOMES", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSE_OUTCOMES_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "COURSE_REQUIREMENTS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TEXT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_REQUIREMENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSE_REQUIREMENTS_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "COURSE_SECTIONS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_SECTIONS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSE_SECTIONS_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LEARNING_PATH_ITEMS",
                schema: "CATALOG",
                columns: table => new
                {
                    PATH_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LEARNING_PATH_ITEMS", x => new { x.PATH_ID, x.COURSE_ID });
                    table.ForeignKey(
                        name: "FK_LEARNING_PATH_ITEMS_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LEARNING_PATH_ITEMS_LEARNING_PATHS_PATH_ID",
                        column: x => x.PATH_ID,
                        principalSchema: "CATALOG",
                        principalTable: "LEARNING_PATHS",
                        principalColumn: "ID",
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

            migrationBuilder.CreateTable(
                name: "REFRESH_TOKENS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TOKEN_HASH = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EXPIRES_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    REVOKED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    REPLACED_BY_TOKEN_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REFRESH_TOKENS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_REFRESH_TOKENS_REFRESH_TOKENS_REPLACED_BY_TOKEN_ID",
                        column: x => x.REPLACED_BY_TOKEN_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "REFRESH_TOKENS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REFRESH_TOKENS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REFRESH_TOKENS_USER_SESSIONS_SESSION_ID",
                        column: x => x.SESSION_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USER_SESSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "COURSE_EPISODES",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SECTION_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TITLE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SORT_ORDER = table.Column<int>(type: "int", nullable: false),
                    MEDIA_ASSET_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DURATION_SECONDS = table.Column<int>(type: "int", nullable: true),
                    IS_FREE_PREVIEW = table.Column<bool>(type: "bit", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_EPISODES", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSE_EPISODES_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_COURSE_EPISODES_COURSE_SECTIONS_SECTION_ID",
                        column: x => x.SECTION_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSE_SECTIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EPISODE_ATTACHMENTS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EPISODE_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FILE_NAME = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    STORAGE_KEY = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CONTENT_TYPE = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SIZE_BYTES = table.Column<long>(type: "bigint", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EPISODE_ATTACHMENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_EPISODE_ATTACHMENTS_COURSE_EPISODES_EPISODE_ID",
                        column: x => x.EPISODE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSE_EPISODES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "IDENTITY",
                table: "ROLES",
                columns: new[] { "ID", "NAME" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "Learner" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "Instructor" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Admin" },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "SuperAdmin" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ANNOUNCEMENTS_COURSE_ID",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS",
                column: "COURSE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ANNOUNCEMENTS_INSTRUCTOR_ID",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS",
                column: "INSTRUCTOR_ID");

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
                schema: "CMS",
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
                name: "IX_CATEGORIES_PARENT_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "CATEGORIES",
                columns: new[] { "PARENT_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORIES_SLUG",
                schema: "CATALOG",
                table: "CATEGORIES",
                column: "SLUG",
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
                name: "IX_CONTACT_MESSAGES_CREATED_AT_UTC",
                schema: "NOTIFY",
                table: "CONTACT_MESSAGES",
                column: "CREATED_AT_UTC");

            migrationBuilder.CreateIndex(
                name: "IX_CONTACT_MESSAGES_IS_DELETED",
                schema: "NOTIFY",
                table: "CONTACT_MESSAGES",
                column: "IS_DELETED");

            migrationBuilder.CreateIndex(
                name: "IX_CONTACT_MESSAGES_STATUS",
                schema: "NOTIFY",
                table: "CONTACT_MESSAGES",
                column: "STATUS");

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_EPISODES_COURSE_ID",
                schema: "CATALOG",
                table: "COURSE_EPISODES",
                column: "COURSE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_EPISODES_SECTION_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "COURSE_EPISODES",
                columns: new[] { "SECTION_ID", "SORT_ORDER" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_OUTCOMES_COURSE_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "COURSE_OUTCOMES",
                columns: new[] { "COURSE_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_REQUIREMENTS_COURSE_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "COURSE_REQUIREMENTS",
                columns: new[] { "COURSE_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID",
                schema: "CATALOG",
                table: "COURSE_REVIEWS",
                column: "COURSE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID_USER_ID",
                schema: "CATALOG",
                table: "COURSE_REVIEWS",
                columns: new[] { "COURSE_ID", "USER_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_SECTIONS_COURSE_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "COURSE_SECTIONS",
                columns: new[] { "COURSE_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_COURSES_INSTRUCTOR_ID",
                schema: "CATALOG",
                table: "COURSES",
                column: "INSTRUCTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_COURSES_SLUG",
                schema: "CATALOG",
                table: "COURSES",
                column: "SLUG",
                unique: true,
                filter: "[IS_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_COURSES_STATUS_CATEGORY_ID_PUBLISHED_AT_UTC",
                schema: "CATALOG",
                table: "COURSES",
                columns: new[] { "STATUS", "CATEGORY_ID", "PUBLISHED_AT_UTC" })
                .Annotation("SqlServer:Include", new[] { "TITLE", "SLUG", "PRICE", "RATING_AVERAGE", "THUMBNAIL_URL" });

            migrationBuilder.CreateIndex(
                name: "IX_DAILY_COURSE_STATS_COURSE_ID_DATE",
                schema: "ANALYTICS",
                table: "DAILY_COURSE_STATS",
                columns: new[] { "COURSE_ID", "DATE" });

            migrationBuilder.CreateIndex(
                name: "IX_DISCUSSIONS_EPISODE_ID_STATUS_CREATED_AT_UTC",
                schema: "COMMUNITY",
                table: "DISCUSSIONS",
                columns: new[] { "EPISODE_ID", "STATUS", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_DISCUSSIONS_PARENT_ID",
                schema: "COMMUNITY",
                table: "DISCUSSIONS",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_EMAIL_OUTBOX_STATUS_NEXT_RETRY_AT_UTC",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX",
                columns: new[] { "STATUS", "NEXT_RETRY_AT_UTC" });

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
                name: "IX_EPISODE_ATTACHMENTS_EPISODE_ID",
                schema: "CATALOG",
                table: "EPISODE_ATTACHMENTS",
                column: "EPISODE_ID");

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
                name: "IX_FEATURE_FLAGS_KEY",
                schema: "CMS",
                table: "FEATURE_FLAGS",
                column: "KEY",
                unique: true);

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
                name: "IX_INSTRUCTOR_PROFILES_USER_ID",
                schema: "CATALOG",
                table: "INSTRUCTOR_PROFILES",
                column: "USER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LEARNING_PATH_ITEMS_COURSE_ID",
                schema: "CATALOG",
                table: "LEARNING_PATH_ITEMS",
                column: "COURSE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LEARNING_PATH_ITEMS_PATH_ID_SORT_ORDER",
                schema: "CATALOG",
                table: "LEARNING_PATH_ITEMS",
                columns: new[] { "PATH_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_LEARNING_PATHS_IS_ACTIVE_SORT_ORDER",
                schema: "CATALOG",
                table: "LEARNING_PATHS",
                columns: new[] { "IS_ACTIVE", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_LEARNING_PATHS_SLUG",
                schema: "CATALOG",
                table: "LEARNING_PATHS",
                column: "SLUG",
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
                schema: "CMS",
                table: "MENU_ITEMS",
                columns: new[] { "PARENT_ID", "SORT_ORDER" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_ID_CREATED_AT_UTC",
                schema: "NOTIFY",
                table: "NOTIFICATIONS",
                columns: new[] { "USER_ID", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_ID_READ_AT_UTC",
                schema: "NOTIFY",
                table: "NOTIFICATIONS",
                columns: new[] { "USER_ID", "READ_AT_UTC" });

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
                schema: "CMS",
                table: "POSTS",
                column: "SLUG",
                unique: true,
                filter: "[IS_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSTS_STATUS_PUBLISHED_AT_UTC",
                schema: "CMS",
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
                name: "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_USER_ID",
                schema: "COMMERCE",
                table: "PROMO_REDEMPTIONS",
                columns: new[] { "PROMO_CODE_ID", "USER_ID" });

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
                schema: "CMS",
                table: "REDIRECTS",
                column: "FROM_PATH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKENS_REPLACED_BY_TOKEN_ID",
                schema: "IDENTITY",
                table: "REFRESH_TOKENS",
                column: "REPLACED_BY_TOKEN_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKENS_SESSION_ID",
                schema: "IDENTITY",
                table: "REFRESH_TOKENS",
                column: "SESSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKENS_TOKEN_HASH",
                schema: "IDENTITY",
                table: "REFRESH_TOKENS",
                column: "TOKEN_HASH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKENS_USER_ID",
                schema: "IDENTITY",
                table: "REFRESH_TOKENS",
                column: "USER_ID");

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
                schema: "COMMUNITY",
                table: "REPORTS",
                column: "DISCUSSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REPORTS_STATUS_CREATED_AT_UTC",
                schema: "COMMUNITY",
                table: "REPORTS",
                columns: new[] { "STATUS", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_REVENUE_SPLITS_ORDER_ITEM_ID",
                schema: "PAYOUT",
                table: "REVENUE_SPLITS",
                column: "ORDER_ITEM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REVENUE_SPLITS_PAYOUT",
                schema: "PAYOUT",
                table: "REVENUE_SPLITS",
                columns: new[] { "INSTRUCTOR_ID", "PERIOD_KEY", "STATUS" });

            migrationBuilder.CreateIndex(
                name: "IX_REVENUE_SPLITS_PAYOUT_BATCH_ITEM_ID",
                schema: "PAYOUT",
                table: "REVENUE_SPLITS",
                column: "PAYOUT_BATCH_ITEM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ROLES_NAME",
                schema: "IDENTITY",
                table: "ROLES",
                column: "NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SECURITY_AUDITS_USER_ID",
                schema: "IDENTITY",
                table: "SECURITY_AUDITS",
                column: "USER_ID");

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
                name: "IX_USER_ROLES_ROLE_ID",
                schema: "IDENTITY",
                table: "USER_ROLES",
                column: "ROLE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SECURITY_TOKENS_ACTIVE",
                schema: "IDENTITY",
                table: "USER_SECURITY_TOKENS",
                columns: new[] { "USER_ID", "PURPOSE" },
                filter: "[CONSUMED_AT_UTC] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SECURITY_TOKENS_TOKEN_HASH",
                schema: "IDENTITY",
                table: "USER_SECURITY_TOKENS",
                column: "TOKEN_HASH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_SESSIONS_ACTIVE",
                schema: "IDENTITY",
                table: "USER_SESSIONS",
                column: "USER_ID",
                filter: "[REVOKED_AT_UTC] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SESSIONS_USER_ID_REVOKED_AT_UTC",
                schema: "IDENTITY",
                table: "USER_SESSIONS",
                columns: new[] { "USER_ID", "REVOKED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_USERS_NORMALIZED_EMAIL",
                schema: "IDENTITY",
                table: "USERS",
                column: "NORMALIZED_EMAIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WATCH_EVENTS_ENROLLMENT_ID_OCCURRED_AT_UTC",
                schema: "LEARNING",
                table: "WATCH_EVENTS",
                columns: new[] { "ENROLLMENT_ID", "OCCURRED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_WISHLISTS_USER_ID",
                schema: "CATALOG",
                table: "WISHLISTS",
                column: "USER_ID");

            migrationBuilder.Sql(
                """
                IF SERVERPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'COURSE_FULL_TEXT_CATALOG')
                    BEGIN
                        CREATE FULLTEXT CATALOG COURSE_FULL_TEXT_CATALOG AS DEFAULT;
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('CATALOG.COURSES'))
                    BEGIN
                        CREATE FULLTEXT INDEX ON CATALOG.COURSES(TITLE, SUBTITLE, DESCRIPTION)
                        KEY INDEX PK_COURSES
                        ON COURSE_FULL_TEXT_CATALOG
                        WITH CHANGE_TRACKING AUTO;
                    END
                END
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF SERVERPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('CATALOG.COURSES'))
                    BEGIN
                        DROP FULLTEXT INDEX ON CATALOG.COURSES;
                    END

                    IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'COURSE_FULL_TEXT_CATALOG')
                    BEGIN
                        DROP FULLTEXT CATALOG COURSE_FULL_TEXT_CATALOG;
                    END
                END
                """,
                suppressTransaction: true);

            migrationBuilder.DropTable(
                name: "ANNOUNCEMENTS",
                schema: "NOTIFY");

            migrationBuilder.DropTable(
                name: "ASSIGNMENT_SUBMISSIONS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "BANNERS",
                schema: "CMS");

            migrationBuilder.DropTable(
                name: "BUNDLE_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "CART_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "CATEGORIES",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "CERTIFICATES",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "CONTACT_MESSAGES",
                schema: "NOTIFY");

            migrationBuilder.DropTable(
                name: "COURSE_OUTCOMES",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "COURSE_REQUIREMENTS",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "COURSE_REVIEWS",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "DAILY_COURSE_STATS",
                schema: "ANALYTICS");

            migrationBuilder.DropTable(
                name: "EMAIL_OUTBOX",
                schema: "NOTIFY");

            migrationBuilder.DropTable(
                name: "EPISODE_ATTACHMENTS",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "EPISODE_DROP_OFF",
                schema: "ANALYTICS");

            migrationBuilder.DropTable(
                name: "EPISODE_PROGRESS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "FEATURE_FLAGS",
                schema: "CMS");

            migrationBuilder.DropTable(
                name: "FLASH_SALE_ITEMS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "INSTRUCTOR_PAYOUT_ACCOUNTS",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "LEARNING_PATH_ITEMS",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "MEDIA_UPLOAD_SESSIONS",
                schema: "MEDIA");

            migrationBuilder.DropTable(
                name: "MENU_ITEMS",
                schema: "CMS");

            migrationBuilder.DropTable(
                name: "NOTIFICATIONS",
                schema: "NOTIFY");

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
                schema: "CMS");

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
                schema: "CMS");

            migrationBuilder.DropTable(
                name: "REFRESH_TOKENS",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "REFUNDS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "REPORTS",
                schema: "COMMUNITY");

            migrationBuilder.DropTable(
                name: "REVENUE_SPLITS",
                schema: "PAYOUT");

            migrationBuilder.DropTable(
                name: "SECURITY_AUDITS",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "STRIPE_WEBHOOK_EVENTS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "TAX_INVOICES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "USER_ROLES",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "USER_SECURITY_TOKENS",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "WATCH_EVENTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "WISHLISTS",
                schema: "CATALOG");

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
                name: "COURSE_EPISODES",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "FLASH_SALES",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "LEARNING_PATHS",
                schema: "CATALOG");

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
                name: "USER_SESSIONS",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "PAYMENTS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "DISCUSSIONS",
                schema: "COMMUNITY");

            migrationBuilder.DropTable(
                name: "ROLES",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "ENROLLMENTS",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "COURSE_SECTIONS",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "QUIZZES",
                schema: "LEARNING");

            migrationBuilder.DropTable(
                name: "USERS",
                schema: "IDENTITY");

            migrationBuilder.DropTable(
                name: "ORDERS",
                schema: "COMMERCE");

            migrationBuilder.DropTable(
                name: "COURSES",
                schema: "CATALOG");

            migrationBuilder.DropTable(
                name: "INSTRUCTOR_PROFILES",
                schema: "CATALOG");
        }
    }
}
