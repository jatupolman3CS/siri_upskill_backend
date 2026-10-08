using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="SESSION_INVITE"/> — docs/contracts/P11-04-live-invites-ics-reminders.md
/// §2.2. No FKs at all: <c>SESSION_ID</c> and <c>USER_ID</c> point into other modules' schemas. Rows are
/// evidence of who was invited/un-invited and are never deleted (no cascade anywhere).
/// </summary>
public sealed class SessionInviteConfiguration : IEntityTypeConfiguration<SESSION_INVITE>
{
    public void Configure(EntityTypeBuilder<SESSION_INVITE> builder)
    {
        builder.ToTable("SESSION_INVITES", "LIVE");

        builder.HasKey(i => i.SESSION_INVITE_ID).HasName("PK_SESSION_INVITES");

        builder.Property(i => i.SESSION_ID).IsRequired();
        builder.Property(i => i.USER_ID).IsRequired();

        builder.Property(i => i.ROLE).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(i => i.STATUS).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(i => i.ICS_SEQUENCE_SENT);

        builder.Property(i => i.INVITE_SENT_AT_UTC).HasPrecision(3);
        builder.Property(i => i.CANCEL_SENT_AT_UTC).HasPrecision(3);

        // The contract's column names are REMINDER_24H_SENT_AT_UTC / REMINDER_1H_SENT_AT_UTC.
        // AppDbContext.ApplyUppercaseNamingConventions rewrites every column name with a regex that also
        // splits "digit followed by an upper-case letter", so the property name (or an upper-case
        // HasColumnName) would land as REMINDER_24_H_SENT_AT_UTC. A name with no upper-case letters is left
        // alone by the splitter and merely upper-cased afterwards — hence the deliberate lower-case here.
        // LiveSchemaTests pins the resulting names so a change to the convention cannot silently rename them.
        builder.Property(i => i.REMINDER_24H_SENT_AT_UTC).HasColumnName("reminder_24h_sent_at_utc").HasPrecision(3);
        builder.Property(i => i.REMINDER_1H_SENT_AT_UTC).HasColumnName("reminder_1h_sent_at_utc").HasPrecision(3);

        builder.Property(i => i.GOOGLE_ATTENDEE_SYNCED_AT_UTC).HasPrecision(3);

        // Short code only (e.g. "no_contact") — never an e-mail address.
        builder.Property(i => i.ERROR).HasMaxLength(300);

        builder.Property(i => i.ROW_VERSION).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        // One invite per participant per session; also what makes two concurrent reconcile runs collide
        // (DbUpdateException on the unique index) instead of double-inviting.
        builder.HasIndex(i => new { i.SESSION_ID, i.USER_ID }).IsUnique().HasDatabaseName("IX_SESSION_INVITES_SESSION_USER");

        // "Which sessions is this user invited to" (my-sessions, reminders).
        builder.HasIndex(i => i.USER_ID).HasDatabaseName("IX_SESSION_INVITES_USER_ID");

        // The reconcile job's "who still needs the first e-mail" scan. Partial — almost every row is Invited.
        builder.HasIndex(i => i.SESSION_ID)
            .HasDatabaseName("IX_SESSION_INVITES_PENDING")
            .HasFilter("\"STATUS\" = 'Pending'");

        // ---- IAuditable: UPPERCASE column, PascalCase C# property ---------------------------------
        builder.Property(i => i.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(i => i.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(i => i.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(i => i.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
