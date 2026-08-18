import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export type ToastType = 'success' | 'error' | 'info' | 'warning';

/** Read-only shape the host component renders. */
export interface ToastMessage {
  readonly id: string;
  readonly type: ToastType;
  readonly message: string;
  readonly durationMs: number;
}

/** Internal bookkeeping — not exposed outside the service. */
interface ToastRecord extends ToastMessage {
  remainingMs: number;
  timerStartedAt: number;
  timeoutHandle: ReturnType<typeof setTimeout> | null;
}

const DEFAULT_DURATION_MS: Record<ToastType, number> = {
  success: 4000,
  info: 4000,
  warning: 5000,
  // Errors get longer on-screen time — they need to actually be read.
  error: 6000,
};

/**
 * Signal-based toast/notification service for the shared UI kit (P0-34).
 * Pair with `ToastHost` (mounted once near the app root, in `app.html`) to
 * actually render active toasts.
 *
 * Timer lifecycle is the trickiest part here (called out explicitly in the
 * task), so it is kept in one small, directly-testable place rather than
 * spread across the host component:
 * - `pause(id)` / `resume(id)` — clears the pending `setTimeout` and
 *   remembers exactly how much time was left, so a user hovering/focusing
 *   a toast to read it never has it disappear mid-read, and resuming
 *   continues the *remaining* time rather than restarting the full
 *   duration.
 * - `setTimeout`/`clearTimeout`/`Date.now()` are only ever touched behind
 *   an `isPlatformBrowser` guard — on the server there is no user to read
 *   a toast, and scheduling real timers during SSR would just leak them
 *   for the lifetime of the server process.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly _toasts = signal<readonly ToastMessage[]>([]);
  readonly toasts = this._toasts.asReadonly();

  private readonly records = new Map<string, ToastRecord>();
  private nextId = 0;

  success(message: string, durationMs?: number): string {
    return this.show('success', message, durationMs);
  }

  error(message: string, durationMs?: number): string {
    return this.show('error', message, durationMs);
  }

  info(message: string, durationMs?: number): string {
    return this.show('info', message, durationMs);
  }

  warning(message: string, durationMs?: number): string {
    return this.show('warning', message, durationMs);
  }

  /** Manual dismissal (close button, or programmatic). Safe to call more than once for the same id. */
  dismiss(id: string): void {
    const record = this.records.get(id);
    if (!record) {
      return;
    }
    this.clearTimer(record);
    this.records.delete(id);
    this._toasts.update((list) => list.filter((toast) => toast.id !== id));
  }

  /** Stop the auto-dismiss countdown, remembering how much time was left. Called on hover/focus. */
  pause(id: string): void {
    const record = this.records.get(id);
    if (!record || record.timeoutHandle === null) {
      return;
    }
    const elapsed = this.isBrowser ? Date.now() - record.timerStartedAt : 0;
    record.remainingMs = Math.max(0, record.remainingMs - elapsed);
    this.clearTimer(record);
  }

  /** Restart the auto-dismiss countdown from wherever `pause` left it. Called on mouse-leave/blur. */
  resume(id: string): void {
    const record = this.records.get(id);
    if (!record || record.timeoutHandle !== null) {
      return;
    }
    this.scheduleTimer(record);
  }

  private show(type: ToastType, message: string, durationMs = DEFAULT_DURATION_MS[type]): string {
    const id = `toast-${this.nextId++}`;
    const record: ToastRecord = {
      id,
      type,
      message,
      durationMs,
      remainingMs: durationMs,
      timerStartedAt: 0,
      timeoutHandle: null,
    };
    this.records.set(id, record);
    this._toasts.update((list) => [...list, { id, type, message, durationMs }]);
    this.scheduleTimer(record);
    return id;
  }

  private scheduleTimer(record: ToastRecord): void {
    if (!this.isBrowser || record.remainingMs <= 0) {
      return;
    }
    record.timerStartedAt = Date.now();
    record.timeoutHandle = setTimeout(() => this.dismiss(record.id), record.remainingMs);
  }

  private clearTimer(record: ToastRecord): void {
    if (record.timeoutHandle !== null) {
      clearTimeout(record.timeoutHandle);
      record.timeoutHandle = null;
    }
  }
}
