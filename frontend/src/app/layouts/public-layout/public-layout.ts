import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * PLACEHOLDER shell for public, SEO-facing pages (home, catalog, course
 * detail, blog — see ARCHITECTURE.md section 3). Real header/nav/mega-menu/
 * footer chrome is a follow-up task once the design system exists; for now
 * this only routes to the page content so SSR/routing can be verified.
 */
@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicLayout {}
