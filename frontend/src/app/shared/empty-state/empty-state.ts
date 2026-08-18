import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorEmpty } from '@ng-icons/phosphor-icons/regular';

/**
 * "Nothing here yet" / "no results" placeholder for the shared UI kit
 * (P0-34). Per ui-design.md's catalog/search guidance
 * ("empty state บอกทางไปต่อ" — an empty state must point somewhere, not
 * just report absence), `description` and the `[emptyStateAction]` slot
 * are first-class here, not afterthoughts; consumers should almost always
 * fill at least one of them.
 *
 * Icon: `icon="default"` (the default) shows Phosphor's `Empty` glyph — a
 * literal "there is nothing here" mark, apt for the generic case. Pass
 * `icon="none"` and project a custom icon/illustration into the
 * `[emptyStateIcon]` slot for a page-specific visual instead. (Angular's
 * `<ng-content>` has no native "fallback content when nothing was
 * projected" mechanism the way a web-component `<slot>` does, so this is
 * an explicit input rather than auto-detected projection.)
 */
@Component({
  selector: 'app-empty-state',
  imports: [NgIcon],
  templateUrl: './empty-state.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ phosphorEmpty })],
  host: {
    class: 'block',
  },
})
export class EmptyState {
  readonly icon = input<'default' | 'none'>('default');
  readonly heading = input.required<string>();
  readonly description = input<string | undefined>(undefined);
}
