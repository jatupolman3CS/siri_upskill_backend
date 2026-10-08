using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="SESSION_JOIN_LOG"/> — docs/contracts/
/// P11-05-live-learner-instructor-api-join-gate.md §2. Append-only: no concurrency token and no audit
/// columns (see the entity's doc comment), and no FKs — every id points into another module's schema.
/// </summary>
public sealed class SessionJoinLogConfiguration : IEntityTypeConfiguration<SESSION_JOIN_LOG>
{
    public void Configure(EntityTypeBuilder<SESSION_JOIN_LOG> builder)
    {
        builder.ToTable("SESSION_JOIN_LOGS", "LIVE");

        builder.HasKey(l => l.SESSION_JOIN_LOG_ID).HasName("PK_SESSION_JOIN_LOGS");

        builder.Property(l => l.SESSION_ID).IsRequired();
        builder.Property(l => l.COURSE_ID).IsRequired();
        builder.Property(l => l.USER_ID).IsRequired();

        builder.Property(l => l.ROLE).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(l => l.AUTH_SESSION_ID);
        builder.Property(l => l.JOINED_AT_UTC).HasPrecision(3).IsRequired();

        builder.Property(l => l.IP_ADDRESS).HasMaxLength(64);
        builder.Property(l => l.USER_AGENT).HasMaxLength(300);

        // "Did this user ever join this session" (join gate bookkeeping, roster).
        builder.HasIndex(l => new { l.SESSION_ID, l.USER_ID }).HasDatabaseName("IX_SESSION_JOIN_LOGS_SESSION_USER");

        // "Did this user ever attend a live session of this course" — the refund hard-block (P11-12) and
        // ILiveAttendanceReader.GetCourseIdsAttendedAsync, answered without a cross-module join.
        builder.HasIndex(l => new { l.USER_ID, l.COURSE_ID }).HasDatabaseName("IX_SESSION_JOIN_LOGS_USER_COURSE");
    }
}
