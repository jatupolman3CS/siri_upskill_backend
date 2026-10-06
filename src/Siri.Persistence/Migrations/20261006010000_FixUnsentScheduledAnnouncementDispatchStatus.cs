using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixUnsentScheduledAnnouncementDispatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data fix for AddAnnouncementDispatchStatus (already applied, so corrected here instead of
            // editing it): that migration backfilled every existing announcement to 'Sent', including
            // scheduled announcements that were never sent (SENT_AT_UTC is only ever set by
            // ANNOUNCEMENT.MarkSent) and are still in the future. Put those back to 'Pending' so
            // AnnouncementDispatchJob delivers them when they come due; already-past, never-delivered
            // announcements stay 'Sent' so they are not blasted out late.
            migrationBuilder.Sql(
                "UPDATE \"NOTIFY\".\"ANNOUNCEMENTS\" " +
                "SET \"DISPATCH_STATUS\" = 'Pending' " +
                "WHERE \"DISPATCH_STATUS\" = 'Sent' " +
                "AND \"SENT_AT_UTC\" IS NULL " +
                "AND \"SCHEDULED_AT_UTC\" IS NOT NULL " +
                "AND \"SCHEDULED_AT_UTC\" > now();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the affected rows cannot be told apart from rows that were legitimately
            // Pending afterwards, and reverting them to 'Sent' would silently drop their delivery.
        }
    }
}
