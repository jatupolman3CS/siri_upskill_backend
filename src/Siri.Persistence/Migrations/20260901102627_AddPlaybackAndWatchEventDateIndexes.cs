using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackAndWatchEventDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WATCH_EVENTS_OCCURRED_AT_UTC",
                schema: "LEARNING",
                table: "WATCH_EVENTS",
                column: "OCCURRED_AT_UTC");

            migrationBuilder.CreateIndex(
                name: "IX_PLAYBACK_SESSIONS_ISSUED_AT_UTC",
                schema: "MEDIA",
                table: "PLAYBACK_SESSIONS",
                column: "ISSUED_AT_UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WATCH_EVENTS_OCCURRED_AT_UTC",
                schema: "LEARNING",
                table: "WATCH_EVENTS");

            migrationBuilder.DropIndex(
                name: "IX_PLAYBACK_SESSIONS_ISSUED_AT_UTC",
                schema: "MEDIA",
                table: "PLAYBACK_SESSIONS");
        }
    }
}
