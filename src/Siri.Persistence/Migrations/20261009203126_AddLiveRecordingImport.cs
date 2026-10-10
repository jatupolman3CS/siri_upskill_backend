using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveRecordingImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ACCOUNT_KIND_CHECKED_AT_UTC",
                schema: "LIVE",
                table: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                type: "timestamp(3) with time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HOSTED_DOMAIN",
                schema: "LIVE",
                table: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SESSION_RECORDING_IMPORTS",
                schema: "LIVE",
                columns: table => new
                {
                    SESSION_RECORDING_IMPORT_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    INSTRUCTOR_USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    STATUS = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ATTEMPTS = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NEXT_ATTEMPT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    LEASE_UNTIL_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    GOOGLE_RECORDING_NAME = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GOOGLE_FILE_ID = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MEDIA_ASSET_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    EPISODE_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    ERROR_CODE = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    SEARCH_UNTIL_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    COMPLETED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "bytea", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SESSION_RECORDING_IMPORTS", x => x.SESSION_RECORDING_IMPORT_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_RECORDING_IMPORTS_DUE",
                schema: "LIVE",
                table: "SESSION_RECORDING_IMPORTS",
                columns: new[] { "STATUS", "NEXT_ATTEMPT_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_RECORDING_IMPORTS_SESSION_ID",
                schema: "LIVE",
                table: "SESSION_RECORDING_IMPORTS",
                column: "SESSION_ID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SESSION_RECORDING_IMPORTS",
                schema: "LIVE");

            migrationBuilder.DropColumn(
                name: "ACCOUNT_KIND_CHECKED_AT_UTC",
                schema: "LIVE",
                table: "INSTRUCTOR_GOOGLE_ACCOUNTS");

            migrationBuilder.DropColumn(
                name: "HOSTED_DOMAIN",
                schema: "LIVE",
                table: "INSTRUCTOR_GOOGLE_ACCOUNTS");
        }
    }
}
