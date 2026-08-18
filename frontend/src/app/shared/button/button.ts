import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';
export type ButtonType = 'button' | 'submit' | 'reset';

/**
 * Base action button for the shared UI kit (P0-34).
 *
 * Renders a single native `<button>` — clicks bubble from it to the host
 * element exactly like any native DOM event, so consumers bind `(click)`
 * directly on `<app-button>` the same way they would on a plain `<button>`;
 * no separate `output()` is needed for something the platform already gives
 * us for free, and `disabled`/`loading` correctly suppress the click at the
 * DOM level (not just visually) because they set the real `disabled`
 * attribute on that native element.
 *
 * Leading/trailing icons are content-projected (`[leading]` / `[trailing]`
 * attribute slots) rather than baked in as an icon-name input, so this
 * component stays icon-library-agnostic — drop an `<ng-icon leading .../>`
 * (or any SVG) in.
 */
@Component({
  selector: 'app-button',
  imports: [TranslatePipe],
  templateUrl: './button.html',
  styleUrl: './button.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-block',
  },
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<ButtonType>('button');
  readonly disabled = input(false);
  readonly loading = input(false);
  readonly fullWidth = input(false);

  /** True while the button must not react to clicks — loading counts as disabled, not just a visual dim. */
  protected readonly isInactive = computed(() => this.disabled() || this.loading());

  protected readonly sizeClasses = computed<string>(() => {
    switch (this.size()) {
      case 'sm':
        return 'h-11 gap-1.5 px-3 text-sm';
      case 'lg':
        return 'h-14 gap-2.5 px-6 text-base';
      case 'md':
      default:
        return 'h-12 gap-2 px-4 text-base';
    }
  });

  protected readonly variantClasses = computed<string>(() => {
    switch (this.variant()) {
      case 'secondary':
        return 'border border-[var(--color-border)] bg-[var(--color-surface)] text-[var(--color-text)] hover:bg-[var(--color-surface-muted)] active:bg-[var(--color-surface-muted)]';
      case 'danger':
        // No dedicated "on-danger" token exists in tokens.css; reusing
        // --color-primary-foreground (pure white in both themes) is
        // deliberate — it is already this codebase's "text on a saturated
        // brand surface" token, and danger/primary buttons share that need.
        return 'bg-[var(--color-danger)] text-[var(--color-primary-foreground)] hover:brightness-90 active:brightness-95';
      case 'ghost':
        return 'bg-transparent text-[var(--color-primary)] hover:bg-[var(--color-surface-muted)] active:bg-[var(--color-surface-muted)]';
      case 'primary':
      default:
        return 'bg-[var(--color-primary)] text-[var(--color-primary-foreground)] hover:bg-[var(--color-primary-hover)] active:bg-[var(--color-primary-hover)]';
    }
  });
}
