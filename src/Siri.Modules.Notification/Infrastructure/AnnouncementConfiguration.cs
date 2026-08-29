using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<ANNOUNCEMENT>
{
    public void Configure(EntityTypeBuilder<ANNOUNCEMENT> builder)
    {
        builder.ToTable("ANNOUNCEMENTS", "NOTIFY");

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

        builder.HasIndex(a => a.CourseId).HasDatabaseName("IX_ANNOUNCEMENTS_COURSE_ID");
        builder.HasIndex(a => a.InstructorId).HasDatabaseName("IX_ANNOUNCEMENTS_INSTRUCTOR_ID");
    }
}
