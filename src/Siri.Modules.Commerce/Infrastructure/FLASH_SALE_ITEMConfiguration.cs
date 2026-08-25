using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="FLASH_SALE_ITEM"/> — see docs/DATABASE.md's "commerce"
/// section.</summary>
public sealed class FLASH_SALE_ITEMConfiguration : IEntityTypeConfiguration<FLASH_SALE_ITEM>
{
    public void Configure(EntityTypeBuilder<FLASH_SALE_ITEM> builder)
    {
        builder.ToTable("FLASH_SALE_ITEMS", "COMMERCE");

        builder.HasKey(fi => fi.FLASH_SALE_ITEM_ID);

        // Pure composition child — true composition child of FLASH_SALE per this scaffold task's own
        // instructions, so Cascade is correct here.
        builder.HasOne<FLASH_SALE>()
            .WithMany(f => f.FLASH_SALE_ITEMS)
            .HasForeignKey(fi => fi.FLASH_SALE_ID)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK — conceptual reference to catalog.Courses.Id only.
        builder.Property(fi => fi.COURSE_ID).IsRequired();

        builder.Property(fi => fi.SALE_PRICE).HasPrecision(18, 2).IsRequired();

        // docs/DATABASE.md: "UQ(FlashSaleId, CourseId)" — a course appears at most once per sale.
        builder.HasIndex(fi => new { fi.FLASH_SALE_ID, fi.COURSE_ID }).IsUnique();
    }
}
