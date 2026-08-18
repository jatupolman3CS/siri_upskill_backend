import { DatePipe, registerLocaleData } from '@angular/common';
import localeTh from '@angular/common/locales/th';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/http/error.interceptor';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';
import { Button } from '../../../shared/button/button';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { Modal } from '../../../shared/modal/modal';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { ToastService } from '../../../shared/toast/toast.service';
import { AuthApiService } from '../data/auth-api.service';
import { SessionSummary } from '../data/auth-api.models';

type LoadState = 'loading' | 'error' | 'success';

// Registered here (this page's sole consumer of 'th-TH' date formatting) rather than only in
// app.config.ts — a module-level side effect at the actual point of use, so this page's own
// unit tests (which mount just this component, not the full bootstrapped app) also get correct
// locale data without depending on bootstrap ordering elsewhere. Idempotent: registering the same
// locale twice (once here, harmlessly again if app.config.ts's own bootstrap-time registration also
// runs) is a no-op overwrite, not an error.
registerLocaleData(localeTh);

/**
 * `/account/devices` — authenticated (guarded by `authGuard`, see `app.routes.ts`). Backs
 * docs/SECURITY.md §2's "ผู้ใช้ดู/ถอดอุปกรณ์เองได้ที่หน้า 'อุปกรณ์ที่เข้าสู่ระบบ'" via
 * `GET /sessions`, `DELETE /sessions/{id}`, `POST /sessions/revoke-others`, `POST /sessions/revoke-all`.
 * <para>
 * <b>Every destructive action is confirm-gated</b> (task requirement): the per-row "sign out" button,
 * "sign out other devices", and "sign out everywhere" buttons each only ever open a
 * `Modal` (`closeOnOverlayClick="false"`, per that component's own doc comment for
 * destructive-confirmation modals) and set a pending-action signal; the actual `AuthApiService` call
 * only ever happens from that modal's own confirm button. Canceling/closing any of the three modals
 * leaves `sessions()` and the backend completely untouched — verified directly in
 * `devices-page.spec.ts`.
 * </para>
 * <para>
 * Revoking a session that turns out to have been THIS one (`wasCurrentSession`/the unconditional
 * "revoke all" action) immediately clears this tab's own in-memory access token
 * (`AuthService.clearSession`) and navigates to `/login` — continuing to show a device list fetched
 * with a token whose backing session was just revoked would be misleading (the token itself stays
 * technically valid for the rest of its ~15-minute lifetime; see `RevokeSessionHandler.cs`'s own doc
 * comment on that accepted, pre-existing gap).
 * </para>
 */
@Component({
  selector: 'app-devices-page',
  imports: [TranslatePipe, Button, Modal, EmptyState, Skeleton],
  providers: [DatePipe],
  templateUrl: './devices-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevicesPage {
  private readonly authApi = inject(AuthApiService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly datePipe = inject(DatePipe);
  protected readonly translation = inject(TranslationService);

  protected readonly loadState = signal<LoadState>('loading');
  protected readonly loadErrorMessage = signal<string | null>(null);
  protected readonly sessions = signal<readonly SessionSummary[]>([]);
  protected readonly actionInFlight = signal(false);

  protected readonly sessionPendingRevoke = signal<SessionSummary | null>(null);
  protected readonly revokeModalOpen = signal(false);
  protected readonly revokeOthersModalOpen = signal(false);
  protected readonly revokeAllModalOpen = signal(false);

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.devices.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.devices.pageDescription'),
      robots: 'noindex',
    });

    this.loadSessions();
  }

  protected formatDateTime(iso: string): string {
    return this.datePipe.transform(iso, 'medium', 'Asia/Bangkok', 'th-TH') ?? iso;
  }

  protected deviceLabel(session: SessionSummary): string {
    return session.deviceName ?? this.translation.t('auth.devices.unknownDevice');
  }

  protected loadSessions(): void {
    this.loadState.set('loading');
    this.authApi.listSessions().subscribe({
      next: (response) => {
        this.sessions.set(response.sessions);
        this.loadState.set('success');
      },
      error: (error: ApiError) => {
        this.loadErrorMessage.set(error.message === 'common.error' ? this.translation.t('common.error') : error.message);
        this.loadState.set('error');
      },
    });
  }

  // ---- Single-session revoke ---------------------------------------------------------------

  protected openRevokeConfirm(session: SessionSummary): void {
    this.sessionPendingRevoke.set(session);
    this.revokeModalOpen.set(true);
  }

  protected confirmRevoke(): void {
    const session = this.sessionPendingRevoke();
    if (!session || this.actionInFlight()) {
      return;
    }

    this.actionInFlight.set(true);
    this.authApi.revokeSession(session.sessionId).subscribe({
      next: (response) => {
        this.actionInFlight.set(false);
        this.revokeModalOpen.set(false);
        this.toast.success(response.message);
        if (response.wasCurrentSession) {
          this.signOutThisTab();
          return;
        }
        this.loadSessions();
      },
      error: (error: ApiError) => this.handleActionError(error),
    });
  }

  // ---- Revoke other sessions ----------------------------------------------------------------

  protected confirmRevokeOthers(): void {
    if (this.actionInFlight()) {
      return;
    }
    this.actionInFlight.set(true);
    this.authApi.revokeOtherSessions().subscribe({
      next: (response) => {
        this.actionInFlight.set(false);
        this.revokeOthersModalOpen.set(false);
        this.toast.success(response.message);
        this.loadSessions();
      },
      error: (error: ApiError) => this.handleActionError(error),
    });
  }

  // ---- Revoke every session, including this one ----------------------------------------------

  protected confirmRevokeAll(): void {
    if (this.actionInFlight()) {
      return;
    }
    this.actionInFlight.set(true);
    this.authApi.revokeAllSessions().subscribe({
      next: (response) => {
        this.actionInFlight.set(false);
        this.revokeAllModalOpen.set(false);
        this.toast.success(response.message);
        this.signOutThisTab();
      },
      error: (error: ApiError) => this.handleActionError(error),
    });
  }

  private handleActionError(error: ApiError): void {
    this.actionInFlight.set(false);
    this.toast.error(error.message === 'common.error' ? this.translation.t('common.error') : error.message);
  }

  private signOutThisTab(): void {
    this.authService.clearSession();
    void this.router.navigateByUrl('/login');
  }
}
