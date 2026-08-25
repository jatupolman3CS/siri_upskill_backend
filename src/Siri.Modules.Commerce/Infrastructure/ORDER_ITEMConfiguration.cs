using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="ORDER_ITEM"/> — see docs/DATABASE.md's "commerce" section.</summary>
public sealed class ORDER_ITEMConfiguration : IEntityTypeConfiguration<ORDER_ITEM>
{
    public void Configure(EntityTypeBuilder<ORDER_ITEM> builder)
    {
        builder.ToTable("ORDER_ITEMS", "COMMERCE");

        builder.HasKey(oi => oi.ORDER_ITEM_ID);

        // Pure composition child, same shape CourseSection -> Course uses: Cascade is correct here (per
        // this scaffold task's own instructions — ORDER_ITEMS is a "true composition child").
        builder.HasOne<ORDER>()
            .WithMany(o => o.ORDER_ITEMS)
            .HasForeignKey(oi => oi.ORDER_ID)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK — conceptual reference to catalog.Courses.Id only (see this property's own doc comment
        // on ORDER_ITEM).
        builder.Property(oi => oi.COURSE_ID);

        builder.Property(oi => oi.TITLE_SNAPSHOT).HasMaxLength(200).IsRequired();
        builder.Property(oi => oi.UNIT_PRICE).HasPrecision(18, 2).IsRequired();
        builder.Property(oi => oi.LINE_TOTAL).HasPrecision(18, 2).IsRequired();
    }
}
