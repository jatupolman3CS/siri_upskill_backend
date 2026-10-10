using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/> — docs/contracts/
/// P11-03-live-module-google-meetings.md §2.1. UPPERCASE properties map to identically-named columns; the
/// four <c>IAuditable</c> properties (PascalCase in C#) get explicit UPPERCASE column names — see
/// <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/>'s doc comment. No FK to <c>identity.Users</c> (cross-module).
/// Every constraint/index name is set explicitly: the auto-generated names for this module are long enough
/// to brush PostgreSQL's 63-byte identifier limit.
/// </summary>
public sealed class InstructorGoogleAccountConfiguration : IEntityTypeConfiguration<INSTRUCTOR_GOOGLE_ACCOUNT>
{
    public void Configure(EntityTypeBuilder<INSTRUCTOR_GOOGLE_ACCOUNT> builder)
    {
        builder.ToTable("INSTRUCTOR_GOOGLE_ACCOUNTS", "LIVE");

        builder.HasKey(a => a.INSTRUCTOR_GOOGLE_ACCOUNT_ID).HasName("PK_INSTRUCTOR_GOOGLE_ACCOUNTS");

        builder.Property(a => a.INSTRUCTOR_USER_ID).IsRequired();
        builder.Property(a => a.GOOGLE_SUBJECT).HasMaxLength(64).IsRequired();
        builder.Property(a => a.GOOGLE_EMAIL).HasMaxLength(320).IsRequired();

        // Output of ISensitiveDataProtector.Encrypt — base64 of unbounded-looking length, nullable because
        // a revoked account deliberately keeps no token.
        builder.Property(a => a.REFRESH_TOKEN_ENCRYPTED).HasColumnType("text");

        builder.Property(a => a.SCOPES).HasMaxLength(500).IsRequired();

        builder.Property(a => a.CONNECTED_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(a => a.LAST_VALIDATED_AT_UTC).HasPrecision(3);
        builder.Property(a => a.REVOKED_AT_UTC).HasPrecision(3);
        builder.Property(a => a.REVOKED_REASON).HasMaxLength(40);

        // P11-13: the Workspace domain (userinfo "hd") and when it was last read. Both NULL on rows connected before the column existed.
        builder.Property(a => a.HOSTED_DOMAIN).HasMaxLength(255);
        builder.Property(a => a.ACCOUNT_KIND_CHECKED_AT_UTC).HasPrecision(3);

        builder.Property(a => a.ROW_VERSION).IsConcurrencyToken().HasColumnType("bytea").IsRequired();

        // Derived members — never columns.
        builder.Ignore(a => a.IsActive);
        builder.Ignore(a => a.AccountKind);
        builder.Ignore(a => a.HasRecordingScopes);

        // One Google account per instructor.
        builder.HasIndex(a => a.INSTRUCTOR_USER_ID).IsUnique().HasDatabaseName("IX_INSTR_GOOGLE_ACCT_USER_ID");

        // ---- IAuditable: UPPERCASE column, PascalCase C# property ---------------------------------
        builder.Property(a => a.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(a => a.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
