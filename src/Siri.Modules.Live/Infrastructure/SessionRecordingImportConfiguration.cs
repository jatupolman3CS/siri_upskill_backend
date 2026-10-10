using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="SESSION_RECORDING_IMPORT"/> — docs/contracts/P11-13-live-recording-auto-import.md §3. No foreign keys at all:
/// <c>SESSION_ID</c>/<c>COURSE_ID</c> point into <c>CATALOG</c>, <c>INSTRUCTOR_USER_ID</c> into <c>IDENTITY</c> (cross-schema, same reasoning as
/// <c>SESSION_MEETINGS</c>), and <c>MEDIA_ASSET_ID</c>/<c>EPISODE_ID</c> into <c>MEDIA</c>/<c>CATALOG</c>. Nothing cascades. Every constraint/index
/// name is set explicitly and kept well under PostgreSQL's 63-byte identifier limit.
/// </summary>
public sealed class SessionRecordingImportConfiguration : IEntityTypeConfiguration<SESSION_RECORDING_IMPORT>
{
    public void Configure(EntityTypeBuilder<SESSION_RECORDING_IMPORT> builder)
    {
        builder.ToTable("SESSION_RECORDING_IMPORTS", "LIVE");

        builder.HasKey(i => i.SESSION_RECORDING_IMPORT_ID).HasName("PK_SESSION_RECORDING_IMPORTS");

        builder.Property(i => i.SESSION_ID).IsRequired();
        builder.Property(i => i.COURSE_ID).IsRequired();
        builder.Property(i => i.INSTRUCTOR_USER_ID).IsRequired();

        // Enum as string (database.md).
        builder.Property(i => i.STATUS).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(i => i.ATTEMPTS).IsRequired().HasDefaultValue(0);

        builder.Property(i => i.NEXT_ATTEMPT_AT_UTC).HasPrecision(3);
        builder.Property(i => i.LEASE_UNTIL_UTC).HasPrecision(3);
        builder.Property(i => i.SEARCH_UNTIL_UTC).HasPrecision(3).IsRequired();
        builder.Property(i => i.COMPLETED_AT_UTC).HasPrecision(3);

        // Google references: ids, not secrets — but never logged or returned.
        builder.Property(i => i.GOOGLE_RECORDING_NAME).HasMaxLength(SESSION_RECORDING_IMPORT.GoogleIdMaxLength);
        builder.Property(i => i.GOOGLE_FILE_ID).HasMaxLength(SESSION_RECORDING_IMPORT.GoogleIdMaxLength);

        builder.Property(i => i.MEDIA_ASSET_ID);
        builder.Property(i => i.EPISODE_ID);

        // A stable short code only — never a message, URL, id, token or e-mail.
        builder.Property(i => i.ERROR_CODE).HasMaxLength(SESSION_RECORDING_IMPORT.ErrorCodeMaxLength);

        builder.Property(i => i.ROW_VERSION).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        // Derived members — never columns.
        builder.Ignore(i => i.IsTerminal);
        builder.Ignore(i => i.CanRetry);

        // One import per session.
        builder.HasIndex(i => i.SESSION_ID).IsUnique().HasDatabaseName("IX_SESSION_RECORDING_IMPORTS_SESSION_ID");

        // The job's due-row query.
        builder.HasIndex(i => new { i.STATUS, i.NEXT_ATTEMPT_AT_UTC }).HasDatabaseName("IX_SESSION_RECORDING_IMPORTS_DUE");

        // ---- IAuditable: UPPERCASE column, PascalCase C# property ---------------------------------
        builder.Property(i => i.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(i => i.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(i => i.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(i => i.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
