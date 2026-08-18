import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiService } from '../../../core/http/api.service';
import {
  ConfirmEmailResponse,
  ForgotPasswordResponse,
  ListSessionsResponse,
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  ResetPasswordRequest,
  ResetPasswordResponse,
  RevokeAllSessionsResponse,
  RevokeOtherSessionsResponse,
  RevokeSessionResponse,
} from './auth-api.models';

/**
 * The one `*ApiService` for everything under `/api/identity/*` (P0-35) — components in
 * `features/auth/*` call this, never `HttpClient` directly (frontend.md). Every path/verb/shape
 * below is read directly off the real backend endpoint files, not guessed; see each method's own
 * comment for which file it mirrors.
 */
@Injectable({ providedIn: 'root' })
export class AuthApiService extends ApiService {
  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.post<RegisterResponse>('/identity/register', request);
  }

  /** POST, not GET — `ConfirmEmailEndpoint`'s own doc comment: a GET-with-side-effects link is
   *  vulnerable to being silently consumed by email-client link-prefetch/security scanners. */
  confirmEmail(token: string): Observable<ConfirmEmailResponse> {
    return this.post<ConfirmEmailResponse>('/identity/confirm-email', { token });
  }

  /** `withCredentials: true` — the only call in this service that needs it: this is the one that
   *  causes the backend to `Set-Cookie` the httpOnly refresh token (`RefreshTokenCookie.Set`), and
   *  the browser only stores/sends a cross-origin cookie when the request opts in. */
  login(request: LoginRequest): Observable<LoginResponse> {
    return this.post<LoginResponse>('/identity/login', request, { withCredentials: true });
  }

  forgotPassword(email: string): Observable<ForgotPasswordResponse> {
    return this.post<ForgotPasswordResponse>('/identity/forgot-password', { email });
  }

  resetPassword(request: ResetPasswordRequest): Observable<ResetPasswordResponse> {
    return this.post<ResetPasswordResponse>('/identity/reset-password', request);
  }

  /** Requires a valid access token — the interceptor attaches it automatically
   *  (`auth.interceptor.ts`); this group has no `.AllowAnonymous()` (`IdentityModule.cs`). */
  listSessions(): Observable<ListSessionsResponse> {
    return this.get<ListSessionsResponse>('/identity/sessions');
  }

  revokeSession(sessionId: string): Observable<RevokeSessionResponse> {
    return this.delete<RevokeSessionResponse>(`/identity/sessions/${sessionId}`);
  }

  revokeOtherSessions(): Observable<RevokeOtherSessionsResponse> {
    return this.post<RevokeOtherSessionsResponse>('/identity/sessions/revoke-others', {});
  }

  revokeAllSessions(): Observable<RevokeAllSessionsResponse> {
    return this.post<RevokeAllSessionsResponse>('/identity/sessions/revoke-all', {});
  }
}
