using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveSessionAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LIVE_SESSION_ATTACHMENTS",
                schema: "CATALOG",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    SESSION_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    FILE_NAME = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    STORAGE_KEY = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CONTENT_TYPE = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SIZE_BYTES = table.Column<long>(type: "bigint", nullable: false),
                    CREATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    CREATED_BY = table.Column<Guid>(type: "uuid", nullable: true),
                    UPDATED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    UPDATED_BY = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LIVE_SESSION_ATTACHMENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LIVE_SESSION_ATTACHMENTS_COURSE_LIVE_SESSIONS_SESSION_ID",
                        column: x => x.SESSION_ID,
                        principalSchema: "CATALOG",
                        principalTable: "COURSE_LIVE_SESSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LIVE_SESSION_ATTACHMENTS_SESSION_ID_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "LIVE_SESSION_ATTACHMENTS",
                columns: new[] { "SESSION_ID", "CREATED_AT_UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LIVE_SESSION_ATTACHMENTS",
                schema: "CATALOG");
        }
    }
}
