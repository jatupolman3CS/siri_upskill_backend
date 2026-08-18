import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { phosphorCaretDown } from '@ng-icons/phosphor-icons/regular';

import { TranslatePipe } from '../../core/i18n/translate.pipe';

export interface SelectOption {
  readonly value: string;
  readonly label: string;
  readonly disabled?: boolean;
}

let nextSelectId = 0;

/**
 * Labeled single-select for the shared UI kit (P0-34).
 *
 * DECISION — native `<select>`, not a custom listbox/combobox: a
 * hand-built combobox has to re-implement arrow-key navigation, type-ahead,
 * Home/End, Enter/Escape, screen-reader value announcement, and correct
 * behaviour on mobile (where OS pickers are usually better than any custom
 * web widget) — every one of those is easy to get subtly wrong, and this
 * codebase's a11y bar (ui-design.md) is strict. The native element gets all
 * of that correct, for free, on every platform and screen reader
 * combination, with zero custom code to maintain. A custom combobox would
 * only be justified by a requirement this kit doesn't have yet — rich
 * option content (icons/thumbnails inside options), multi-select, or
 * in-list search/filtering. If a future task needs one of those, build a
 * *new* component for it rather than retrofitting this one; don't downgrade
 * this one's accessibility to gain flexibility nothing here needs.
 *
 * Visually, the native arrow is replaced with a themed chevron
 * (`appearance: none` + an absolutely-positioned, `pointer-events: none`
 * icon) — purely cosmetic, the underlying element and its behaviour are
 * untouched.
 */
@Component({
  selector: 'app-select',
  imports: [TranslatePipe, NgIcon],
  templateUrl: './select.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    provideIcons({ phosphorCaretDown }),
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => SelectField),
      multi: true,
    },
  ],
  host: {
    class: 'block',
  },
})
export class SelectField implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly options = input.required<readonly SelectOption[]>();
  readonly placeholder = input<string | undefined>(undefined);
  readonly helperText = input<string | undefined>(undefined);
  readonly errorMessage = input<string | undefined>(undefined);
  readonly required = input(false);
  readonly disabled = input(false);

  protected readonly fieldId = `app-select-${nextSelectId++}`;
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

  protected handleChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.value.set(value);
    this.onChange(value);
  }

  protected handleBlur(): void {
    this.onTouched();
  }
}
