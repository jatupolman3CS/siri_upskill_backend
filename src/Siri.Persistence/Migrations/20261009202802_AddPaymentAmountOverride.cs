using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAmountOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ORIGINAL_AMOUNT",
                schema: "COMMERCE",
                table: "PAYMENTS",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PAYMENT_AMOUNT_OVERRIDES",
                schema: "COMMERCE",
                columns: table => new
                {
                    PAYMENT_AMOUNT_OVERRIDE_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    IS_ENABLED = table.Column<bool>(type: "boolean", nullable: false),
                    OVERRIDE_AMOUNT = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    REASON = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CHANGED_BY_USER_ID = table.Column<Guid>(type: "uuid", nullable: false),
                    CHANGED_AT_UTC = table.Column<DateTime>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_AMOUNT_OVERRIDES", x => x.PAYMENT_AMOUNT_OVERRIDE_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_AMOUNT_OVERRIDES_CHANGED_AT_UTC",
                schema: "COMMERCE",
                table: "PAYMENT_AMOUNT_OVERRIDES",
                column: "CHANGED_AT_UTC",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PAYMENT_AMOUNT_OVERRIDES",
                schema: "COMMERCE");

            migrationBuilder.DropColumn(
                name: "ORIGINAL_AMOUNT",
                schema: "COMMERCE",
                table: "PAYMENTS");
        }
    }
}
