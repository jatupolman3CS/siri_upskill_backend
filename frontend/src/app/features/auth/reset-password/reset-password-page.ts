import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiError } from '../../../core/http/error.interceptor';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';
import { Button } from '../../../shared/button/button';
import { InputField } from '../../../shared/input/input';
import { AuthApiService } from '../data/auth-api.service';
import { fieldError } from '../data/field-error.util';
import { validationMessage } from '../validation-message.util';

/** Matches `RegisterValidator`'s password-length rules, reused server-side by
 *  `ResetPasswordValidator` (`Features/ResetPassword/Validator.cs`'s own doc comment explains why it
 *  references `RegisterValidator`'s constants directly rather than duplicating the values — this
 *  frontend does the analogous thing by naming this constant after the same source of truth,
 *  even though TypeScript can't literally import a C# constant). Denylist not duplicated client-side
 *  — same reasoning as `RegisterPage`. */
const MIN_PASSWORD_LENGTH = 10;
const MAX_PASSWORD_LENGTH = 128;

interface ResetPasswordForm {
  newPassword: FormControl<string>;
}

/**
 * `/reset-password?token=...` — `POST /api/identity/reset-password`.
 * <para>
 * Unlike `ConfirmEmailPage`, this page does NOT auto-fire an API call on load — redeeming a reset
 * token requires a new password from the user first, so there is no side-effecting network call to
 * defer past SSR here in the first place (reading `token` off the URL to decide "show the form or
 * the missing-token message" has no side effect; the actual `resetPassword()` call only ever fires
 * from a user-submitted form, which is inherently browser-only). Token presence is still checked
 * up front so a malformed/copy-pasted-wrong link shows a clear error instead of a confusing "your
 * new password was rejected" surprise.
 * </para>
 * <para>
 * On a rejected token, this renders `ResetPasswordHandler`'s generic `InvalidTokenError` message
 * as-is — identical whether the token never existed, already expired, was already used, or points at
 * a since-deactivated account (see that handler's own anti-enumeration doc comment); no client-side
 * logic here tries to guess which.
 * </para>
 */
@Component({
  selector: 'app-reset-password-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, InputField, Button],
  templateUrl: './reset-password-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordPage {
  private readonly authApi = inject(AuthApiService);
  private readonly route = inject(ActivatedRoute);
  protected readonly translation = inject(TranslationService);

  private readonly errorSummary = viewChild<ElementRef<HTMLElement>>('errorSummary');

  protected readonly token = this.route.snapshot.queryParamMap.get('token');

  protected readonly form = new FormGroup<ResetPasswordForm>({
    newPassword: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH), Validators.maxLength(MAX_PASSWORD_LENGTH)],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly showClientErrorSummary = signal(false);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  private readonly focusRequestId = signal(0);
  private lastFocusedRequestId = 0;

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.resetPassword.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.resetPassword.pageDescription'),
      robots: 'noindex',
    });

    afterRenderEffect(() => {
      const requestId = this.focusRequestId();
      if (requestId !== this.lastFocusedRequestId) {
        this.lastFocusedRequestId = requestId;
        this.errorSummary()?.nativeElement.focus();
      }
    });
  }

  protected newPasswordError(): string | undefined {
    return validationMessage(this.form.controls.newPassword, this.translation) ?? fieldError(this.apiError(), 'NewPassword');
  }

  protected formLevelError(): string | null {
    const error = this.apiError();
    if (!error || fieldError(error, 'NewPassword')) {
      return null;
    }
    return error.message === 'common.error' ? this.translation.t('common.error') : error.message;
  }

  protected onSubmit(): void {
    if (this.submitting() || !this.token) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.showClientErrorSummary.set(true);
      this.apiError.set(null);
      this.focusRequestId.update((n) => n + 1);
      return;
    }

    this.showClientErrorSummary.set(false);
    this.apiError.set(null);
    this.submitting.set(true);

    this.authApi.resetPassword({ token: this.token, newPassword: this.form.getRawValue().newPassword }).subscribe({
      next: (response) => {
        this.submitting.set(false);
        this.successMessage.set(response.message);
      },
      error: (error: ApiError) => {
        this.submitting.set(false);
        this.apiError.set(error);
        this.focusRequestId.update((n) => n + 1);
      },
    });
  }
}
