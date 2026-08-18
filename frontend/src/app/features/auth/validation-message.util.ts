import { AbstractControl } from '@angular/forms';

import { TranslationService } from '../../core/i18n/translation.service';

/**
 * Maps a reactive-forms `AbstractControl`'s built-in `Validators.*` errors to a translated i18n
 * string, gated on `touched` (frontend.md: "แสดงเมื่อ touched/dirty แล้วเท่านั้น" — never show a red
 * border before the person has had a chance to interact with the field).
 *
 * Deliberately only maps the four built-in Angular validators actually used by this feature's forms
 * (`required`, `email`, `minlength`, `maxlength` — Angular's own error-key casing, not this
 * codebase's camelCase convention). Server-side field errors (e.g. the password denylist, which has
 * no client-side equivalent — see RegisterPage's own comment for why) are a separate concern, merged
 * in by each page's own `fieldError()` call (`data/field-error.util.ts`), not this function.
 */
export function validationMessage(control: AbstractControl | null, translation: TranslationService): string | undefined {
  if (!control || !control.errors || !control.touched) {
    return undefined;
  }

  if (control.errors['required']) {
    return translation.t('auth.validation.required');
  }
  if (control.errors['email']) {
    return translation.t('auth.validation.email');
  }
  if (control.errors['minlength']) {
    const requiredLength = (control.errors['minlength'] as { requiredLength: number }).requiredLength;
    return translation.t('auth.validation.minLength', { min: requiredLength });
  }
  if (control.errors['maxlength']) {
    const requiredLength = (control.errors['maxlength'] as { requiredLength: number }).requiredLength;
    return translation.t('auth.validation.maxLength', { max: requiredLength });
  }

  return undefined;
}
