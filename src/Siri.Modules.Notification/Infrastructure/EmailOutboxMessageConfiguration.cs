using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Infrastructure;

/// <summary>EF Core mapping for <see cref="EmailOutboxMessage"/> — see docs/DATABASE.md's
/// "cms / community / notify / analytics" section, <c>notify.EmailOutbox</c>.</summary>
public sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox", "notify");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ToEmail).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(300).IsRequired();

        // Genuinely unbounded rendered HTML content (task P0-19: "BodyHtml can be nvarchar(max)
        // since it is genuinely unbounded content") — the one intentional exception to "always
        // HasMaxLength", same as SecurityAudit.Detail's json column.
        builder.Property(m => m.BodyHtml).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(m => m.TemplateKey).HasMaxLength(100);

        // Same HasConversion<string>() convention UserStatus established as the first enum in the
        // codebase (database.md leaves string-vs-smallint open but says pick one and stay consistent).
        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.Attempts).IsRequired();

        builder.Property(m => m.NextRetryAtUtc).HasPrecision(3);
        builder.Property(m => m.SentAtUtc).HasPrecision(3);

        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Supports the sender job's exact due-message query (Infrastructure/EmailOutboxSenderJob.cs):
        // Status == Pending OR (Status == Failed AND NextRetryAtUtc <= now). Status leads the index
        // so both branches of the OR can seek on it; NextRetryAtUtc as the second key column then
        // supports the range scan for the Failed-but-retryable branch. Per database.md: "เขียน query
        // ใหม่ที่แตะตารางใหญ่ ... ต้องบอกได้ว่าใช้ index ตัวไหน ถ้าไม่มีให้เพิ่ม index มาใน migration เดียวกัน".
        builder.HasIndex(m => new { m.Status, m.NextRetryAtUtc })
            .HasDatabaseName("IX_EmailOutbox_Status_NextRetryAtUtc");
    }
}
