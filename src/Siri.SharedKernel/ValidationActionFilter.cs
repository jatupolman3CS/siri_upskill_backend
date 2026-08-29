using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.SharedKernel;

/// <summary>
/// Action filter executing FluentValidation on action arguments.
/// Runs any registered <see cref="IValidator{T}"/> for bound action parameters before the action executes,
/// short-circuiting with a ValidationProblem (RFC 9457) on failure.
/// </summary>
public sealed class ValidationActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            var validator = services.GetService(validatorType) as IValidator;

            if (validator is not null)
            {
                var validationContext = new ValidationContext<object>(argument);
                var validationResult = await validator.ValidateAsync(validationContext, cancellationToken).ConfigureAwait(false);

                if (!validationResult.IsValid)
                {
                    var errors = validationResult.ToDictionary();
                    var problemDetails = new ValidationProblemDetails(errors)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "One or more validation errors occurred.",
                    };
                    context.Result = new BadRequestObjectResult(problemDetails);
                    return;
                }
            }
        }

        await next().ConfigureAwait(false);
    }
}
