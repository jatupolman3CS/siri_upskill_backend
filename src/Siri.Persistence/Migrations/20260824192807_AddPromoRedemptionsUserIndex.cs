using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoRedemptionsUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_USER_ID",
                schema: "COMMERCE",
                table: "PROMO_REDEMPTIONS",
                columns: new[] { "PROMO_CODE_ID", "USER_ID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PROMO_REDEMPTIONS_PROMO_CODE_ID_USER_ID",
                schema: "COMMERCE",
                table: "PROMO_REDEMPTIONS");
        }
    }
}
