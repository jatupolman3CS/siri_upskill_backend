import { RenderMode, ServerRoute } from '@angular/ssr';

// TODO: once /learn, /instructor, /admin routes exist, split them out here
// with RenderMode.Client (+ noindex) per frontend.md — only public pages
// should be RenderMode.Server.
//
// P0-35 auth pages — a deliberate, per-route SSR/CSR split, not a blanket choice:
//
// - /register, /login, /forgot-password: RenderMode.Server. These are genuinely public,
//   unauthenticated-state pages with stable, indexable content (a real product's /login and
//   /signup pages are routinely indexed — "SIRI UpSkill sign up" is a legitimate search query) and
//   nothing about their initial render is session-dependent or side-effecting: the actual
//   register/login/forgot-password API calls only ever fire from a user-submitted form (a browser
//   event), never during the render itself. So SSR gives a faster first paint / SEO entry with zero
//   downside.
//
// - /confirm-email, /reset-password: RenderMode.Server too, but marked `noindex` (see each page's
//   own `setPageMeta` call) — their URLs carry a one-time secret token in the query string, which
//   has no business being indexed or appearing in search results. Both pages' own components
//   explicitly defer their actual token-consuming logic to `afterNextRender()`
//   (`ConfirmEmailPage`) or to a user-submitted form (`ResetPasswordPage`) specifically so the SSR
//   render itself never touches the token — see `ConfirmEmailPage`'s doc comment for why that
//   matters (a server-rendered page is still "fetched" by crawlers/link-preview bots/email security
//   scanners, and `ConfirmEmail`'s one-time token must never be silently consumed by one of those).
//
// - /account/devices: RenderMode.Client — this is this app's one authenticated page (guarded by
//   `authGuard`), same category frontend.md already carves out for /learn, /instructor, /admin
//   ("เป็น CSR + noindex"). SSR fundamentally cannot render this page's real content: the access
//   token lives in memory only (never a cookie/header the server could read — security.md), so
//   every server render of this route would always start from "not authenticated" regardless of the
//   visitor's real session, making an SSR attempt pure wasted work (and `authGuard` would redirect
//   it to /login every time anyway — see that guard's own doc comment).
export const serverRoutes: ServerRoute[] = [
  { path: 'account/devices', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Server },
];
