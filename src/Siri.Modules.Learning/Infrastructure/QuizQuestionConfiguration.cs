using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="QUIZ_QUESTION"/>. No
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> columns — see that class's own doc comment.</summary>
public sealed class QuizQuestionConfiguration : IEntityTypeConfiguration<QUIZ_QUESTION>
{
    public void Configure(EntityTypeBuilder<QUIZ_QUESTION> builder)
    {
        builder.ToTable("QUIZ_QUESTIONS", "LEARNING");

        builder.HasKey(q => q.QUIZ_QUESTION_ID);

        builder.Property(q => q.TYPE).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(q => q.TEXT).HasMaxLength(1000).IsRequired();
        builder.Property(q => q.EXPLANATION).HasMaxLength(2000);
        builder.Property(q => q.POINTS).IsRequired();
        builder.Property(q => q.SORT_ORDER).IsRequired();

        // Cascade: the real ownership edge — a question cannot outlive its quiz. .WithMany(quiz =>
        // quiz.QUESTIONS), not a bare .WithMany() — see QuizConfiguration's matching note for why an
        // unreferenced navigation makes EF invent a phantom shadow FK.
        builder.HasOne<QUIZ>()
            .WithMany(quiz => quiz.QUESTIONS)
            .HasForeignKey(q => q.QUIZ_ID)
            .OnDelete(DeleteBehavior.Cascade);

        // Same navigation-vs-shadow-FK reasoning as the QUIZ relationship above, one level down:
        // QUIZ_QUESTION.OPTIONS is a real navigation, so QUIZ_OPTION's own configuration must reference it
        // too (see QuizOptionConfiguration) — this side owns the .Navigation()/.HasField() wiring since
        // OPTIONS lives on this entity type.
        builder.Navigation(q => q.OPTIONS).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Mirrors Catalog's (CourseId, SortOrder)-shaped indexes exactly — serves the ordered-children
        // query for a quiz's questions.
        builder.HasIndex(q => new { q.QUIZ_ID, q.SORT_ORDER });
    }
}
