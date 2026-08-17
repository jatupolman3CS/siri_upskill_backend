import { HttpInterceptorFn } from '@angular/common/http';

import { accessTokenStore } from './access-token.store';

/**
 * Attaches the in-memory access token (if any) as a Bearer Authorization
 * header. There is no real token issuance flow yet — nothing calls
 * `accessTokenStore.set()` outside of tests until the Identity module
 * ships — so today this interceptor is effectively a no-op passthrough.
 * It exists so the transport-level plumbing is already correct and every
 * later `*ApiService` automatically gets auth headers for free.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = accessTokenStore.token();
  if (!token) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
