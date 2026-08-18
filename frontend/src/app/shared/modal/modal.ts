import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  PLATFORM_ID,
  effect,
  inject,
  input,
  model,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { CdkTrapFocus } from '@angular/cdk/a11y';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorX } from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

let nextModalId = 0;

/**
 * Modal / dialog for the shared UI kit (P0-34) — the highest-stakes a11y
 * component in the kit.
 *
 * FOCUS TRAP — CDK, not hand-rolled: uses `@angular/cdk/a11y`'s
 * `CdkTrapFocus` directive (`[cdkTrapFocus][cdkTrapFocusAutoCapture]`) on
 * the dialog panel, per docs/ARCHITECTURE.md's explicit call-out that CDK
 * a11y primitives are the intended tool for exactly this. With
 * `autoCapture` enabled it, on its own, both (a) moves focus into the
 * panel's first tabbable element the moment it mounts, and (b) restores
 * focus to whatever was focused beforehand the moment it is destroyed —
 * both requirements this task calls out — without this component having to
 * touch `document.activeElement` itself. Tab/Shift+Tab containment is CDK's
 * own well-tested anchor-element mechanism. `@angular/cdk` was not
 * previously a dependency of this workspace; it was added for this task.
 *
 * This component only owns what CDK's directive does *not* do: Escape to
 * close, click-on-overlay to close (optionally disabled, for
 * destructive-confirmation modals), the `role="dialog"` / `aria-modal` /
 * `aria-labelledby` wiring, and an SSR-safe body scroll lock. Content is
 * mounted/unmounted via `@if` on `open()`, so `CdkTrapFocus`'s own
 * lifecycle hooks (`ngAfterContentInit` / `ngOnDestroy`) fire exactly when
 * this component opens/closes — no manual coordination needed.
 */
@Component({
  selector: 'app-modal',
  imports: [NgIcon, TranslatePipe, CdkTrapFocus],
  templateUrl: './modal.html',
  styleUrl: './modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ phosphorX })],
})
export class Modal {
  /** Two-way: parent owns the source of truth, this component only ever sets it to `false`. */
  readonly open = model(false);
  readonly titleText = input.required<string>();
  readonly showCloseButton = input(true);
  /** Destructive-confirmation modals should pass `false` — accidental backdrop dismissal is bad there. */
  readonly closeOnOverlayClick = input(true);

  protected readonly titleId = `app-modal-title-${nextModalId++}`;

  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  /** `null` = scroll is not currently locked by this modal instance. */
  private previousBodyOverflow: string | null = null;

  constructor() {
    effect(() => {
      const isOpen = this.open();
      if (!this.isBrowser) {
        return;
      }
      if (isOpen && this.previousBodyOverflow === null) {
        this.previousBodyOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
      } else if (!isOpen && this.previousBodyOverflow !== null) {
        document.body.style.overflow = this.previousBodyOverflow;
        this.previousBodyOverflow = null;
      }
    });

    inject(DestroyRef).onDestroy(() => {
      if (this.isBrowser && this.previousBodyOverflow !== null) {
        document.body.style.overflow = this.previousBodyOverflow;
      }
    });
  }

  protected requestClose(): void {
    this.open.set(false);
  }

  protected onOverlayClick(event: MouseEvent): void {
    if (event.target !== event.currentTarget) {
      return;
    }
    if (this.closeOnOverlayClick()) {
      this.requestClose();
    }
  }
}
