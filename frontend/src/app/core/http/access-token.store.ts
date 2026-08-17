import { signal } from '@angular/core';

/**
 * In-memory holder for the current access token.
 *
 * Per frontend.md: the access token lives in memory only — never
 * localStorage — and refresh state lives in an httpOnly cookie the browser
 * manages automatically. There is no real login/refresh flow yet (Identity
 * module has no endpoints); this only gives `authInterceptor` and the
 * future `AuthService` a shared, typed place to read/write the token from.
 */
const accessToken = signal<string | null>(null);

export const accessTokenStore = {
  token: accessToken.asReadonly(),
  set(token: string | null): void {
    accessToken.set(token);
  },
};
