using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="SESSION_MEETING"/> — docs/contracts/P11-03-live-module-google-meetings.md
/// §2.2. The only FK is to <c>INSTRUCTOR_GOOGLE_ACCOUNTS</c> (same module, <c>NoAction</c>); <c>SESSION_ID</c>
/// and <c>INSTRUCTOR_USER_ID</c> carry no FK (cross-schema — and the session row is not yet in the
/// database when the sink stages this one). The FK is declared with an explicit principal/dependent key
/// and constraint name (the auto-generated name would be 64 bytes, over PostgreSQL's 63-byte limit).
/// </summary>
public sealed class SessionMeetingConfiguration : IEntityTypeConfiguration<SESSION_MEETING>
{
    public void Configure(EntityTypeBuilder<SESSION_MEETING> builder)
    {
        builder.ToTable("SESSION_MEETINGS", "LIVE");

        builder.HasKey(m => m.SESSION_MEETING_ID).HasName("PK_SESSION_MEETINGS");

        builder.Property(m => m.SESSION_ID).IsRequired();
        builder.Property(m => m.INSTRUCTOR_USER_ID);

        // Enums as strings (database.md). Null PROVIDER = "not decided yet".
        builder.Property(m => m.PROVIDER).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        builder.Property(m => m.PROVIDER_EVENT_ID).HasMaxLength(200);

        // ISensitiveDataProtector.Encrypt(url) — never plaintext (the room link is a capability URL).
        builder.Property(m => m.MEET_URL_ENCRYPTED).HasColumnType("text");

        builder.Property(m => m.SYNC_STATUS).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(m => m.ICS_SEQUENCE).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.ATTEMPTS).IsRequired().HasDefaultValue(0);

        builder.Property(m => m.NEXT_RETRY_AT_UTC).HasPrecision(3);
        builder.Property(m => m.LAST_SYNC_AT_UTC).HasPrecision(3);

        // Short error code only — never a token, URL or e-mail address.
        builder.Property(m => m.ERROR).HasMaxLength(500);

        builder.Property(m => m.MEETING_ALERT_SENT_AT_UTC).HasPrecision(3);
        builder.Property(m => m.READINESS_ALERT_SENT_AT_UTC).HasPrecision(3);
        builder.Property(m => m.ATTENDEE_SYNC_ALERT_SENT_AT_UTC).HasPrecision(3);

        builder.Property(m => m.ROW_VERSION).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        // Derived members — never columns.
        builder.Ignore(m => m.IsUsable);
        builder.Ignore(m => m.CanRequestResync);

        // Same-module FK, NoAction: a Google account row is never deleted (revocation clears the token and
        // keeps the row), and nothing may cascade into meetings. No navigation — the sync job loads the
        // account by id through its own repository.
        builder.HasOne<INSTRUCTOR_GOOGLE_ACCOUNT>()
            .WithMany()
            .HasForeignKey(m => m.INSTRUCTOR_GOOGLE_ACCOUNT_ID)
            .HasConstraintName("FK_SESSION_MEETINGS_GOOGLE_ACCT")
            .OnDelete(DeleteBehavior.NoAction);

        // One meeting per session.
        builder.HasIndex(m => m.SESSION_ID).IsUnique().HasDatabaseName("IX_SESSION_MEETINGS_SESSION_ID");

        // The sync job's due-row query (Pending/PendingDelete whose retry time has passed). Partial index —
        // the vast majority of rows are Synced/Deleted and never queried by this path.
        builder.HasIndex(m => new { m.SYNC_STATUS, m.NEXT_RETRY_AT_UTC })
            .HasDatabaseName("IX_SESSION_MEETINGS_SYNC_DUE")
            .HasFilter("\"SYNC_STATUS\" IN ('Pending','PendingDelete')");

        // Reset-on-reconnect lookup: this instructor's meetings.
        builder.HasIndex(m => m.INSTRUCTOR_USER_ID).HasDatabaseName("IX_SESSION_MEETINGS_INSTR_USER_ID");

        // EF would create this FK-column index anyway under an auto-generated name; declaring it keeps the
        // name deterministic and short. Not listed in the contract's index list (it only enumerates the
        // indexes written by hand) — see the P11-03 hand-off note.
        builder.HasIndex(m => m.INSTRUCTOR_GOOGLE_ACCOUNT_ID).HasDatabaseName("IX_SESSION_MEETINGS_GOOGLE_ACCT_ID");

        // ---- IAuditable: UPPERCASE column, PascalCase C# property ---------------------------------
        builder.Property(m => m.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(m => m.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(m => m.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(m => m.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
