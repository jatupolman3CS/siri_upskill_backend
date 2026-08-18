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
import { RouterLink } from '@angular/router';

import { ApiError } from '../../../core/http/error.interceptor';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';
import { Button } from '../../../shared/button/button';
import { InputField } from '../../../shared/input/input';
import { AuthApiService } from '../data/auth-api.service';
import { fieldError } from '../data/field-error.util';
import { validationMessage } from '../validation-message.util';

const MAX_EMAIL_LENGTH = 256;

interface ForgotPasswordForm {
  email: FormControl<string>;
}

/**
 * `/forgot-password` — `POST /api/identity/forgot-password`.
 * <para>
 * <b>Anti-enumeration integrity</b>, same discipline as `RegisterPage`: on success this renders
 * `ForgotPasswordResponse.message` verbatim — `ForgotPasswordHandler.cs` returns this exact string
 * whether or not the email belongs to a real, active account, and this component adds no branch that
 * could tell the two cases apart or word them differently.
 * </para>
 */
@Component({
  selector: 'app-forgot-password-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, InputField, Button],
  templateUrl: './forgot-password-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordPage {
  private readonly authApi = inject(AuthApiService);
  protected readonly translation = inject(TranslationService);

  private readonly errorSummary = viewChild<ElementRef<HTMLElement>>('errorSummary');

  protected readonly form = new FormGroup<ForgotPasswordForm>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(MAX_EMAIL_LENGTH)],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly showClientErrorSummary = signal(false);

  private readonly focusRequestId = signal(0);
  private lastFocusedRequestId = 0;

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.forgotPassword.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.forgotPassword.pageDescription'),
    });

    afterRenderEffect(() => {
      const requestId = this.focusRequestId();
      if (requestId !== this.lastFocusedRequestId) {
        this.lastFocusedRequestId = requestId;
        this.errorSummary()?.nativeElement.focus();
      }
    });
  }

  protected emailError(): string | undefined {
    return validationMessage(this.form.controls.email, this.translation) ?? fieldError(this.apiError(), 'Email');
  }

  protected formLevelError(): string | null {
    const error = this.apiError();
    if (!error || fieldError(error, 'Email')) {
      return null;
    }
    return error.message === 'common.error' ? this.translation.t('common.error') : error.message;
  }

  protected onSubmit(): void {
    if (this.submitting()) {
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

    this.authApi.forgotPassword(this.form.getRawValue().email).subscribe({
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
