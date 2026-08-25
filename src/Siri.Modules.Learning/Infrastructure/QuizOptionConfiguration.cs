using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="QUIZ_OPTION"/>. No
/// <see cref="Siri.Persistence.Conventions.IAuditable"/> columns — see that class's own doc comment.</summary>
public sealed class QuizOptionConfiguration : IEntityTypeConfiguration<QUIZ_OPTION>
{
    public void Configure(EntityTypeBuilder<QUIZ_OPTION> builder)
    {
        builder.ToTable("QUIZ_OPTIONS", "LEARNING");

        builder.HasKey(o => o.QUIZ_OPTION_ID);

        builder.Property(o => o.TEXT).HasMaxLength(500).IsRequired();
        builder.Property(o => o.IS_CORRECT).IsRequired();
        builder.Property(o => o.SORT_ORDER).IsRequired();

        // Cascade: single incoming FK path to QUIZ_QUESTIONS (no dual-cascade-path conflict — unlike
        // Catalog's CourseEpisode, which has two paths to Courses; there is no equivalent second path
        // here). .WithMany(q => q.OPTIONS), not a bare .WithMany() — see QuizQuestionConfiguration's
        // matching note for why an unreferenced navigation makes EF invent a phantom shadow FK.
        builder.HasOne<QUIZ_QUESTION>()
            .WithMany(q => q.OPTIONS)
            .HasForeignKey(o => o.QUESTION_ID)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.QUESTION_ID, o.SORT_ORDER });
    }
}
