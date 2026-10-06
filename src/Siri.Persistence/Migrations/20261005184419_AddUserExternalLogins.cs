using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserExternalLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USER_EXTERNAL_LOGINS",
                schema: "IDENTITY",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    PROVIDER = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PROVIDER_SUBJECT = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PROVIDER_EMAIL = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LINKED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    LAST_LOGIN_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_EXTERNAL_LOGINS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USER_EXTERNAL_LOGINS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalSchema: "IDENTITY",
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_EXTERNAL_LOGINS_USER_ID",
                schema: "IDENTITY",
                table: "USER_EXTERNAL_LOGINS",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "UX_USER_EXTERNAL_LOGINS_PROVIDER_SUBJECT",
                schema: "IDENTITY",
                table: "USER_EXTERNAL_LOGINS",
                columns: new[] { "PROVIDER", "PROVIDER_SUBJECT" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_EXTERNAL_LOGINS",
                schema: "IDENTITY");
        }
    }
}
