import { ChangeDetectionStrategy, Component, afterNextRender, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiError } from '../../../core/http/error.interceptor';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { AuthApiService } from '../data/auth-api.service';

type ConfirmState = 'loading' | 'success' | 'error' | 'missingToken';

/**
 * `/confirm-email?token=...` — `POST /api/identity/confirm-email`.
 * <para>
 * <b>Why the actual API call is deferred to `afterNextRender`, not fired from the constructor/
 * `ngOnInit`</b> — the one genuinely important architectural decision on this page. `ConfirmEmail`'s
 * own backend doc comment (`Features/ConfirmEmail/Command.cs`) explains it is POST, not GET,
 * specifically so an email client's link-prefetch/security scanner can never silently consume a
 * one-time confirmation token just by generating a link preview of the *email* — but that protection
 * assumes whoever fetches THIS page's own URL doesn't also blindly fire the POST. If this component
 * called `confirmEmail(token)` unconditionally during SSR (Angular still executes component code
 * once, server-side, to produce the initial HTML — see `app.routes.server.ts`'s comment on why this
 * route stays `RenderMode.Server`), then any HTTP fetch of `/confirm-email?token=...` — including
 * Googlebot, a corporate email-security scanner, a Slack/Discord unfurl bot, or simply a browser
 * prerendering the link before the user clicks it — would trigger the server render, which would
 * trigger this call, which would consume the one-time token before the real user's browser ever
 * loads the page. `afterNextRender` runs exactly once, client-side only, after the first real
 * browser paint — the correct place for a side-effecting, one-time action gated on "a real browser
 * is actually looking at this", independent of whether the route's shell itself is SSR or CSR.
 * </para>
 */
@Component({
  selector: 'app-confirm-email-page',
  imports: [RouterLink, TranslatePipe, Skeleton],
  templateUrl: './confirm-email-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmailPage {
  private readonly authApi = inject(AuthApiService);
  private readonly route = inject(ActivatedRoute);
  protected readonly translation = inject(TranslationService);

  protected readonly state = signal<ConfirmState>('loading');
  protected readonly resultMessage = signal<string | null>(null);

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.confirmEmail.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.confirmEmail.pageDescription'),
      robots: 'noindex',
    });

    afterNextRender(() => {
      const token = this.route.snapshot.queryParamMap.get('token');
      if (!token) {
        this.state.set('missingToken');
        return;
      }

      this.authApi.confirmEmail(token).subscribe({
        next: (response) => {
          this.resultMessage.set(response.message);
          this.state.set('success');
        },
        error: (error: ApiError) => {
          this.resultMessage.set(error.message === 'common.error' ? this.translation.t('common.error') : error.message);
          this.state.set('error');
        },
      });
    });
  }
}
