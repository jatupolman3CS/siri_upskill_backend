import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Query parameter values accepted by the ApiService helper methods. */
export type ApiQueryParams = Record<
  string,
  string | number | boolean | readonly (string | number)[] | undefined
>;

function toHttpParams(params?: ApiQueryParams): HttpParams {
  let httpParams = new HttpParams();
  if (!params) {
    return httpParams;
  }
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined) {
      continue;
    }
    if (Array.isArray(value)) {
      for (const item of value) {
        httpParams = httpParams.append(key, String(item));
      }
    } else {
      httpParams = httpParams.set(key, String(value));
    }
  }
  return httpParams;
}

/**
 * Thin base that feature-level `*ApiService` classes (living under each
 * feature's `data/` folder) extend instead of injecting `HttpClient`
 * directly — per frontend.md, components must never call `HttpClient`
 * themselves.
 *
 * No real endpoints exist yet (the backend has no Identity/Catalog/etc.
 * endpoints to call). This class only fixes the base URL and gives typed
 * `get/post/put/delete` helpers so the first real feature API service has
 * a consistent shape to follow.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  protected readonly http = inject(HttpClient);

  // TODO: source from a proper environment/config token once the backend
  // base URL (and any per-env overrides for staging/prod) is decided.
  protected readonly apiBaseUrl = '/api';

  protected get<T>(path: string, params?: ApiQueryParams): Observable<T> {
    return this.http.get<T>(`${this.apiBaseUrl}${path}`, { params: toHttpParams(params) });
  }

  /**
   * `withCredentials`, when passed, tells the browser to send/store this origin's cookies on the
   * request — needed by Identity's login/refresh endpoints, whose httpOnly refresh-token cookie
   * (`RefreshTokenCookie.cs`) is otherwise never set/sent cross-origin between the Angular dev
   * server and the API. Optional and defaulted-away for every other caller, so this is additive,
   * not a behavior change for the handful of call sites that predate P0-35.
   */
  protected post<T>(path: string, body: unknown, options?: { readonly withCredentials?: boolean }): Observable<T> {
    return this.http.post<T>(`${this.apiBaseUrl}${path}`, body, { withCredentials: options?.withCredentials });
  }

  protected put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<T>(`${this.apiBaseUrl}${path}`, body);
  }

  protected delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(`${this.apiBaseUrl}${path}`);
  }
}
