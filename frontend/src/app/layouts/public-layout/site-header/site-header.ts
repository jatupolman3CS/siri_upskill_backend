import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorList } from '@ng-icons/phosphor-icons/regular';

import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Locale, TranslationService } from '../../../core/i18n/translation.service';
import { MobileNav } from '../mobile-nav/mobile-nav';

/**
 * Sticky site header for `PublicLayout` (P0-33) — wordmark, desktop nav,
 * auth-aware actions (Login/Register vs Devices/Sign out), a TH/EN toggle,
 * and the hamburger trigger for `MobileNav` below the `md` breakpoint.
 * <para>
 * `mobileNavOpen` lives here rather than in `PublicLayout` because this
 * header is the only thing that ever opens it — keeping the state colocated
 * with its one trigger avoids threading a signal through an extra layer for
 * no reason.
 * </para>
 * <para>
 * The Login/Register actions are plain `<a routerLink>` elements styled to
 * match `app-button`'s ghost/primary variants (`sm` size), not
 * `<app-button>` itself — `Button` always renders a real `<button>`
 * internally, and nesting a `<button>` inside an `<a>` is invalid HTML
 * (interactive content inside interactive content). Same reasoning Modal's
 * and ToastHost's own icon-only buttons already follow for staying on a raw
 * native element instead of a wrapper component when the wrapper doesn't
 * fit.
 * </para>
 */
@Component({
  selector: 'app-site-header',
  imports: [RouterLink, RouterLinkActive, TranslatePipe, NgIcon, MobileNav],
  templateUrl: './site-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ phosphorList })],
})
export class SiteHeader {
  protected readonly authService = inject(AuthService);
  protected readonly translation = inject(TranslationService);
  private readonly router = inject(Router);

  protected readonly mobileNavOpen = signal(false);
  /** Must match the `id` on mobile-nav.html's dialog panel — referenced by this header's hamburger
   *  trigger via `aria-controls`. Hardcoded on both sides rather than threaded through an input:
   *  `MobileNav` only ever has this one call site (here), so there is exactly one place either string
   *  could drift, and a shared constant module for a single two-file id isn't worth the indirection. */
  protected readonly mobileNavPanelId = 'app-mobile-nav-panel';

  protected setLocale(locale: Locale): void {
    this.translation.setLocale(locale);
  }

  /** First real UI wiring of `AuthService.clearSession()` — see that method's own doc comment. */
  protected signOut(): void {
    this.authService.clearSession();
    void this.router.navigateByUrl('/');
  }
}
