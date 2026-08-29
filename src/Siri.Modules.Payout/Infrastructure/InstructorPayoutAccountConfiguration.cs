using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/> — see docs/DATABASE.md's "payout" section
/// and this module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// </summary>
public sealed class InstructorPayoutAccountConfiguration : IEntityTypeConfiguration<INSTRUCTOR_PAYOUT_ACCOUNT>
{
    public void Configure(EntityTypeBuilder<INSTRUCTOR_PAYOUT_ACCOUNT> builder)
    {
        builder.ToTable("INSTRUCTOR_PAYOUT_ACCOUNTS", "PAYOUT");

        builder.HasKey(x => x.INSTRUCTOR_PAYOUT_ACCOUNT_ID);
        builder.Property(x => x.INSTRUCTOR_PAYOUT_ACCOUNT_ID).HasColumnName("INSTRUCTOR_PAYOUT_ACCOUNT_ID");

        builder.Property(x => x.INSTRUCTOR_ID).HasColumnName("INSTRUCTOR_ID").IsRequired();
        builder.HasIndex(x => x.INSTRUCTOR_ID).IsUnique().HasDatabaseName("IX_INSTRUCTOR_PAYOUT_ACCOUNTS_INSTRUCTOR_ID");

        builder.Property(x => x.BANK_CODE).HasColumnName("BANK_CODE").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ACCOUNT_NO_ENCRYPTED).HasColumnName("ACCOUNT_NO_ENCRYPTED").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ACCOUNT_NAME).HasColumnName("ACCOUNT_NAME").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TAX_ID).HasColumnName("TAX_ID").HasMaxLength(500);

        builder.Property(x => x.TAX_PAYER_TYPE).HasColumnName("TAX_PAYER_TYPE").HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(x => x.VERIFIED_AT_UTC).HasColumnName("VERIFIED_AT_UTC").HasPrecision(3);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
