import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { InputField } from './input';

/**
 * Proves `InputField`'s `ControlValueAccessor` actually round-trips with a
 * typed reactive form via `formControlName` — not just "it compiles" (see
 * frontend.md's "Typed reactive forms เท่านั้น"). Covers both directions:
 * form -> view (programmatic `setValue`) and view -> form (native `input`
 * event), plus `disabled`/touched wiring.
 */
@Component({
  selector: 'app-input-test-host',
  imports: [ReactiveFormsModule, InputField],
  template: `
    <form [formGroup]="form">
      <app-input formControlName="name" label="Name" />
    </form>
  `,
})
class InputTestHost {
  readonly form = new FormGroup({
    name: new FormControl('initial'),
  });
}

describe('InputField + formControlName', () => {
  let fixture: ComponentFixture<InputTestHost>;
  let host: InputTestHost;
  let nativeInput: HTMLInputElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [InputTestHost] });
    fixture = TestBed.createComponent(InputTestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
    nativeInput = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  });

  it('writes the initial FormControl value into the rendered input (form -> view)', () => {
    expect(nativeInput.value).toBe('initial');
  });

  it('reflects a later programmatic setValue into the view', () => {
    host.form.get('name')?.setValue('changed by form');
    fixture.detectChanges();
    expect(nativeInput.value).toBe('changed by form');
  });

  it('propagates a native input event back into the FormControl value (view -> form)', () => {
    nativeInput.value = 'typed by user';
    nativeInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(host.form.get('name')?.value).toBe('typed by user');
  });

  it('marks the control touched on blur', () => {
    expect(host.form.get('name')?.touched).toBe(false);
    nativeInput.dispatchEvent(new Event('blur'));
    expect(host.form.get('name')?.touched).toBe(true);
  });

  it('disables the native input when the FormControl is disabled', () => {
    host.form.get('name')?.disable();
    fixture.detectChanges();
    expect(nativeInput.disabled).toBe(true);
  });
});
