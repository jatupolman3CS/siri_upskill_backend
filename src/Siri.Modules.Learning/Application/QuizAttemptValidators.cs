using FluentValidation;

namespace Siri.Modules.Learning.Application;

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.</summary>
public sealed class StartQuizAttemptRequestValidator : AbstractValidator<StartQuizAttemptRequest>
{
    public StartQuizAttemptRequestValidator()
    {
        RuleFor(r => r.QuizId).NotEmpty();
        RuleFor(r => r.EnrollmentId).NotEmpty();
    }
}

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment. A
/// learner must select at least one option — <see cref="Domain.QuizQuestionType.SingleChoice"/> vs
/// <see cref="Domain.QuizQuestionType.MultipleChoice"/>'s "exactly one" vs "one or more" distinction is a
/// business rule that needs the question's own <see cref="Domain.QUIZ_QUESTION.TYPE"/> loaded — left to
/// <see cref="QuizAttemptService.AnswerAsync"/>, not this format-only validator.</summary>
public sealed class SubmitQuizAnswerRequestValidator : AbstractValidator<SubmitQuizAnswerRequest>
{
    public SubmitQuizAnswerRequestValidator()
    {
        RuleFor(r => r.QuestionId).NotEmpty();
        RuleFor(r => r.SelectedOptionIds).NotEmpty();
    }
}
