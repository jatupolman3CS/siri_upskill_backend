using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("Notifications", "notify");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.Type).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(300).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.LinkUrl).HasMaxLength(1000);
        builder.Property(n => n.ReadAtUtc).HasPrecision(3);
        builder.Property(n => n.CreatedAtUtc).HasPrecision(3).IsRequired();

        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc }).HasDatabaseName("IX_Notifications_UserId_CreatedAtUtc");
        builder.HasIndex(n => new { n.UserId, n.ReadAtUtc }).HasDatabaseName("IX_Notifications_UserId_ReadAtUtc");
    }
}
