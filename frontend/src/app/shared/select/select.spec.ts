import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { SelectField, SelectOption } from './select';

/**
 * Proves `SelectField`'s `ControlValueAccessor` actually round-trips with a
 * typed reactive form via `formControlName` (see input.spec.ts's doc
 * comment for why this matters more than "it compiles").
 */
@Component({
  selector: 'app-select-test-host',
  imports: [ReactiveFormsModule, SelectField],
  template: `
    <form [formGroup]="form">
      <app-select formControlName="category" label="Category" [options]="options" />
    </form>
  `,
})
class SelectTestHost {
  readonly options: SelectOption[] = [
    { value: 'a', label: 'Option A' },
    { value: 'b', label: 'Option B' },
  ];
  readonly form = new FormGroup({
    category: new FormControl('a'),
  });
}

describe('SelectField + formControlName', () => {
  let fixture: ComponentFixture<SelectTestHost>;
  let host: SelectTestHost;
  let nativeSelect: HTMLSelectElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [SelectTestHost] });
    fixture = TestBed.createComponent(SelectTestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
    nativeSelect = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
  });

  it('writes the initial FormControl value into the rendered select (form -> view)', () => {
    expect(nativeSelect.value).toBe('a');
  });

  it('reflects a later programmatic setValue into the view', () => {
    host.form.get('category')?.setValue('b');
    fixture.detectChanges();
    expect(nativeSelect.value).toBe('b');
  });

  it('propagates a native change event back into the FormControl value (view -> form)', () => {
    nativeSelect.value = 'b';
    nativeSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(host.form.get('category')?.value).toBe('b');
  });

  it('disables the native select when the FormControl is disabled', () => {
    host.form.get('category')?.disable();
    fixture.detectChanges();
    expect(nativeSelect.disabled).toBe(true);
  });
});
