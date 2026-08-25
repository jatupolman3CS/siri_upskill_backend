using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.SharedKernel;

/// <summary>
/// Generic FluentValidation endpoint filter (ARCHITECTURE.md §2 cross-cutting table: "Validation |
/// FluentValidation + endpoint filter (คืน RFC 9457 ProblemDetails)"). Runs the registered
/// <see cref="IValidator{T}"/> for the endpoint's bound request type before the handler delegate
/// runs, short-circuiting with a validation-problem response on failure — so handlers never see an
/// invalid command (backend.md: "Endpoint ต้องบางที่สุด ... ห้ามมี business logic ใน endpoint").
/// <para>
/// Originally lived in <c>Siri.Modules.Identity</c> (the first module with real endpoints, so there
/// was no cross-module home for this yet). Relocated here for P1-01 once Catalog became a second
/// consumer — that class's own doc comment said this was the point to factor it out, not before.
/// </para>
/// </summary>
public sealed class ValidationEndpointFilter<T> : IEndpointFilter
    where T : notnull
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
        var validationResult = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted).ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return Results.ValidationProblem(validationResult.ToDictionary());
        }

        return await next(context).ConfigureAwait(false);
    }
}
