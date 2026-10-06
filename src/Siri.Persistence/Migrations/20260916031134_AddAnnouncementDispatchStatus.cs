using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementDispatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // X-31: existing rows must backfill to "Sent" (not "Pending" and not the scaffolded ""
            // default) — "Pending" would make AnnouncementDispatchJob re-blast every historical
            // announcement the moment this deploys; "" doesn't deserialize to any
            // AnnouncementDispatchStatus member at all. See docs/contracts/X-31-announcement-delivery.md §2.
            migrationBuilder.AddColumn<string>(
                name: "DISPATCH_STATUS",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Sent");

            migrationBuilder.CreateIndex(
                name: "IX_ANNOUNCEMENTS_DISPATCH_STATUS_SCHEDULED_AT_UTC",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS",
                columns: new[] { "DISPATCH_STATUS", "SCHEDULED_AT_UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ANNOUNCEMENTS_DISPATCH_STATUS_SCHEDULED_AT_UTC",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS");

            migrationBuilder.DropColumn(
                name: "DISPATCH_STATUS",
                schema: "NOTIFY",
                table: "ANNOUNCEMENTS");
        }
    }
}
