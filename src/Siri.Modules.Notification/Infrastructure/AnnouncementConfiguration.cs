using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements", "notify");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.CourseId).IsRequired();
        builder.Property(a => a.InstructorId).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Body).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(a => a.SendEmail).IsRequired();
        builder.Property(a => a.ScheduledAtUtc).HasPrecision(3);
        builder.Property(a => a.SentAtUtc).HasPrecision(3);
        builder.Property(a => a.RecipientCount).IsRequired();
        builder.Property(a => a.CreatedAtUtc).HasPrecision(3).IsRequired();

        builder.HasIndex(a => a.CourseId).HasDatabaseName("IX_Announcements_CourseId");
        builder.HasIndex(a => a.InstructorId).HasDatabaseName("IX_Announcements_InstructorId");
    }
}
