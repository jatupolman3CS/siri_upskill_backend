import { Router, UrlTree } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { accessTokenStore } from '../http/access-token.store';
import { authGuard } from './auth.guard';

/**
 * Proves the guard actually gates `/account/devices` (its first real consumer, see
 * `app.routes.ts`): an unauthenticated visitor is redirected to `/login`, never allowed through to
 * the authenticated shell, and an authenticated one passes straight through.
 */
describe('authGuard', () => {
  beforeEach(() => {
    accessTokenStore.set(null);
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  afterEach(() => {
    accessTokenStore.set(null);
  });

  it('redirects an unauthenticated visitor to /login instead of allowing activation', () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));

    expect(result).not.toBe(true);
    const router = TestBed.inject(Router);
    const expectedTree = router.createUrlTree(['/login']);
    expect(router.serializeUrl(result as UrlTree)).toBe(router.serializeUrl(expectedTree));
  });

  it('allows activation once an access token is present', () => {
    accessTokenStore.set('a-real-looking-token');

    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));

    expect(result).toBe(true);
  });
});
