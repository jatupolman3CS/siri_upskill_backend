using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/> — see docs/DATABASE.md's "payout" section
/// and this module's D-17 UPPERCASE-naming decision (docs/DECISIONS.md).
/// <para>
/// <see cref="INSTRUCTOR_PAYOUT_ACCOUNT.INSTRUCTOR_ID"/> gets no <c>HasOne()</c>/FK at all — see that
/// class's own doc comment for why a cross-module/cross-schema FK is never added in this module. The
/// unique index on it is this configuration's own addition (not spelled out verbatim in docs/DATABASE.md's
/// terse inline sketch, same way <c>CourseConfiguration</c>'s covering index expands on catalog's inline
/// sketch) — see <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/>'s own doc comment for the one-row-per-instructor
/// reasoning.
/// </para>
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

        // Ciphertext budget, not the ~10-15 raw digits a Thai bank account number alone would need — see
        // INSTRUCTOR_PAYOUT_ACCOUNT's own doc comment: no real encryption is implemented yet, but the
        // column is already sized for a later encrypted payload.
        builder.Property(x => x.ACCOUNT_NO_ENCRYPTED).HasColumnName("ACCOUNT_NO_ENCRYPTED").HasMaxLength(500).IsRequired();

        builder.Property(x => x.ACCOUNT_NAME).HasColumnName("ACCOUNT_NAME").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TAX_ID).HasColumnName("TAX_ID").HasMaxLength(20);

        builder.Property(x => x.VERIFIED_AT_UTC).HasColumnName("VERIFIED_AT_UTC").HasPrecision(3);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(x => x.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
