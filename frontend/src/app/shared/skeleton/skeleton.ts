import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type SkeletonVariant = 'text' | 'block';

/**
 * Loading placeholder shape for the shared UI kit (P0-34). Two variants:
 * `text` (one or more stacked line placeholders) and `block` (a
 * card/thumbnail-shaped rectangle, size set via `width`/`height`).
 *
 * Always `aria-hidden="true"` — a skeleton is a purely visual loading cue;
 * per this task's own framing, the goal is specifically to *avoid*
 * screen-reader noise while content loads, not to announce placeholders.
 * If a screen has multiple skeletons loading together and a consumer wants
 * one announcement for the whole group, that group should have its own
 * single `aria-busy`/`aria-live` region wrapping them — that is the
 * consuming page's job, not something N individual skeleton instances
 * should each try to do.
 *
 * The shimmer sweep animates `transform` only (never `width`/`left`, per
 * ui-design.md's anti-pattern list) and collapses to a static muted block
 * under `prefers-reduced-motion: reduce`.
 */
@Component({
  selector: 'app-skeleton',
  templateUrl: './skeleton.html',
  styleUrl: './skeleton.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'block',
    'aria-hidden': 'true',
  },
})
export class Skeleton {
  readonly variant = input<SkeletonVariant>('text');
  readonly width = input('100%');
  readonly height = input<string | undefined>(undefined);
  readonly radius = input<'sm' | 'md' | 'lg' | 'full'>('md');
  /** Number of stacked lines — `text` variant only. */
  readonly lines = input(1);

  protected readonly resolvedHeight = computed(() => this.height() ?? (this.variant() === 'text' ? '0.875rem' : '8rem'));

  protected readonly radiusVar = computed(() => `var(--radius-${this.radius()})`);

  protected readonly lineNumbers = computed(() => Array.from({ length: Math.max(1, this.lines()) }, (_, i) => i));
}
