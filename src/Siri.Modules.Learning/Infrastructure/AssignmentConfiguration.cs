using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="ASSIGNMENT"/> — see docs/DATABASE.md's "learning" section and
/// <see cref="QUIZ"/>'s own doc comment for the UPPERCASE naming exception (docs/DECISIONS.md D-17).</summary>
public sealed class AssignmentConfiguration : IEntityTypeConfiguration<ASSIGNMENT>
{
    public void Configure(EntityTypeBuilder<ASSIGNMENT> builder)
    {
        builder.ToTable("ASSIGNMENTS", "LEARNING");

        builder.HasKey(a => a.ASSIGNMENT_ID);

        // No FK — cross-module/cross-schema to catalog.CourseEpisodes, same reasoning as QUIZ.EPISODE_ID.
        builder.Property(a => a.EPISODE_ID).IsRequired();
        builder.HasIndex(a => a.EPISODE_ID);

        builder.Property(a => a.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(a => a.INSTRUCTIONS).HasMaxLength(4000).IsRequired();
        builder.Property(a => a.DUE_DAYS);
        builder.Property(a => a.MAX_FILE_SIZE_MB).IsRequired();

        // Comma-separated (e.g. "pdf,docx,zip"), not JSON — a flat list of short tokens has no nesting to
        // justify a JSON column (unlike QUIZ_ATTEMPT_ANSWER.SELECTED_OPTION_IDS, which really is a
        // variable-shape structure), and stays trivially readable/editable directly in the database.
        builder.Property(a => a.ALLOWED_EXTENSIONS).HasMaxLength(500).IsRequired();

        builder.Property(a => a.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(a => a.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
