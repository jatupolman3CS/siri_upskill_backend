import { inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

/**
 * Small shared helper so every route sets a real `<title>`/meta description instead of the app's
 * default (frontend.md: "หน้า public ... ต้องตั้ง title/meta ... ครบ"), without six near-identical
 * `inject(Title)`/`inject(Meta)` call sites. `Title`/`Meta` are themselves SSR-safe (they write to
 * the server-rendered document during SSR and the real one after hydration), so no
 * `isPlatformBrowser` guard is needed here.
 * <para>
 * Must be called from an injection context (a component constructor/field initializer) — same rule
 * as any other `inject()` call.
 * </para>
 * <para>
 * `robots`, when passed `'noindex'`, marks a page that must never appear in search results — used by
 * `ConfirmEmailPage`/`ResetPasswordPage` (their URLs carry a one-time secret token in the query
 * string) and `DevicesPage` (session-bound, not a public page at all). Omitted entirely for pages
 * that ARE meant to be indexable (Register/Login/ForgotPassword) rather than emitting an explicit
 * `index` value — omission is already the correct, unambiguous default for those.
 * </para>
 */
export function setPageMeta(config: { readonly title: string; readonly description: string; readonly robots?: 'noindex' }): void {
  const titleService = inject(Title);
  const metaService = inject(Meta);

  titleService.setTitle(config.title);
  metaService.updateTag({ name: 'description', content: config.description });

  if (config.robots === 'noindex') {
    metaService.updateTag({ name: 'robots', content: 'noindex, nofollow' });
  }
}
