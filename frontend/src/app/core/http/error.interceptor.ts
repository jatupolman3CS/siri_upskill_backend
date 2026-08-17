import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/**
 * Normalized error shape every `*ApiService` consumer can rely on, instead
 * of branching on raw `HttpErrorResponse` transport details.
 */
export interface ApiError {
  readonly status: number;
  readonly message: string;
  readonly code?: string;
  readonly details?: unknown;
}

interface ProblemDetailsLike {
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
}

function isProblemDetailsLike(value: unknown): value is ProblemDetailsLike {
  return typeof value === 'object' && value !== null;
}

/**
 * Normalizes every failed HTTP call into an `ApiError`. The backend returns
 * RFC 9457 ProblemDetails bodies (see ARCHITECTURE.md section 2, cross-cutting
 * error handling) once real endpoints exist; this already understands that
 * shape plus falls back gracefully for network-level failures that never
 * reach the server. `message` falls back to an i18n key (not display text)
 * so callers can resolve it through `TranslationService` themselves.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const body: unknown = error.error;
      const problemDetails = isProblemDetailsLike(body) ? body : undefined;

      const apiError: ApiError = {
        status: error.status,
        message: problemDetails?.detail ?? problemDetails?.title ?? 'common.error',
        code: problemDetails?.code,
        details: problemDetails,
      };

      return throwError(() => apiError);
    }),
  );
