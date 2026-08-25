using FluentValidation;

namespace Siri.Modules.Learning.Application;

/// <summary>Format-only checks — no DB access (mirrors Catalog's <c>CreateCourseValidator</c> doc
/// comment: format here, existence/uniqueness/business rules in the Service).</summary>
public sealed class CreateQuizRequestValidator : AbstractValidator<CreateQuizRequest>
{
    public const int MaxTitleLength = 200; // matches QUIZZES.TITLE's column width (QuizConfiguration)

    public CreateQuizRequestValidator()
    {
        RuleFor(r => r.EpisodeId).NotEmpty();
        RuleFor(r => r.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(r => r.PassingScorePercent).InclusiveBetween(0, 100);
        RuleFor(r => r.MaxAttempts).GreaterThan(0);
    }
}

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.</summary>
public sealed class AddQuizQuestionRequestValidator : AbstractValidator<AddQuizQuestionRequest>
{
    public const int MaxTextLength = 1000; // matches QUIZ_QUESTIONS.TEXT's column width
    public const int MaxExplanationLength = 2000; // matches QUIZ_QUESTIONS.EXPLANATION's column width

    public AddQuizQuestionRequestValidator()
    {
        RuleFor(r => r.Type).IsInEnum();
        RuleFor(r => r.Text).NotEmpty().MaximumLength(MaxTextLength);
        RuleFor(r => r.Explanation).MaximumLength(MaxExplanationLength);
        RuleFor(r => r.Points).GreaterThan(0);
    }
}

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.</summary>
public sealed class AddQuizOptionRequestValidator : AbstractValidator<AddQuizOptionRequest>
{
    public const int MaxTextLength = 500; // matches QUIZ_OPTIONS.TEXT's column width

    public AddQuizOptionRequestValidator()
    {
        RuleFor(r => r.Text).NotEmpty().MaximumLength(MaxTextLength);
    }
}
