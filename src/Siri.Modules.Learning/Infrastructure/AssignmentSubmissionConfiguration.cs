using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>EF Core mapping for <see cref="ASSIGNMENT_SUBMISSION"/> — see that class's own doc comment
/// for why <see cref="ASSIGNMENT_SUBMISSION.ASSIGNMENT_ID"/> gets a real (NoAction) FK but
/// <see cref="ASSIGNMENT_SUBMISSION.ENROLLMENT_ID"/>/<see cref="ASSIGNMENT_SUBMISSION.GRADED_BY_USER_ID"/>
/// do not.</summary>
public sealed class AssignmentSubmissionConfiguration : IEntityTypeConfiguration<ASSIGNMENT_SUBMISSION>
{
    public void Configure(EntityTypeBuilder<ASSIGNMENT_SUBMISSION> builder)
    {
        builder.ToTable("ASSIGNMENT_SUBMISSIONS", "LEARNING");

        builder.HasKey(s => s.ASSIGNMENT_SUBMISSION_ID);

        // NoAction, not Cascade — references an ASSIGNMENT from a separate aggregate; deleting an
        // assignment must not silently cascade-delete a learner's historical submissions (same reasoning
        // as QuizAttemptConfiguration's QUIZ_ID relationship, and database.md's blanket instinct against
        // losing a learner's own history).
        builder.Property(s => s.ASSIGNMENT_ID).IsRequired();
        builder.HasOne<ASSIGNMENT>()
            .WithMany()
            .HasForeignKey(s => s.ASSIGNMENT_ID)
            .OnDelete(DeleteBehavior.NoAction);

        // No FK — different aggregate (Enrollment, same module), same reasoning as
        // QUIZ_ATTEMPT.ENROLLMENT_ID.
        builder.Property(s => s.ENROLLMENT_ID).IsRequired();
        builder.HasIndex(s => s.ENROLLMENT_ID);

        builder.Property(s => s.STORAGE_KEY).HasMaxLength(500).IsRequired();
        builder.Property(s => s.NOTE).HasMaxLength(2000);
        builder.Property(s => s.SUBMITTED_AT_UTC).HasPrecision(3).IsRequired();
        builder.Property(s => s.STATUS).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.SCORE).HasPrecision(5, 2);
        builder.Property(s => s.FEEDBACK).HasMaxLength(2000);
        // No FK — conceptually identity.Users.Id, cross-module; see this entity's own doc comment.
        builder.Property(s => s.GRADED_BY_USER_ID);
        builder.Property(s => s.GRADED_AT_UTC).HasPrecision(3);

        // Serves the instructor's "pending submissions for this assignment" queue query directly.
        builder.HasIndex(s => new { s.ASSIGNMENT_ID, s.STATUS });

        builder.Property(s => s.CreatedAtUtc).HasColumnName("CREATED_AT_UTC").HasPrecision(3).IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("CREATED_BY");
        builder.Property(s => s.UpdatedAtUtc).HasColumnName("UPDATED_AT_UTC").HasPrecision(3);
        builder.Property(s => s.UpdatedBy).HasColumnName("UPDATED_BY");
    }
}
