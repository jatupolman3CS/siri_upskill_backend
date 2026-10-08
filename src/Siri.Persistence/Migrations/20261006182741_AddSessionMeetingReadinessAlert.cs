using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionMeetingReadinessAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "READINESS_ALERT_SENT_AT_UTC",
                schema: "LIVE",
                table: "SESSION_MEETINGS",
                type: "timestamp(3) with time zone",
                precision: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "READINESS_ALERT_SENT_AT_UTC",
                schema: "LIVE",
                table: "SESSION_MEETINGS");
        }
    }
}
