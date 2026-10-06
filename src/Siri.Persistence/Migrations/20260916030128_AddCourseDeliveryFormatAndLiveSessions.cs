using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseDeliveryFormatAndLiveSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DELIVERY_FORMAT",
                schema: "CATALOG",
                table: "COURSES",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "OnDemand");

            migrationBuilder.CreateTable(
                name: "COURSE_LIVE_SESSIONS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    COURSE_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    TITLE = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    STARTS_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    ENDS_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    SORT_ORDER = table.Column<int>(type: "integer", nullable: false),
                    STATUS = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CANCEL_REASON = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RECORDING_EPISODE_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "bytea", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COURSE_LIVE_SESSIONS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_COURSE_LIVE_SESSIONS_COURSES_COURSE_ID",
                        column: x => x.COURSE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSES",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_COURSE_LIVE_SESSIONS_COURSE_EPISODES_RECORDING_EPISODE_ID",
                        column: x => x.RECORDING_EPISODE_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSE_EPISODES",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_LIVE_SESSIONS_COURSE_ID_STARTS_AT_UTC",
                schema: "CATALOG",
                table: "COURSE_LIVE_SESSIONS",
                columns: new[] { "COURSE_ID", "STARTS_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_LIVE_SESSIONS_RECORDING_EPISODE_ID",
                schema: "CATALOG",
                table: "COURSE_LIVE_SESSIONS",
                column: "RECORDING_EPISODE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_LIVE_SESSIONS_STARTS_AT_UTC",
                schema: "CATALOG",
                table: "COURSE_LIVE_SESSIONS",
                column: "STARTS_AT_UTC",
                filter: "\"STATUS\" = 'Scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COURSE_LIVE_SESSIONS",
                schema: "CATALOG");

            migrationBuilder.DropColumn(
                name: "DELIVERY_FORMAT",
                schema: "CATALOG",
                table: "COURSES");
        }
    }
}
