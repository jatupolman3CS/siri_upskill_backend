import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  phosphorCheckCircle,
  phosphorInfo,
  phosphorWarningCircle,
  phosphorX,
  phosphorXCircle,
} from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ToastService, ToastType } from './toast.service';

const ICON_BY_TYPE: Record<ToastType, string> = {
  success: 'phosphorCheckCircle',
  error: 'phosphorXCircle',
  info: 'phosphorInfo',
  warning: 'phosphorWarningCircle',
};

const COLOR_CLASS_BY_TYPE: Record<ToastType, string> = {
  success: 'text-[var(--color-success)]',
  error: 'text-[var(--color-danger)]',
  info: 'text-[var(--color-primary)]',
  warning: 'text-[var(--color-warning)]',
};

const LABEL_KEY_BY_TYPE: Record<ToastType, string> = {
  success: 'shared.toast.typeSuccess',
  error: 'shared.toast.typeError',
  info: 'shared.toast.typeInfo',
  warning: 'shared.toast.typeWarning',
};

/**
 * Renders the active toast stack. Mount exactly one of these near the app
 * root (see `app.html`) — `ToastService` is a singleton, so multiple hosts
 * would just render the same stack twice.
 *
 * Each toast card is its own live region (`role`/`aria-live` set per item
 * — "status"/"polite" for success/info/warning, "alert"/"assertive" for
 * error) rather than one shared live region for the whole stack: the
 * politeness genuinely varies per toast, and a freshly-inserted element
 * that already carries its final text plus its own live-region role is the
 * pattern the ARIA Authoring Practices use for one-shot alerts — it avoids
 * the reliability problems of adding text into an already-mounted empty
 * live region.
 */
@Component({
  selector: 'app-toast-host',
  imports: [NgIcon, TranslatePipe],
  templateUrl: './toast-host.html',
  styleUrl: './toast-host.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    provideIcons({ phosphorCheckCircle, phosphorInfo, phosphorWarningCircle, phosphorX, phosphorXCircle }),
  ],
  host: {
    class: 'contents',
  },
})
export class ToastHost {
  protected readonly toastService = inject(ToastService);

  protected iconFor(type: ToastType): string {
    return ICON_BY_TYPE[type];
  }

  protected colorClassFor(type: ToastType): string {
    return COLOR_CLASS_BY_TYPE[type];
  }

  protected labelKeyFor(type: ToastType): string {
    return LABEL_KEY_BY_TYPE[type];
  }

  protected roleFor(type: ToastType): 'alert' | 'status' {
    return type === 'error' ? 'alert' : 'status';
  }

  protected ariaLiveFor(type: ToastType): 'assertive' | 'polite' {
    return type === 'error' ? 'assertive' : 'polite';
  }
}
