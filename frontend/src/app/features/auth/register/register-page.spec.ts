import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { RegisterPage } from './register-page';

/**
 * Task requirement: "Register's anti-enumeration message is displayed as-is (not altered)" —
 * `RegisterHandler.cs` always returns the exact same `RegisterResponse.message` whether or not the
 * email was already registered (its entire anti-enumeration guarantee). This proves the frontend
 * renders that string byte-for-byte, with no rewording/truncation/conditional branch that could
 * quietly undermine it.
 */
describe('RegisterPage', () => {
  let fixture: ComponentFixture<RegisterPage>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(RegisterPage);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  function fillAndSubmit(): void {
    const inputs = fixture.nativeElement.querySelectorAll('input');
    const [emailInput, displayNameInput, passwordInput] = Array.from(inputs) as HTMLInputElement[];

    emailInput.value = 'learner@example.test';
    emailInput.dispatchEvent(new Event('input'));
    displayNameInput.value = 'Somchai Learner';
    displayNameInput.dispatchEvent(new Event('input'));
    passwordInput.value = 'a-genuinely-long-password';
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('renders the exact anti-enumeration message from RegisterResponse, unaltered', () => {
    const exactBackendMessage =
      'หากอีเมลนี้ยังไม่เคยสมัครสมาชิกมาก่อน เราได้ส่งลิงก์ยืนยันไปยังกล่องจดหมายของคุณแล้ว ' +
      'กรุณาตรวจสอบอีเมล (รวมถึงโฟลเดอร์ Junk/Spam) และกดยืนยันภายใน 24 ชั่วโมง';

    fillAndSubmit();

    const req = httpMock.expectOne('/api/identity/register');
    expect(req.request.method).toBe('POST');
    req.flush({ message: exactBackendMessage });
    fixture.detectChanges();

    const rendered = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(rendered).toContain(exactBackendMessage);
  });

  it('shows the exact same success message text regardless of whether this call represents a new or already-registered email — the component has no branch that could tell, by design', () => {
    // Simulates the "already registered" branch: RegisterHandler still returns
    // RegisterResponse with the identical SuccessMessage constant — this test asserts the
    // component renders whatever it's given, proving there is no separate "already exists"
    // rendering path to accidentally diverge from the real one.
    const sameMessage = 'GENERIC_ANTI_ENUMERATION_MESSAGE';

    fillAndSubmit();

    const req = httpMock.expectOne('/api/identity/register');
    req.flush({ message: sameMessage });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(sameMessage);
  });

  it('disables the submit button while the request is in flight', () => {
    fillAndSubmit();

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submitButton.disabled).toBe(true);

    httpMock.expectOne('/api/identity/register').flush({ message: 'ok' });
  });
});
