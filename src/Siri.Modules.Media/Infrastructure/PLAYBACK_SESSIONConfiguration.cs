using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>EF Core mapping for <see cref="PLAYBACK_SESSION"/> — see docs/DATABASE.md's "media" section,
/// and that entity's own doc comment for why it carries no FK constraints and no IAuditable/ISoftDelete.</summary>
public sealed class PLAYBACK_SESSIONConfiguration : IEntityTypeConfiguration<PLAYBACK_SESSION>
{
    public void Configure(EntityTypeBuilder<PLAYBACK_SESSION> builder)
    {
        builder.ToTable("PLAYBACK_SESSIONS", "MEDIA");

        builder.HasKey(p => p.PLAYBACK_SESSION_ID);
        builder.Property(p => p.PLAYBACK_SESSION_ID).HasColumnName("PLAYBACK_SESSION_ID");

        // No FK constraints on any of the three — all cross-module, see PLAYBACK_SESSION's own doc comment.
        builder.Property(p => p.USER_ID).HasColumnName("USER_ID").IsRequired();
        builder.Property(p => p.EPISODE_ID).HasColumnName("EPISODE_ID").IsRequired();
        builder.Property(p => p.SESSION_ID).HasColumnName("SESSION_ID").IsRequired();

        builder.Property(p => p.ISSUED_AT_UTC).HasColumnName("ISSUED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(p => p.EXPIRES_AT_UTC).HasColumnName("EXPIRES_AT_UTC").HasPrecision(3).IsRequired();

        builder.Property(p => p.IP_ADDRESS).HasColumnName("IP_ADDRESS").HasMaxLength(64);
        builder.Property(p => p.DEVICE_ID).HasColumnName("DEVICE_ID").HasMaxLength(200);

        // Not in DATABASE.md's sketch, but database.md calls for an index on any new query against a table
        // that can grow large ("เขียน query ใหม่ที่แตะตารางใหญ่ ... ต้องบอกได้ว่าใช้ index ตัวไหน") — this
        // table logs one row per playback token issuance, and its whole reason to exist is "find this
        // user's sessions to investigate a leak" (see class doc comment), so (UserId, IssuedAtUtc) is the
        // obvious first index to ship with it rather than add later under pressure.
        builder.HasIndex(p => new { p.USER_ID, p.ISSUED_AT_UTC });
    }
}
