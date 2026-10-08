using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseGoogleAttendeeSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GOOGLE_ATTENDEE_SYNC_ENABLED",
                schema: "CATALOG",
                table: "COURSES",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GOOGLE_ATTENDEE_SYNC_ENABLED",
                schema: "CATALOG",
                table: "COURSES");
        }
    }
}
