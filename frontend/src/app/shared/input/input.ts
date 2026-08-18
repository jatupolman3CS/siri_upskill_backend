import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

export type InputFieldType = 'text' | 'email' | 'password' | 'number' | 'tel' | 'search' | 'url';

let nextInputId = 0;

/**
 * Labeled text field for the shared UI kit (P0-34).
 *
 * Implements `ControlValueAccessor` (the current, non-experimental way to
 * plug a custom control into Angular's reactive forms) rather than Signal
 * Forms — frontend.md pins this codebase to "typed reactive forms เท่านั้น"
 * (`ReactiveFormsModule` / `FormControl`), and Angular's Signal Forms are
 * still experimental, so CVA is the correct choice here, not a fallback.
 *
 * Label is always rendered as a real `<label>` (never placeholder-only, per
 * ui-design.md) and is associated to the input via a generated `id` /
 * `for` pair. Error text, when present, replaces the helper text in place
 * and is wired up via `aria-describedby` + `aria-invalid` so assistive tech
 * gets both, not just a red border.
 */
@Component({
  selector: 'app-input',
  imports: [TranslatePipe],
  templateUrl: './input.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => InputField),
      multi: true,
    },
  ],
  host: {
    class: 'block',
  },
})
export class InputField implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly type = input<InputFieldType>('text');
  readonly placeholder = input<string | undefined>(undefined);
  readonly helperText = input<string | undefined>(undefined);
  readonly errorMessage = input<string | undefined>(undefined);
  readonly required = input(false);
  readonly autocomplete = input<string | undefined>(undefined);
  /** Standalone (non-forms) disabled override; ORed with CVA's setDisabledState. */
  readonly disabled = input(false);

  protected readonly fieldId = `app-input-${nextInputId++}`;
  protected readonly helperId = `${this.fieldId}-helper`;
  protected readonly errorId = `${this.fieldId}-error`;

  protected readonly value = signal('');
  private readonly formDisabled = signal(false);
  protected readonly isDisabled = computed(() => this.disabled() || this.formDisabled());
  protected readonly hasError = computed(() => !!this.errorMessage());

  protected readonly describedBy = computed<string | null>(() => {
    if (this.hasError()) {
      return this.errorId;
    }
    if (this.helperText()) {
      return this.helperId;
    }
    return null;
  });

  // Concise-body arrows (no `{}` block) so these CVA no-op defaults don't
  // trip @typescript-eslint/no-empty-function; real handlers are wired in
  // via registerOnChange/registerOnTouched before Angular ever calls these.
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }

  protected handleInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.value.set(value);
    this.onChange(value);
  }

  protected handleBlur(): void {
    this.onTouched();
  }
}
