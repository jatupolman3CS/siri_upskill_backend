import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { Checkbox } from './checkbox';

/**
 * Proves `Checkbox`'s `ControlValueAccessor` actually round-trips with a
 * typed reactive form via `formControlName` (see input.spec.ts's doc
 * comment for why this matters more than "it compiles").
 */
@Component({
  selector: 'app-checkbox-test-host',
  imports: [ReactiveFormsModule, Checkbox],
  template: `
    <form [formGroup]="form">
      <app-checkbox formControlName="agree" label="I agree" />
    </form>
  `,
})
class CheckboxTestHost {
  readonly form = new FormGroup({
    agree: new FormControl(false),
  });
}

describe('Checkbox + formControlName', () => {
  let fixture: ComponentFixture<CheckboxTestHost>;
  let host: CheckboxTestHost;
  let nativeCheckbox: HTMLInputElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CheckboxTestHost] });
    fixture = TestBed.createComponent(CheckboxTestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
    nativeCheckbox = fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
  });

  it('writes the initial FormControl value into the rendered checkbox (form -> view)', () => {
    expect(nativeCheckbox.checked).toBe(false);
  });

  it('reflects a later programmatic setValue into the view', () => {
    host.form.get('agree')?.setValue(true);
    fixture.detectChanges();
    expect(nativeCheckbox.checked).toBe(true);
  });

  it('propagates a native change event back into the FormControl value (view -> form)', () => {
    nativeCheckbox.checked = true;
    nativeCheckbox.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(host.form.get('agree')?.value).toBe(true);
  });

  it('marks the control touched when toggled', () => {
    expect(host.form.get('agree')?.touched).toBe(false);
    nativeCheckbox.checked = true;
    nativeCheckbox.dispatchEvent(new Event('change'));
    expect(host.form.get('agree')?.touched).toBe(true);
  });

  it('disables the native checkbox when the FormControl is disabled', () => {
    host.form.get('agree')?.disable();
    fixture.detectChanges();
    expect(nativeCheckbox.disabled).toBe(true);
  });
});
