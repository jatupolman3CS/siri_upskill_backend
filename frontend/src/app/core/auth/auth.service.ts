import { Injectable, computed, signal } from '@angular/core';

/**
 * Shape of the authenticated user as exposed to the rest of the app.
 * TODO: replace with the real DTO once the Identity module's Contracts
 * surface exists (see ARCHITECTURE.md section 2, module boundary rules).
 */
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly roles: readonly string[];
}

/**
 * Signal-based auth skeleton only — there is no real login/logout/refresh
 * flow yet because the Identity module has no endpoints to call. This
 * exists so guards, layout chrome, and interceptors have a stable, typed
 * `currentUser`/`isAuthenticated` surface to depend on while the real auth
 * flow is built in a later task.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _currentUser = signal<CurrentUser | null>(null);

  /** `null` when no user is signed in. */
  readonly currentUser = this._currentUser.asReadonly();

  readonly isAuthenticated = computed(() => this._currentUser() !== null);

  // TODO: real login(credentials), logout(), and refresh() once
  // POST /api/auth/* exists. Will also need to wire accessTokenStore
  // (core/http/access-token.store.ts) and the httpOnly refresh cookie flow
  // described in frontend.md.
}
