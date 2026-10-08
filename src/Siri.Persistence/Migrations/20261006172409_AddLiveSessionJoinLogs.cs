using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveSessionJoinLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SESSION_JOIN_LOGS",
                schema: "LIVE",
                columns: table => new
                {
                    SESSION_JOIN_LOG_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ROLE = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AUTH_SESSION_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    JOINED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    IP_ADDRESS = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    USER_AGENT = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SESSION_JOIN_LOGS", x => x.SESSION_JOIN_LOG_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_JOIN_LOGS_SESSION_USER",
                schema: "LIVE",
                table: "SESSION_JOIN_LOGS",
                columns: new[] { "SESSION_ID", "USER_ID" });

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_JOIN_LOGS_USER_COURSE",
                schema: "LIVE",
                table: "SESSION_JOIN_LOGS",
                columns: new[] { "USER_ID", "COURSE_ID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SESSION_JOIN_LOGS",
                schema: "LIVE");
        }
    }
}
