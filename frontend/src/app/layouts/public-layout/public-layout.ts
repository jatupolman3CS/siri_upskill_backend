import { ChangeDetectionStrategy, Component, ElementRef, PLATFORM_ID, inject, viewChild } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SiteFooter } from './site-footer/site-footer';
import { SiteHeader } from './site-header/site-header';

/**
 * Real shell for public, SEO-facing pages (P0-33) — home + all auth pages today; catalog/course
 * detail/blog land in later features (see ARCHITECTURE.md section 3), reusing this same layout.
 * Header/footer/skip-link/focus-on-route-change all live here now; `learn`/`instructor`/`admin`
 * layouts (siblings of this file) stay placeholder shells on purpose — no feature exists yet to put
 * in their nav, so building real chrome for them now would just be fabricated links.
 */
@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, TranslatePipe, SiteHeader, SiteFooter],
  templateUrl: './public-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicLayout {
  private readonly router = inject(Router);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly mainContent = viewChild<ElementRef<HTMLElement>>('mainContent');

  constructor() {
    if (!this.isBrowser) {
      return;
    }

    // Move focus to #main-content after an in-app route change, so screen-reader/keyboard users
    // aren't left anchored wherever focus happened to be on the previous page (WCAG "focus order
    // after navigation"). Skips the first NavigationEnd (initial load/hydration) — on first paint,
    // focus should land wherever the browser puts it naturally, not get yanked to #main-content.
    let isFirstNavigation = true;
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (isFirstNavigation) {
          isFirstNavigation = false;
          return;
        }
        this.mainContent()?.nativeElement.focus();
      });
  }
}
