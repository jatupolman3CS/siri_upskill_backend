/**
 * Minimal, read-only JWT payload decoder — no signature verification (the frontend has no key to
 * verify with, and has no reason to: the token only ever reached this app because the backend's
 * own `AddJwtBearer` pipeline already validated it on whichever request returned it, and every API
 * call this app makes gets independently re-validated server-side regardless of what this decodes).
 * This exists purely so `AuthService` can read the two claims it needs for UI state back out of the
 * access token it already holds in memory, without adding a "who am I" HTTP round-trip.
 *
 * Uses the global `atob`/`TextDecoder` — real Web Platform APIs, not `window`/`document`, and both
 * are available as Node.js globals too (Node 18+, which this SSR app already requires), so no
 * `isPlatformBrowser` guard is needed here (frontend.md's SSR-safety rule targets DOM globals like
 * `window`/`document`/`localStorage`, not these).
 */

function base64UrlDecode(segment: string): string {
  const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
  const paddingLength = (4 - (base64.length % 4)) % 4;
  const padded = base64 + '='.repeat(paddingLength);
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

/**
 * Reads the `sub` claim (the user's own id — `AccessTokenGenerator.cs`'s
 * `JwtRegisteredClaimNames.Sub`) out of a raw JWT access token.
 *
 * Deliberately does NOT attempt to read email/display name/anything else: per
 * `AccessTokenGenerator.cs`'s own doc comment, this backend's access tokens carry only
 * `sub`/`nameidentifier`/`jti`/`sid`/`role` claims and intentionally no PII — there is nothing else
 * here to decode. See the P0-35 final report's "gaps" section for what that means for the frontend
 * (no display name/email available after login without a future "who am I" endpoint).
 *
 * Returns `null` for any malformed/unexpected input rather than throwing — a decode failure here
 * must never crash the app; it just means `AuthService.currentUser` reports `null`.
 */
export function decodeAccessTokenUserId(token: string): string | null {
  try {
    const segments = token.split('.');
    const payloadSegment = segments[1];
    if (!payloadSegment) {
      return null;
    }
    const payload: unknown = JSON.parse(base64UrlDecode(payloadSegment));
    if (typeof payload !== 'object' || payload === null) {
      return null;
    }
    const sub = (payload as Record<string, unknown>)['sub'];
    return typeof sub === 'string' && sub.length > 0 ? sub : null;
  } catch {
    return null;
  }
}
