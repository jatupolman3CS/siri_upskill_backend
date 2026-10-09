using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationKafkaDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PUBLISHED_AT_UTC",
                schema: "NOTIFY",
                table: "NOTIFICATIONS",
                type: "timestamp(3) with time zone",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QUEUED_AT_UTC",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX",
                type: "timestamp(3) with time zone",
                precision: 3,
                nullable: true);

            // Every notification that exists before this migration was never going to be announced to a message broker (there was none), so it
            // is created already "published". Without this the new partial index would start as a full index on the table and the Kafka relay's
            // first run would have the whole history to wade through. (Rows created later under the default Database transport stay unpublished
            // until a relay runs — they are retired in small chunks by the relay itself the first time Kafka is enabled.)
            migrationBuilder.Sql(
                "UPDATE \"NOTIFY\".\"NOTIFICATIONS\" SET \"PUBLISHED_AT_UTC\" = \"CREATED_AT_UTC\" WHERE \"PUBLISHED_AT_UTC\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_UNPUBLISHED",
                schema: "NOTIFY",
                table: "NOTIFICATIONS",
                column: "ID",
                filter: "\"PUBLISHED_AT_UTC\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATIONS_UNPUBLISHED",
                schema: "NOTIFY",
                table: "NOTIFICATIONS");

            migrationBuilder.DropColumn(
                name: "PUBLISHED_AT_UTC",
                schema: "NOTIFY",
                table: "NOTIFICATIONS");

            migrationBuilder.DropColumn(
                name: "QUEUED_AT_UTC",
                schema: "NOTIFY",
                table: "EMAIL_OUTBOX");
        }
    }
}
