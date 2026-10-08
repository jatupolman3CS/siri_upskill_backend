using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "LIVE");

            migrationBuilder.CreateTable(
                name: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                schema: "LIVE",
                columns: table => new
                {
                    INSTRUCTOR_GOOGLE_ACCOUNT_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    INSTRUCTOR_USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    GOOGLE_SUBJECT = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GOOGLE_EMAIL = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    REFRESH_TOKEN_ENCRYPTED = table.Column<string>(type: "text", nullable: true),
                    SCOPES = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CONNECTED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    LAST_VALIDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    REVOKED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    REVOKED_REASON = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "bytea", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INSTRUCTOR_GOOGLE_ACCOUNTS", x => x.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
                });

            migrationBuilder.CreateTable(
                name: "SESSION_MEETINGS",
                schema: "LIVE",
                columns: table => new
                {
                    SESSION_MEETING_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    INSTRUCTOR_USER_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    PROVIDER = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    INSTRUCTOR_GOOGLE_ACCOUNT_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    PROVIDER_EVENT_ID = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MEET_URL_ENCRYPTED = table.Column<string>(type: "text", nullable: true),
                    SYNC_STATUS = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ICS_SEQUENCE = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ATTEMPTS = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NEXT_RETRY_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    LAST_SYNC_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    ERROR = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MEETING_ALERT_SENT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "bytea", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SESSION_MEETINGS", x => x.SESSION_MEETING_ID);
                    table.ForeignKey(
                        name: "FK_SESSION_MEETINGS_GOOGLE_ACCT",
                        column: x => x.INSTRUCTOR_GOOGLE_ACCOUNT_ID,
                        principalSchema: "LIVE",
                        principalTable: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                        principalColumn: "INSTRUCTOR_GOOGLE_ACCOUNT_ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_INSTR_GOOGLE_ACCT_USER_ID",
                schema: "LIVE",
                table: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                column: "INSTRUCTOR_USER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_MEETINGS_GOOGLE_ACCT_ID",
                schema: "LIVE",
                table: "SESSION_MEETINGS",
                column: "INSTRUCTOR_GOOGLE_ACCOUNT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_MEETINGS_INSTR_USER_ID",
                schema: "LIVE",
                table: "SESSION_MEETINGS",
                column: "INSTRUCTOR_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_MEETINGS_SESSION_ID",
                schema: "LIVE",
                table: "SESSION_MEETINGS",
                column: "SESSION_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_MEETINGS_SYNC_DUE",
                schema: "LIVE",
                table: "SESSION_MEETINGS",
                columns: new[] { "SYNC_STATUS", "NEXT_RETRY_AT_UTC" },
                filter: "\"SYNC_STATUS\" IN ('Pending','PendingDelete')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SESSION_MEETINGS",
                schema: "LIVE");

            migrationBuilder.DropTable(
                name: "INSTRUCTOR_GOOGLE_ACCOUNTS",
                schema: "LIVE");
        }
    }
}
