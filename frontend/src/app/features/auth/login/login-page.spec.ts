import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { errorInterceptor } from '../../../core/http/error.interceptor';
import { LoginPage } from './login-page';

/** Stub target for the post-login `router.navigateByUrl('/account/devices')` call — a real route
 *  must exist for that navigation to resolve instead of rejecting with NG04002, but this suite has
 *  no reason to actually load the real (lazy, authenticated) `DevicesPage` to prove it. */
@Component({ selector: 'app-devices-stub', template: '' })
class DevicesStubPage {}

/**
 * Task requirement: "Login's error message is identical regardless of the simulated failure reason
 * (mock the API to return the same generic error for different underlying causes and confirm the UI
 * doesn't try to differentiate)". `LoginHandler.cs` already collapses "no such account", "wrong
 * password", and "account not yet active" into one `InvalidCredentialsError` body server-side — this
 * proves the frontend renders whatever it receives as-is, in one shared region, never attributing it
 * to a single field (which would visually leak "the email was right, only the password was wrong").
 */
describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let httpMock: HttpTestingController;

  const GENERIC_MESSAGE = 'อีเมลหรือรหัสผ่านไม่ถูกต้อง';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'account/devices', component: DevicesStubPage }]),
      ],
    });
    fixture = TestBed.createComponent(LoginPage);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  function fillAndSubmit(email: string, password: string): void {
    const [emailInput, passwordInput] = Array.from(fixture.nativeElement.querySelectorAll('input')) as HTMLInputElement[];
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function flushGenericInvalidCredentials(): void {
    httpMock
      .expectOne('/api/identity/login')
      .flush({ title: GENERIC_MESSAGE, status: 400, errorCode: 'validation' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
  }

  function errorSummaryText(): string {
    return (fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement | null)?.textContent?.trim() ?? '';
  }

  it('shows the identical generic message for a "wrong password" response as for a "no such account" one', () => {
    // Scenario A — server-side this is the wrong-password branch; wire-shape is just the shared
    // InvalidCredentialsError body.
    fillAndSubmit('real-user@example.test', 'wrong-password-123');
    flushGenericInvalidCredentials();
    const messageForWrongPassword = errorSummaryText();
    expect(messageForWrongPassword).toContain(GENERIC_MESSAGE);

    // Scenario B — server-side this is the no-such-account branch; byte-for-byte the same body.
    fillAndSubmit('nobody-registered@example.test', 'whatever-password-123');
    flushGenericInvalidCredentials();
    const messageForNoSuchAccount = errorSummaryText();

    expect(messageForNoSuchAccount).toBe(messageForWrongPassword);
  });

  it('never attaches the generic credentials error to a single field — only the shared alert region shows it', () => {
    fillAndSubmit('real-user@example.test', 'wrong-password-123');
    flushGenericInvalidCredentials();

    expect(errorSummaryText()).toContain(GENERIC_MESSAGE);
    // Neither input is marked invalid — the error never got routed onto app-input's own
    // per-field errorMessage (which would set aria-invalid="true" and render its own alert
    // right under just that one field).
    expect(fixture.nativeElement.querySelectorAll('input[aria-invalid="true"]').length).toBe(0);
  });

  it('disables the submit button while the request is in flight', () => {
    fillAndSubmit('real-user@example.test', 'correct-horse-battery');

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submitButton.disabled).toBe(true);

    httpMock.expectOne('/api/identity/login').flush({ accessToken: 'token', accessTokenExpiresAtUtc: new Date().toISOString() });
  });
});
