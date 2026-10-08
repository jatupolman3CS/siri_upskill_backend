using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOutboxCalendarPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CALENDAR_ICS",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CALENDAR_METHOD",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CALENDAR_ICS",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX");

            migrationBuilder.DropColumn(
                name: "CALENDAR_METHOD",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX");
        }
    }
}
