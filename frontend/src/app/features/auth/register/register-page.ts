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

import { setPageMeta } from '../../../core/seo/page-meta';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { ApiError } from '../../../core/http/error.interceptor';
import { Button } from '../../../shared/button/button';
import { InputField } from '../../../shared/input/input';
import { AuthApiService } from '../data/auth-api.service';
import { fieldError } from '../data/field-error.util';
import { validationMessage } from '../validation-message.util';

/** Mirrors `RegisterValidator`'s exact rules (`Features/Register/Validator.cs`) so the UI catches
 *  the same length problems the server would reject anyway, instead of surprising the user with a
 *  round-trip failure for something client-side validation could have caught. The denylist rule is
 *  deliberately NOT duplicated here — see the class doc comment below. */
const MIN_PASSWORD_LENGTH = 10;
const MAX_PASSWORD_LENGTH = 128;
const MIN_DISPLAY_NAME_LENGTH = 2;
const MAX_DISPLAY_NAME_LENGTH = 200;
const MAX_EMAIL_LENGTH = 256;

interface RegisterForm {
  email: FormControl<string>;
  password: FormControl<string>;
  displayName: FormControl<string>;
}

/**
 * `/register` — `POST /api/identity/register`.
 * <para>
 * <b>Anti-enumeration integrity</b> (task's headline requirement for this page): on success, this
 * component renders `RegisterResponse.message` — the exact string `RegisterHandler` returns,
 * unmodified, un-reworded, not conditioned on anything the frontend itself infers about whether the
 * email was "really" new. There is no code path here that shows a different success message, or any
 * message at all differentiated by outcome; the backend's carefully-designed anti-enumeration
 * guarantee (`RegisterHandler.cs`'s own doc comment) is preserved by simply never adding logic that
 * could undermine it.
 * </para>
 * <para>
 * <b>Password denylist — deliberately not duplicated client-side</b> (task: "client-side denylist
 * behavior is optional/nice-to-have"). `RegisterValidator.DisallowedPasswords` is `internal` to
 * `Siri.Modules.Identity` and not exposed via any contract this frontend could read at build time;
 * copying the literal list here would create a second copy that could silently drift from the real
 * one (the exact risk `ResetPasswordValidator`'s own doc comment calls out for why IT doesn't
 * duplicate rules either). Length/required rules ARE duplicated (they're cheap, public constants'
 * *values*, and catching them client-side avoids a wasted round trip); the denylist is left to the
 * server, whose rejection surfaces via `fieldError()` exactly like any other field validation error.
 * </para>
 */
@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, InputField, Button],
  templateUrl: './register-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly authApi = inject(AuthApiService);
  protected readonly translation = inject(TranslationService);

  private readonly errorSummary = viewChild<ElementRef<HTMLElement>>('errorSummary');

  protected readonly form = new FormGroup<RegisterForm>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(MAX_EMAIL_LENGTH)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH), Validators.maxLength(MAX_PASSWORD_LENGTH)],
    }),
    displayName: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.minLength(MIN_DISPLAY_NAME_LENGTH),
        Validators.maxLength(MAX_DISPLAY_NAME_LENGTH),
      ],
    }),
  });

  protected readonly submitting = signal(false);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly showClientErrorSummary = signal(false);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  /** Bumped whenever a failed submit should move focus to the error summary — see
   *  `focusErrorSummary()`'s own comment for why this indirection (an `afterRenderEffect`, not a
   *  direct `.focus()` call) is the correct way to do this in a zoneless, OnPush component. */
  private readonly focusRequestId = signal(0);
  private lastFocusedRequestId = 0;

  constructor() {
    setPageMeta({
      title: `${this.translation.t('auth.register.pageTitle')} · ${this.translation.t('common.appName')}`,
      description: this.translation.t('auth.register.pageDescription'),
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

  protected displayNameError(): string | undefined {
    return validationMessage(this.form.controls.displayName, this.translation) ?? fieldError(this.apiError(), 'DisplayName');
  }

  protected formLevelError(): string | null {
    const error = this.apiError();
    if (!error) {
      return null;
    }
    // A validation-shaped error is already shown per-field above; a form-level line for it too
    // would just repeat the same information twice.
    if (fieldError(error, 'Email') || fieldError(error, 'Password') || fieldError(error, 'DisplayName')) {
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
      this.focusErrorSummary();
      return;
    }

    this.showClientErrorSummary.set(false);
    this.apiError.set(null);
    this.submitting.set(true);

    const { email, password, displayName } = this.form.getRawValue();

    this.authApi.register({ email, password, displayName }).subscribe({
      next: (response) => {
        this.submitting.set(false);
        this.successMessage.set(response.message);
      },
      error: (error: ApiError) => {
        this.submitting.set(false);
        this.apiError.set(error);
        this.focusErrorSummary();
      },
    });
  }

  /**
   * Moving focus has to happen AFTER the error summary element actually exists in the DOM — and
   * because this is an OnPush, zoneless component, setting the signals that make `@if
   * (showErrorSummary())` true does not synchronously render the DOM (rendering is scheduled, not
   * immediate). `afterRenderEffect` (registered once, in the constructor) is the correct tool for
   * "run this after the next real DOM update", so this method only ever bumps a signal it reads —
   * never calls `.focus()` itself.
   */
  private focusErrorSummary(): void {
    this.focusRequestId.update((n) => n + 1);
  }
}
