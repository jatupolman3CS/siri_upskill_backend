using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>EF Core mapping for <see cref="TAX_INVOICE"/> — see docs/DECISIONS.md D-17's gap-fill (this
/// table is new, not in docs/DATABASE.md's original "commerce" sketch).</summary>
public sealed class TAX_INVOICEConfiguration : IEntityTypeConfiguration<TAX_INVOICE>
{
    public void Configure(EntityTypeBuilder<TAX_INVOICE> builder)
    {
        builder.ToTable("TAX_INVOICES", "COMMERCE");

        builder.HasKey(t => t.TAX_INVOICE_ID);

        // Money table — NoAction, never Cascade, same reasoning as PAYMENTConfiguration's own ORDER_ID FK.
        // Unique: at most one tax invoice per order.
        builder.HasOne<ORDER>()
            .WithMany()
            .HasForeignKey(t => t.ORDER_ID)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.ORDER_ID).IsUnique();

        // See TAX_INVOICE.TAX_ID_ENCRYPTED's own doc comment: holds an encrypted value at rest once a
        // later task implements that; 500 chars leaves room for typical AES-GCM ciphertext + IV/tag
        // overhead in a way the ~13-digit plaintext Thai tax ID never would need.
        builder.Property(t => t.TAX_ID_ENCRYPTED).HasMaxLength(500).IsRequired();

        builder.Property(t => t.BUYER_NAME).HasMaxLength(200).IsRequired();

        builder.Property(t => t.INVOICE_NO).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.INVOICE_NO).IsUnique();

        builder.Property(t => t.ISSUED_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(t => t.PDF_STORAGE_KEY).HasMaxLength(1000);
        builder.Property(t => t.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
    }
}
