using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Identity.Infrastructure.Endpoints;

/// <summary>
/// Generic FluentValidation endpoint filter (ARCHITECTURE.md §2 cross-cutting table: "Validation |
/// FluentValidation + endpoint filter (คืน RFC 9457 ProblemDetails)"). Runs the registered
/// <see cref="IValidator{T}"/> for the endpoint's bound request type before the handler delegate
/// runs, short-circuiting with a validation-problem response on failure — so handlers never see an
/// invalid command (backend.md: "Endpoint ต้องบางที่สุด ... ห้ามมี business logic ใน endpoint").
/// <para>
/// Kept local to this module rather than in a shared project: Identity is the first module with real
/// endpoints, so there is no existing cross-module home for this yet, and this task's scope is
/// deliberately Siri.Modules.Identity/Siri.Persistence/Siri.Api only. If a second module needs the
/// identical filter later, that is the point to factor it out into Siri.SharedKernel — not before.
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
