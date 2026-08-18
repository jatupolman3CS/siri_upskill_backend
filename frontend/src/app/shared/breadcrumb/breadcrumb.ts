import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorCaretRight } from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

export interface BreadcrumbItem {
  readonly label: string;
  /** Omit on an item that shouldn't be a link even though it isn't last (rare, but supported). */
  readonly href?: string;
}

/**
 * Breadcrumb trail for the shared UI kit (P0-34). The *last* item in
 * `items` is always rendered as the current page — a plain, non-link
 * `aria-current="page"` element — regardless of whether it has an `href`,
 * per this task's spec.
 *
 * No truncation strategy for narrow viewports: this app's breadcrumb
 * depth is shallow (catalog → category → course, roughly 2-3 levels — see
 * ARCHITECTURE.md's route tree), so a collapsing/responsive algorithm
 * would be solving a problem this product doesn't have yet. Horizontal
 * overflow is instead handled the cheap, always-correct way — the list
 * scrolls within its own `overflow-x-auto` container — which also keeps
 * the hard "no body-level horizontal scroll" rule satisfied if a future
 * page ever does have a long trail.
 */
@Component({
  selector: 'app-breadcrumb',
  imports: [RouterLink, NgIcon, TranslatePipe],
  templateUrl: './breadcrumb.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ phosphorCaretRight })],
  host: {
    class: 'block',
  },
})
export class Breadcrumb {
  readonly items = input.required<readonly BreadcrumbItem[]>();
}
