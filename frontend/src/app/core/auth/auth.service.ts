import { Injectable, computed } from '@angular/core';

import { accessTokenStore } from '../http/access-token.store';
import { decodeAccessTokenUserId } from './jwt.util';

/**
 * Shape of the authenticated user as exposed to the rest of the app. Deliberately just `id` — see
 * this class's own doc comment ("Why so little") for why there is nothing more to expose yet.
 */
export interface CurrentUser {
  readonly id: string;
}

/**
 * Signal-based auth state, now backed by the real Identity API (P0-35) — `LoginPage` is the only
 * writer (via `setAccessToken`), reached after a real `POST /api/identity/login` call succeeds.
 * Guards, layout chrome, and interceptors read `isAuthenticated`/`currentUser`, same stable surface
 * this class always exposed; only the implementation behind it is now real.
 * <para>
 * <b>Why so little on `CurrentUser`</b>: `AccessTokenGenerator.cs` deliberately puts no email/
 * display name into the JWT ("keeping the JWT itself minimal limits what a stolen token actually
 * exposes"), and there is no "who am I" endpoint yet to ask for the rest — see the P0-35 final
 * report's gaps section. So `currentUser` can only ever honestly expose what the token itself
 * carries: the caller's own id (the `sub` claim, decoded client-side — see `jwt.util.ts`). No
 * backend request is needed for this; the token already sat in memory.
 * </para>
 * <para>
 * `isAuthenticated` is derived from token *presence*, not from a successful decode — a token that
 * fails to decode (should never happen for a token this app itself issued) still means "the user is
 * holding what they believe is a valid session"; guards should not treat a decode hiccup as "log
 * them out", only as "we don't know their id".
 * </para>
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly isAuthenticated = computed(() => accessTokenStore.token() !== null);

  readonly currentUser = computed<CurrentUser | null>(() => {
    const token = accessTokenStore.token();
    if (token === null) {
      return null;
    }
    const userId = decodeAccessTokenUserId(token);
    return userId === null ? null : { id: userId };
  });

  /** Called once, right after `POST /api/identity/login` (or `/refresh`) resolves — stores the
   *  access token in memory only (`accessTokenStore`), never localStorage (security.md). */
  setAccessToken(accessToken: string): void {
    accessTokenStore.set(accessToken);
  }

  /**
   * Drops the in-memory access token, ending the session from this tab's point of view.
   * <para>
   * Deliberately does NOT call any backend endpoint — there is no dedicated "logout" endpoint in
   * this API (see `IdentityModule.cs`'s full endpoint list); the closest real equivalents are
   * `DELETE /sessions/{sessionId}` (revoke just this device) or `/sessions/revoke-all` (revoke
   * everywhere), both heavier operations than a plain logout (they also revoke the DB-tracked
   * `UserSession`/`RefreshToken`, not just clear a local token) and both already wired into
   * `DevicesPage`. This method is what those flows call afterward to also clear this tab's own
   * in-memory state; it is intentionally not a page/button of its own in this task's scope.
   * </para>
   */
  clearSession(): void {
    accessTokenStore.set(null);
  }
}
