using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="QUIZ_ATTEMPT_ANSWER"/>. No
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> columns — see that class's own doc comment.</summary>
public sealed class QuizAttemptAnswerConfiguration : IEntityTypeConfiguration<QUIZ_ATTEMPT_ANSWER>
{
    public void Configure(EntityTypeBuilder<QUIZ_ATTEMPT_ANSWER> builder)
    {
        builder.ToTable("QUIZ_ATTEMPT_ANSWERS", "LEARNING");

        builder.HasKey(a => a.QUIZ_ATTEMPT_ANSWER_ID);

        // Cascade: the real ownership edge — an answer cannot outlive its attempt. .WithMany(a =>
        // a.ANSWERS), not a bare .WithMany() — see QuizQuestionConfiguration's matching note for why an
        // unreferenced navigation makes EF invent a phantom shadow FK.
        builder.HasOne<QUIZ_ATTEMPT>()
            .WithMany(a => a.ANSWERS)
            .HasForeignKey(a => a.ATTEMPT_ID)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK — see QUIZ_ATTEMPT_ANSWER's own doc comment (references a question on the separate QUIZ
        // aggregate).
        builder.Property(a => a.QUESTION_ID).IsRequired();

        // nvarchar(max), not a bounded length — JSON-serialized array, a deliberate simplification
        // already in the source schema sketch (docs/DATABASE.md), not a real relational structure. See
        // QUIZ_ATTEMPT_ANSWER.SELECTED_OPTION_IDS's own doc comment.
        builder.Property(a => a.SELECTED_OPTION_IDS).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(a => a.IS_CORRECT).IsRequired();

        // One recorded answer per question per attempt.
        builder.HasIndex(a => new { a.ATTEMPT_ID, a.QUESTION_ID }).IsUnique();
    }
}
