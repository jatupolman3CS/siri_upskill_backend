using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<CONTACT_MESSAGE>
{
    public void Configure(EntityTypeBuilder<CONTACT_MESSAGE> builder)
    {
        builder.ToTable("CONTACT_MESSAGES", "NOTIFY");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Subject)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Message)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(c => c.AdminNotes)
            .HasMaxLength(1000);

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.CreatedAtUtc);
        builder.HasIndex(c => c.IsDeleted);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
