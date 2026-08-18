import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

/**
 * Functional route guard (Angular's current, non-class-based guard shape) for any route that
 * requires a signed-in caller — first real consumer being `/account/devices` (P0-35), which wraps a
 * backend group that itself defaults to `.RequireAuthorization()`
 * (`IdentityModule.MapIdentityEndpoints`'s doc comment) with no `.AllowAnonymous()` opt-out anywhere
 * in the device-management endpoints. This guard is the frontend-side mirror of that same default
 * deny: an unauthenticated visitor must never even see the authenticated shell render before the
 * first API call 401s.
 * <para>
 * Reads `AuthService.isAuthenticated` — itself derived from `accessTokenStore`'s in-memory token
 * (never `localStorage`, per security.md/frontend.md) — so this is correct in both the browser and
 * during SSR: SSR always starts with a fresh, empty in-memory token (nothing carries an access
 * token from a prior request into a new server-rendered one), so this guard will always redirect
 * during a server render too, which is the same reason `/account/devices` is mapped
 * `RenderMode.Client` in `app.routes.server.ts` — SSR can never legitimately know whether the
 * visitor is signed in, so it should not try to render the authenticated page at all.
 * </para>
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);

  if (authService.isAuthenticated()) {
    return true;
  }

  return inject(Router).createUrlTree(['/login']);
};
