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
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/http/error.interceptor';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { setPageMeta } from '../../../core/seo/page-meta';
import { Button } from '../../../shared/button/button';
import { InputField } from '../../../shared/input/input';
import { AuthApiService } from '../data/auth-api.service';
import { fieldError } from '../data/field-error.util';
import { validationMessage } from '../validation-message.util';

/** Sanity cap only — matches `LoginValidator`'s own reuse of
 *  `RegisterValidator.MaxPasswordLength` (`Features/Login/Validator.cs`). Login deliberately does
 *  NOT enforce a minimum length client-side (or server-side) — see that validator's own doc comment:
 *  a correct password that predates a policy change must still work. */
const MAX_PASSWORD_LENGTH = 128;
const MAX_EMAIL_LENGTH = 256;

interface LoginForm {
  email: FormControl<string>;
  password: FormControl<string>;
}

/**
 * `/login` — `POST /api/identity/login`.
 * <para>
 * <b>Anti-enumeration integrity</b> (task's headline requirement for this page). `LoginHandler.cs`
 * deliberately returns the exact same `InvalidCredentialsError` message for a nonexistent email, a
 * wrong password, and an account that exists but isn't `Active` yet (unconfirmed/suspended/deleted)
 * — see its own doc comment. This component adds NO client-side logic that tries to tell those cases
 * apart: `formLevelError()` below renders whatever single string `ApiError.message` resolves to,
 * unconditionally, in one shared alert region — never a per-field message next to just the password
 * input (which would visually imply "the email was fine, only the password was wrong", exactly what
 * the backend is designed never to reveal), never a different message for different underlying HTTP
 * bodies. There is deliberately no separate "check if this email exists" call anywhere in this
 * component before or instead of calling `/login` itself.
 * </para>
 */
@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, InputField, Button],
  templateUrl: './login-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly authApi = inject(AuthApiService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly translation = inject(TranslationService);

  private readonly errorSummary = viewChild<ElementRef<HTMLElement>>('errorSummary');

  protected readonly form = new FormGroup<LoginForm>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(MAX_EMAIL_LENGTH)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_PASSWORD_LENGTH)],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly showClientErrorSummary = signal(false);

  private readonly focusRequestId = signal(0);
  private lastFocusedRequestId = 0;

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.login.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.login.pageDescription'),
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

  protected passwordError(): string | undefined {
    return validationMessage(this.form.controls.password, this.translation) ?? fieldError(this.apiError(), 'Password');
  }

  /** The one and only place the backend's generic "invalid email or password" message is rendered
   *  — see this class's doc comment. Never split per field. */
  protected formLevelError(): string | null {
    const error = this.apiError();
    if (!error) {
      return null;
    }
    if (fieldError(error, 'Email') || fieldError(error, 'Password')) {
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

    const { email, password } = this.form.getRawValue();

    this.authApi.login({ email, password }).subscribe({
      next: (response) => {
        this.submitting.set(false);
        this.authService.setAccessToken(response.accessToken);
        // Placeholder authenticated landing (task's own suggestion) — also this app's only
        // authenticated page today, so it doubles as a genuinely useful destination rather than a
        // throwaway "you are logged in" screen.
        void this.router.navigateByUrl('/account/devices');
      },
      error: (error: ApiError) => {
        this.submitting.set(false);
        this.apiError.set(error);
        this.focusRequestId.update((n) => n + 1);
      },
    });
  }
}
