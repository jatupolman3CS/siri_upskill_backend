import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { TranslationService } from '../../../core/i18n/translation.service';
import { ListSessionsResponse } from '../data/auth-api.models';
import { DevicesPage } from './devices-page';

const SESSIONS: ListSessionsResponse = {
  sessions: [
    {
      sessionId: '11111111-1111-1111-1111-111111111111',
      deviceName: 'Chrome on Windows',
      userAgent: 'Mozilla/5.0',
      ipAddress: '203.0.113.5',
      createdAtUtc: '2026-08-01T00:00:00Z',
      lastSeenAtUtc: '2026-08-16T00:00:00Z',
      isActive: true,
      revokedAtUtc: null,
      isCurrentSession: false,
    },
  ],
};

/**
 * Task requirement: "the revoke-session confirmation Modal actually gates the destructive action
 * (canceling the modal must not revoke anything)". `httpMock.verify()` in `afterEach` is the actual
 * proof — it fails the test if any HTTP request went unaccounted for, so if `Cancel` ever triggered a
 * `DELETE` this test wasn't expecting, the whole suite would fail loudly.
 */
describe('DevicesPage', () => {
  let fixture: ComponentFixture<DevicesPage>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DevicesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(DevicesPage);
    httpMock = TestBed.inject(HttpTestingController);
    // English locale purely so this spec's text-based button lookups match stable, predictable
    // ASCII strings — the default locale (`th`) works identically, this is a test-readability
    // choice, not a behavior difference.
    TestBed.inject(TranslationService).setLocale('en');
    fixture.detectChanges();
    httpMock.expectOne('/api/identity/sessions').flush(SESSIONS);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  function revokeButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).find((button) =>
      (button as HTMLButtonElement).textContent?.includes('Sign out') && !(button as HTMLButtonElement).textContent?.includes('other') && !(button as HTMLButtonElement).textContent?.includes('everywhere'),
    ) as HTMLButtonElement;
  }

  function dialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  it('opens a confirmation modal on "Sign out" without calling the revoke endpoint yet', () => {
    expect(dialog()).toBeNull();

    revokeButton().click();
    fixture.detectChanges();

    expect(dialog()).not.toBeNull();
    // No DELETE request exists yet — proven by httpMock.verify() in afterEach finding nothing
    // outstanding beyond what this test itself expects (nothing, here).
  });

  it('canceling the modal closes it and revokes nothing', () => {
    revokeButton().click();
    fixture.detectChanges();
    expect(dialog()).not.toBeNull();

    const cancelButton = Array.from(dialog()?.querySelectorAll('button') ?? []).find((button) =>
      (button as HTMLButtonElement).textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;
    cancelButton.click();
    fixture.detectChanges();

    expect(dialog()).toBeNull();
    // httpMock.verify() (afterEach) asserts no DELETE /api/identity/sessions/{id} was ever made.
  });

  it('confirming the modal actually calls DELETE /api/identity/sessions/{id}', () => {
    revokeButton().click();
    fixture.detectChanges();

    const confirmButton = Array.from(dialog()?.querySelectorAll('button') ?? []).find((button) =>
      (button as HTMLButtonElement).textContent?.trim() === 'Sign out device',
    ) as HTMLButtonElement;
    confirmButton.click();
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/identity/sessions/11111111-1111-1111-1111-111111111111');
    expect(req.request.method).toBe('DELETE');
    req.flush({ message: 'signed out', wasCurrentSession: false });

    httpMock.expectOne('/api/identity/sessions').flush(SESSIONS);
  });
});
