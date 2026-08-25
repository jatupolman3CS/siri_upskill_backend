using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="BUNDLE_ITEM"/> — see docs/DATABASE.md's "commerce" section:
/// <c>BundleItems(BundleId, CourseId) PK(BundleId, CourseId)</c>. Composite primary key, no surrogate
/// id — the only child entity in this scaffold pass shaped this way (contrast
/// <see cref="FLASH_SALE_ITEM"/>, which keeps its own id plus a unique index instead).
/// </summary>
public sealed class BUNDLE_ITEMConfiguration : IEntityTypeConfiguration<BUNDLE_ITEM>
{
    public void Configure(EntityTypeBuilder<BUNDLE_ITEM> builder)
    {
        builder.ToTable("BUNDLE_ITEMS", "COMMERCE");

        builder.HasKey(bi => new { bi.BUNDLE_ID, bi.COURSE_ID });

        // Pure composition child — true composition child of BUNDLE per this scaffold task's own
        // instructions, so Cascade is correct here.
        builder.HasOne<BUNDLE>()
            .WithMany(b => b.BUNDLE_ITEMS)
            .HasForeignKey(bi => bi.BUNDLE_ID)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK — conceptual reference to catalog.Courses.Id only.
        builder.Property(bi => bi.COURSE_ID).IsRequired();
    }
}
