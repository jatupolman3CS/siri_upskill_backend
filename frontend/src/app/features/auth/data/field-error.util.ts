import { ApiError } from '../../../core/http/error.interceptor';

/** Shape of `Results.ValidationProblem(validationResult.ToDictionary())` — `ValidationEndpointFilter<T>`
 *  (`Infrastructure/Endpoints/ValidationEndpointFilter.cs`). Dictionary keys are FluentValidation's
 *  `PropertyName` off the C# command record (e.g. `"Email"`, `"DisplayName"`) — PascalCase, exactly as
 *  written in each `RuleFor(c => c.X)` call, not camelCased: ASP.NET Core's default JSON naming policy
 *  transforms object *property* names, not `Dictionary<string, T>` *keys*. */
interface ValidationProblemDetailsLike {
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

function isValidationProblemDetailsLike(value: unknown): value is ValidationProblemDetailsLike {
  return typeof value === 'object' && value !== null && 'errors' in value;
}

/**
 * Pulls the first validation-error message for one command property (by its exact C# name, e.g.
 * `"Email"`) out of an `ApiError.details` payload — or `undefined` when this wasn't a
 * validation-shaped failure, or that field had no error. Used to route a backend field-shape
 * rejection (never an account-existence one — see each Handler's own anti-enumeration doc comment)
 * onto the matching `app-input`'s `errorMessage`.
 */
export function fieldError(apiError: ApiError | null, propertyName: string): string | undefined {
  const details = apiError?.details;
  if (!isValidationProblemDetailsLike(details)) {
    return undefined;
  }
  return details.errors?.[propertyName]?.[0];
}
