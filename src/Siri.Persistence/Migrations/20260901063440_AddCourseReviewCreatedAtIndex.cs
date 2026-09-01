using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseReviewCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EPISODE_ATTACHMENTS_EPISODE_ID",
                schema: "CATALOG",
                table: "EPISODE_ATTACHMENTS");

            migrationBuilder.DropIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID",
                schema: "CATALOG",
                table: "COURSE_REVIEWS");

            migrationBuilder.CreateIndex(
                name: "IX_INSTRUCTOR_PROFILES_STATUS_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "INSTRUCTOR_PROFILES",
                columns: new[] { "STATUS", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_EPISODE_ATTACHMENTS_EPISODE_ID_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "EPISODE_ATTACHMENTS",
                columns: new[] { "EPISODE_ID", "CREATED_AT_UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "COURSE_REVIEWS",
                columns: new[] { "COURSE_ID", "CREATED_AT_UTC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_INSTRUCTOR_PROFILES_STATUS_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "INSTRUCTOR_PROFILES");

            migrationBuilder.DropIndex(
                name: "IX_EPISODE_ATTACHMENTS_EPISODE_ID_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "EPISODE_ATTACHMENTS");

            migrationBuilder.DropIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID_CREATED_AT_UTC",
                schema: "CATALOG",
                table: "COURSE_REVIEWS");

            migrationBuilder.CreateIndex(
                name: "IX_EPISODE_ATTACHMENTS_EPISODE_ID",
                schema: "CATALOG",
                table: "EPISODE_ATTACHMENTS",
                column: "EPISODE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_COURSE_REVIEWS_COURSE_ID",
                schema: "CATALOG",
                table: "COURSE_REVIEWS",
                column: "COURSE_ID");
        }
    }
}
