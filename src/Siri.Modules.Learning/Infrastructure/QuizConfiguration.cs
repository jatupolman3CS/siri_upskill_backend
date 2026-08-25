using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core mapping for <see cref="QUIZ"/> — see docs/DATABASE.md's "learning" section and
/// <see cref="QUIZ"/>'s own doc comment for the UPPERCASE naming exception (docs/DECISIONS.md D-17).
/// Table/schema/column names are uppercase by convention; the C# properties are already named that way
/// (e.g. <c>TITLE</c>), so no explicit <c>.HasColumnName()</c> is needed except on the four
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> properties, which stay PascalCase in C# (see
/// that interface's own doc comment) but still map to uppercase columns.
/// </summary>
public sealed class QuizConfiguration : IEntityTypeConfiguration<QUIZ>
{
    public void Configure(EntityTypeBuilder<QUIZ> builder)
    {
        builder.ToTable("QUIZZES", "LEARNING");

        builder.HasKey(q => q.QUIZ_ID);

        // No FK — cross-module/cross-schema to catalog.CourseEpisodes, same reasoning
        // Course.TrailerMediaAssetId never gets one in Catalog (CourseConfiguration's own doc comment).
        // Indexed anyway: "does this episode already have a quiz" is exactly the lookup
        // IQuizRepository.GetByEpisodeIdAsync needs.
        builder.Property(q => q.EPISODE_ID).IsRequired();
        builder.HasIndex(q => q.EPISODE_ID);

        builder.Property(q => q.TITLE).HasMaxLength(200).IsRequired();
        builder.Property(q => q.PASSING_SCORE_PERCENT).IsRequired();
        builder.Property(q => q.MAX_ATTEMPTS).IsRequired();
        builder.Property(q => q.IS_ACTIVE).IsRequired();

        // .WithMany(q => q.QUESTIONS), not a bare .WithMany() — QUIZ.QUESTIONS is a real navigation
        // property (backed by a private field, wired below) — an unreferenced navigation makes EF's
        // convention discovery invent a second, phantom relationship with its own shadow FK column,
        // exactly the bug Catalog's CourseConfiguration doc comment already caught once for
        // Course.Sections. See QuizQuestionConfiguration for the FK side of this relationship.
        builder.Navigation(q => q.QUESTIONS).HasField("_questions").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(q => q.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(q => q.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(q => q.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(q => q.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
