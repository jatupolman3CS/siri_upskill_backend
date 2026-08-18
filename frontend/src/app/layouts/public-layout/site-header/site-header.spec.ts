import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { TranslationService } from '../../../core/i18n/translation.service';
import { SiteHeader } from './site-header';

describe('SiteHeader', () => {
  let fixture: ComponentFixture<SiteHeader>;
  let authService: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SiteHeader],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(SiteHeader);
    authService = TestBed.inject(AuthService);
    // accessTokenStore is a module-level singleton signal, not reset by TestBed — clear it so a
    // previous spec file's logged-in state never leaks into this one (see mobile-nav.spec.ts for the
    // same note).
    authService.clearSession();
    // English locale purely for stable ASCII text lookups below, same reasoning as
    // devices-page.spec.ts — Thai (the default) behaves identically, this is test-readability only.
    TestBed.inject(TranslationService).setLocale('en');
  });

  afterEach(() => {
    authService.clearSession();
  });

  function linkNamed(text: string): HTMLAnchorElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('a')).find(
      (a) => (a as HTMLAnchorElement).textContent?.trim() === text,
    ) as HTMLAnchorElement | undefined;
  }

  function buttonNamed(text: string): HTMLButtonElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => (b as HTMLButtonElement).textContent?.trim() === text,
    ) as HTMLButtonElement | undefined;
  }

  it('shows Log in/Sign up when logged out, not the devices link or sign-out', () => {
    fixture.detectChanges();
    expect(linkNamed('Log in')).toBeTruthy();
    expect(linkNamed('Sign up')).toBeTruthy();
    expect(linkNamed('Signed-in devices')).toBeFalsy();
    expect(buttonNamed('Sign out')).toBeFalsy();
  });

  it('shows the devices link and sign-out when logged in, and sign-out clears the session', () => {
    authService.setAccessToken('fake-token');
    fixture.detectChanges();

    expect(linkNamed('Signed-in devices')).toBeTruthy();
    expect(linkNamed('Log in')).toBeFalsy();

    buttonNamed('Sign out')?.click();
    fixture.detectChanges();

    expect(authService.isAuthenticated()).toBe(false);
  });

  it('opens the mobile nav drawer from the hamburger trigger, with aria-expanded reflecting state', () => {
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('button[aria-haspopup="dialog"]') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    trigger.click();
    fixture.detectChanges();
    TestBed.tick();

    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
  });
});
