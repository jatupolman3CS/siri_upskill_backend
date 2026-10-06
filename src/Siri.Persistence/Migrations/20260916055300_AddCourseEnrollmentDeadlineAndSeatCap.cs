using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseEnrollmentDeadlineAndSeatCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ENROLLMENT_DEADLINE_UTC",
                schema: "CATALOG",
                table: "COURSES",
                type: "timestamp(3) with time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MAX_SEATS",
                schema: "CATALOG",
                table: "COURSES",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SEATS_USED",
                schema: "CATALOG",
                table: "COURSES",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ENROLLMENT_DEADLINE_UTC",
                schema: "CATALOG",
                table: "COURSES");

            migrationBuilder.DropColumn(
                name: "MAX_SEATS",
                schema: "CATALOG",
                table: "COURSES");

            migrationBuilder.DropColumn(
                name: "SEATS_USED",
                schema: "CATALOG",
                table: "COURSES");
        }
    }
}
