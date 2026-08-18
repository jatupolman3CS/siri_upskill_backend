import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { TranslationService } from '../../../core/i18n/translation.service';
import { MobileNav } from './mobile-nav';

/**
 * Covers this component's own responsibility, same restraint as
 * modal.spec.ts: Escape/overlay-click-to-close, ARIA wiring, body scroll
 * lock, plus the auth-state branch and nav-link-closes-drawer behaviour
 * that's specific to this component. Tab/Shift+Tab containment and the
 * focus-capture/restore behaviour itself are CdkTrapFocus's own internals —
 * already covered by CDK's own test suite, not re-tested here (same
 * reasoning as modal.spec.ts).
 */
@Component({
  selector: 'app-mobile-nav-test-host',
  imports: [MobileNav],
  template: `
    <button type="button">outside trigger</button>
    <app-mobile-nav [(open)]="open" />
  `,
})
class MobileNavTestHost {
  readonly open = signal(false);
}

describe('MobileNav', () => {
  let fixture: ComponentFixture<MobileNavTestHost>;
  let host: MobileNavTestHost;
  let authService: AuthService;

  beforeEach(() => {
    document.body.style.overflow = '';
    TestBed.configureTestingModule({
      imports: [MobileNavTestHost],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(MobileNavTestHost);
    host = fixture.componentInstance;
    authService = TestBed.inject(AuthService);
    // accessTokenStore is a module-level singleton signal, not reset by TestBed — clear it so a
    // previous spec file's (or an earlier it() in this one's) logged-in state never leaks in here.
    authService.clearSession();
    // English locale purely for stable ASCII text lookups below, same reasoning as
    // devices-page.spec.ts — Thai (the default) behaves identically, this is test-readability only.
    TestBed.inject(TranslationService).setLocale('en');
  });

  afterEach(() => {
    authService.clearSession();
  });

  function openDrawer(): void {
    host.open.set(true);
    fixture.detectChanges();
    TestBed.tick();
  }

  function dialogEl(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  function linkNamed(text: string): HTMLAnchorElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('nav a')).find(
      (a) => (a as HTMLAnchorElement).textContent?.trim() === text,
    ) as HTMLAnchorElement | undefined;
  }

  function buttonNamed(text: string): HTMLButtonElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('nav button')).find(
      (b) => (b as HTMLButtonElement).textContent?.trim() === text,
    ) as HTMLButtonElement | undefined;
  }

  it('renders nothing when closed', () => {
    fixture.detectChanges();
    expect(dialogEl()).toBeNull();
  });

  it('renders dialog role/aria-modal/aria-labelledby wired to the visible title when open', () => {
    openDrawer();
    const dialog = dialogEl();
    expect(dialog).not.toBeNull();
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    const labelledBy = dialog?.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();
    expect(document.getElementById(labelledBy ?? '')).not.toBeNull();
  });

  it('closes on Escape and updates the two-way bound open() signal', () => {
    openDrawer();
    const panel = dialogEl() as HTMLElement;
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(false);
    expect(dialogEl()).toBeNull();
  });

  it('closes when the backdrop itself is clicked', () => {
    openDrawer();
    const overlay = fixture.nativeElement.querySelector('.app-mobile-nav-overlay') as HTMLElement;
    overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(false);
  });

  it('does NOT close when a click inside the panel bubbles to the overlay', () => {
    openDrawer();
    const panel = fixture.nativeElement.querySelector('.app-mobile-nav-panel') as HTMLElement;
    panel.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(true);
    expect(dialogEl()).not.toBeNull();
  });

  it('closes via the close button', () => {
    openDrawer();
    const closeButton = dialogEl()?.querySelector('button') as HTMLButtonElement;
    closeButton.click();
    fixture.detectChanges();
    expect(host.open()).toBe(false);
  });

  it('locks body scroll while open and restores the previous value after close', () => {
    document.body.style.overflow = '';
    openDrawer();
    expect(document.body.style.overflow).toBe('hidden');

    host.open.set(false);
    fixture.detectChanges();
    TestBed.tick();
    expect(document.body.style.overflow).toBe('');
  });

  it('shows Log in/Sign up links when logged out, and closes the drawer when a nav link is clicked', () => {
    openDrawer();
    expect(linkNamed('Log in')).toBeTruthy();
    expect(linkNamed('Sign up')).toBeTruthy();
    expect(linkNamed('Signed-in devices')).toBeFalsy();

    linkNamed('Home')?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(false);
  });

  it('shows the devices link and sign-out when logged in, and sign-out clears the session', () => {
    authService.setAccessToken('fake-token');
    openDrawer();
    expect(linkNamed('Signed-in devices')).toBeTruthy();
    expect(linkNamed('Log in')).toBeFalsy();

    buttonNamed('Sign out')?.click();
    fixture.detectChanges();

    expect(authService.isAuthenticated()).toBe(false);
    expect(host.open()).toBe(false);
  });
});
