using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for this module's Quiz/Assignment cluster entities on the shared
/// <see cref="AppDbContext"/> — same reasoning as <c>Siri.Modules.Catalog.Infrastructure
/// .AppDbContextCatalogExtensions</c>'s own doc comment (<see cref="AppDbContext"/> carries no
/// module-owned <c>DbSet&lt;T&gt;</c> properties to avoid a circular project reference).
/// <para>
/// A SEPARATE file from <c>AppDbContextLearningExtensions.cs</c> (the Enrollment/Progress/Certificate
/// part of this module, scaffolded in parallel by another task/agent) — both are plain static classes of
/// extension methods with distinct names, specifically to avoid two agents editing the same file at the
/// same time. There is no other reason they need to be split; a later cleanup pass may merge them once
/// both parts have landed.
/// </para>
/// <para>
/// Child entities (<see cref="QuizQuestions"/>/<see cref="QuizOptions"/>/<see cref="QuizAttemptAnswers"/>)
/// get their own accessors too, despite <see cref="QUIZ"/>/<see cref="QUIZ_ATTEMPT"/> being their
/// aggregate roots — matches Catalog's precedent (database.md prefers projecting straight to DTOs over
/// always materializing whole graphs) and gives repository implementations a direct route to the child
/// tables when they need one.
/// </para>
/// </summary>
public static class AppDbContextLearningQuizExtensions
{
    public static DbSet<QUIZ> Quizzes(this AppDbContext context) => context.Set<QUIZ>();

    public static DbSet<QUIZ_QUESTION> QuizQuestions(this AppDbContext context) => context.Set<QUIZ_QUESTION>();

    public static DbSet<QUIZ_OPTION> QuizOptions(this AppDbContext context) => context.Set<QUIZ_OPTION>();

    public static DbSet<QUIZ_ATTEMPT> QuizAttempts(this AppDbContext context) => context.Set<QUIZ_ATTEMPT>();

    public static DbSet<QUIZ_ATTEMPT_ANSWER> QuizAttemptAnswers(this AppDbContext context) => context.Set<QUIZ_ATTEMPT_ANSWER>();

    public static DbSet<ASSIGNMENT> Assignments(this AppDbContext context) => context.Set<ASSIGNMENT>();

    public static DbSet<ASSIGNMENT_SUBMISSION> AssignmentSubmissions(this AppDbContext context) => context.Set<ASSIGNMENT_SUBMISSION>();
}
