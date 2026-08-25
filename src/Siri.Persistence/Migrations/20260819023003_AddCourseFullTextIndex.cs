using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siri.Persistence.Migrations
{
    /// <summary>
    /// Task P1-06 (Full-text search): MSSQL Full-Text Index on <c>catalog.Courses(Title, Subtitle,
    /// Description)</c> — docs/ARCHITECTURE.md §6 / docs/DECISIONS.md D-10. EF Core has no fluent API for
    /// this (Full-Text Search is a SQL Server feature outside EF's provider-agnostic model entirely, not
    /// just a missing convenience method), so this migration is hand-written raw SQL rather than
    /// generated from a model change — <c>dotnet ef migrations add</c> produced an empty scaffold, which
    /// this fills in by hand. <c>KEY INDEX PK_Courses</c>: SQL Server requires a full-text index to be
    /// keyed off a single-column, non-nullable, unique index — <c>Courses</c>' own primary key
    /// (confirmed as the literal constraint name in the <c>AddCourseDomain</c> migration) is exactly
    /// that. <c>WITH CHANGE_TRACKING AUTO</c> keeps the index updated automatically as rows change,
    /// so nothing else in this codebase needs to know the index exists at all.
    /// <para>
    /// <b>Real fix, caught by actually applying this to the real Contabo DB</b> (2026-08-19): the first
    /// apply attempt failed with "CREATE FULLTEXT CATALOG statement cannot be used inside a user
    /// transaction" — SQL Server refuses that specific DDL statement inside an explicit transaction, but
    /// EF Core wraps every migration's SQL in one by default. It never applied successfully (confirmed
    /// via <c>dotnet ef migrations list</c> still showing it Pending right after the failed attempt —
    /// the two migrations before it in the same run committed fine, only this one's transaction rolled
    /// back), so per database.md's "ห้ามแก้ไข migration ที่ apply ขึ้น shared env ไปแล้ว" this file was
    /// safe to edit in place rather than needing a brand-new migration. Fixed by passing
    /// <c>suppressTransaction: true</c> to every statement below — EF Core then runs each one outside any
    /// ambient transaction, satisfying SQL Server's restriction on <c>CREATE</c>/<c>DROP FULLTEXT
    /// CATALOG</c>; applied to the <c>INDEX</c> statements too even though those alone aren't restricted
    /// this way, so nothing here depends on cross-statement transactional atomicity SQL Server won't
    /// actually give it regardless.
    /// </para>
    /// </summary>
    public partial class AddCourseFullTextIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE FULLTEXT CATALOG CourseFullTextCatalog AS DEFAULT;", suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE FULLTEXT INDEX ON catalog.Courses(Title, Subtitle, Description)
                KEY INDEX PK_Courses
                ON CourseFullTextCatalog
                WITH CHANGE_TRACKING AUTO;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FULLTEXT INDEX ON catalog.Courses;", suppressTransaction: true);
            migrationBuilder.Sql("DROP FULLTEXT CATALOG CourseFullTextCatalog;", suppressTransaction: true);
        }
    }
}
