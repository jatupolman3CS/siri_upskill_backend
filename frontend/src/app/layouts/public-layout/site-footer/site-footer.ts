import { ChangeDetectionStrategy, Component } from '@angular/core';

import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/**
 * Site footer for `PublicLayout` (P0-33) — brand, one-line tagline, and a
 * copyright line. Deliberately no Terms/Privacy links yet — those pages
 * don't exist (P7-09), and a footer link to nowhere is worse than no link.
 */
@Component({
  selector: 'app-site-footer',
  imports: [TranslatePipe],
  templateUrl: './site-footer.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteFooter {
  /** Plain system-clock read, not a browser API — safe during SSR (frontend.md's SSR guard rule is
   *  about `window`/`document`/`localStorage`/`navigator`, none of which this touches). */
  protected readonly year = new Date().getFullYear();
}
