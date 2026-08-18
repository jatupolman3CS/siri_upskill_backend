import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorCheck, phosphorMinus } from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

let nextCheckboxId = 0;

/**
 * Labeled checkbox for the shared UI kit (P0-34). `ControlValueAccessor`
 * for `formControlName` compatibility (see input.ts's doc comment for why
 * CVA over Signal Forms in this codebase).
 *
 * Checked / indeterminate state is drawn with an explicit glyph (check
 * mark / dash), not conveyed by fill color alone, per ui-design.md's
 * "ห้ามใช้สีอย่างเดียวสื่อความหมาย" rule. The native `<input
 * type="checkbox">` stays in the DOM and focusable (visually hidden via
 * `sr-only`-style clipping, not `display:none`) so keyboard, screen
 * reader, and native indeterminate-state behaviour all keep working —
 * only the checkbox's *paint* is custom.
 */
@Component({
  selector: 'app-checkbox',
  imports: [NgIcon, TranslatePipe],
  templateUrl: './checkbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    provideIcons({ phosphorCheck, phosphorMinus }),
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => Checkbox),
      multi: true,
    },
  ],
  host: {
    class: 'block',
  },
})
export class Checkbox implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly indeterminate = input(false);
  readonly required = input(false);
  readonly disabled = input(false);

  protected readonly fieldId = `app-checkbox-${nextCheckboxId++}`;

  protected readonly checked = signal(false);
  private readonly formDisabled = signal(false);
  protected readonly isDisabled = computed(() => this.disabled() || this.formDisabled());

  /** Box paint driven explicitly by the checked/indeterminate signals (not
   *  the native :checked/:indeterminate CSS pseudo-classes) so there is one
   *  source of truth instead of two mechanisms that could disagree. */
  protected readonly boxClasses = computed<string>(() =>
    this.checked() || this.indeterminate()
      ? 'border-[var(--color-primary)] bg-[var(--color-primary)]'
      : 'border-[var(--color-border)] bg-[var(--color-bg)]',
  );

  // Concise-body arrows (no `{}` block) so these CVA no-op defaults don't
  // trip @typescript-eslint/no-empty-function; real handlers are wired in
  // via registerOnChange/registerOnTouched before Angular ever calls these.
  private onChange: (value: boolean) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: boolean | null): void {
    this.checked.set(!!value);
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }

  protected handleChange(event: Event): void {
    const value = (event.target as HTMLInputElement).checked;
    this.checked.set(value);
    this.onChange(value);
    this.onTouched();
  }
}
