using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveSessionInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SESSION_INVITES",
                schema: "LIVE",
                columns: table => new
                {
                    SESSION_INVITE_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ROLE = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    STATUS = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ICS_SEQUENCE_SENT = table.Column<int>(type: "integer", nullable: true),
                    INVITE_SENT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    CANCEL_SENT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    REMINDER_24H_SENT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    REMINDER_1H_SENT_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    GOOGLE_ATTENDEE_SYNCED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    ERROR = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "bytea", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SESSION_INVITES", x => x.SESSION_INVITE_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_INVITES_PENDING",
                schema: "LIVE",
                table: "SESSION_INVITES",
                column: "SESSION_ID",
                filter: "\"STATUS\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_INVITES_SESSION_USER",
                schema: "LIVE",
                table: "SESSION_INVITES",
                columns: new[] { "SESSION_ID", "USER_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_INVITES_USER_ID",
                schema: "LIVE",
                table: "SESSION_INVITES",
                column: "USER_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SESSION_INVITES",
                schema: "LIVE");
        }
    }
}
