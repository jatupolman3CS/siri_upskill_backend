using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="QUIZ_ATTEMPT"/> — see that class's own doc comment for why
/// <see cref="QUIZ_ATTEMPT.QUIZ_ID"/> gets a real (NoAction) FK but <see cref="QUIZ_ATTEMPT.ENROLLMENT_ID"/>
/// does not.</summary>
public sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QUIZ_ATTEMPT>
{
    public void Configure(EntityTypeBuilder<QUIZ_ATTEMPT> builder)
    {
        builder.ToTable("QUIZ_ATTEMPTS", "LEARNING");

        builder.HasKey(a => a.QUIZ_ATTEMPT_ID);

        // NoAction, not Cascade — this attempt references a QUIZ from a separate aggregate; deleting a
        // quiz must not silently cascade-delete a learner's historical attempts.
        builder.Property(a => a.QUIZ_ID).IsRequired();
        builder.HasOne<QUIZ>()
            .WithMany()
            .HasForeignKey(a => a.QUIZ_ID)
            .OnDelete(DeleteBehavior.NoAction);

        // No FK — different aggregate (Enrollment, same module) with no cross-aggregate repository
        // contract yet in this scaffold pass; see this entity's own doc comment.
        builder.Property(a => a.ENROLLMENT_ID).IsRequired();

        builder.Property(a => a.ATTEMPT_NO).IsRequired();
        builder.Property(a => a.SCORE_PERCENT).HasPrecision(5, 2);
        builder.Property(a => a.IS_PASSED).IsRequired();
        builder.Property(a => a.STARTED_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(a => a.SUBMITTED_AT_UTC).HasPrecision(3);

        // .WithMany(a => a.ANSWERS), not a bare .WithMany() — see QuizQuestionConfiguration's matching
        // note for why an unreferenced navigation makes EF invent a phantom shadow FK.
        builder.Navigation(a => a.ANSWERS).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);

        // A learner cannot have two rows claiming the same attempt number for the same quiz — this is
        // also exactly the "how many attempts already exist" lookup IQuizAttemptRepository needs.
        builder.HasIndex(a => new { a.QUIZ_ID, a.ENROLLMENT_ID, a.ATTEMPT_NO }).IsUnique();

        builder.Property(a => a.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(a => a.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(a => a.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(a => a.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
