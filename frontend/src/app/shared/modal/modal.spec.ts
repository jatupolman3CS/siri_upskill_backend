import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Modal } from './modal';

/**
 * Covers the a11y wiring that is *this component's own* responsibility
 * (see modal.ts's doc comment): Escape-to-close, overlay-click-to-close
 * (with its opt-out), ARIA attributes, and body scroll lock. Tab/Shift+Tab
 * containment and the focus-capture/focus-restore behaviour itself are
 * CDK's `CdkTrapFocus` internals — already covered by CDK's own test
 * suite, and separately confirmed here end-to-end in a real browser during
 * development (see the P0-34 final report).
 */
@Component({
  selector: 'app-modal-test-host',
  imports: [Modal],
  template: `
    <button type="button">outside trigger</button>
    <app-modal
      [(open)]="open"
      titleText="Test modal title"
      [closeOnOverlayClick]="closeOnOverlayClick()"
    >
      <p>Body content</p>
    </app-modal>
  `,
})
class ModalTestHost {
  readonly open = signal(false);
  readonly closeOnOverlayClick = signal(true);
}

describe('Modal', () => {
  let fixture: ComponentFixture<ModalTestHost>;
  let host: ModalTestHost;

  beforeEach(() => {
    document.body.style.overflow = '';
    TestBed.configureTestingModule({ imports: [ModalTestHost] });
    fixture = TestBed.createComponent(ModalTestHost);
    host = fixture.componentInstance;
  });

  function openModal(): void {
    host.open.set(true);
    fixture.detectChanges();
    TestBed.tick();
  }

  function dialogEl(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  it('renders nothing when closed', () => {
    fixture.detectChanges();
    expect(dialogEl()).toBeNull();
  });

  it('renders dialog role/aria-modal/aria-labelledby wired to the visible title when open', () => {
    openModal();
    const dialog = dialogEl();
    expect(dialog).not.toBeNull();
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    const labelledBy = dialog?.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();
    expect(document.getElementById(labelledBy ?? '')?.textContent).toContain('Test modal title');
  });

  it('closes on Escape and updates the two-way bound open() signal', () => {
    openModal();
    const overlay = fixture.nativeElement.querySelector('.app-modal-overlay') as HTMLElement;
    overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(false);
    expect(dialogEl()).toBeNull();
  });

  it('closes when the backdrop itself is clicked', () => {
    openModal();
    const overlay = fixture.nativeElement.querySelector('.app-modal-overlay') as HTMLElement;
    overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(false);
  });

  it('does NOT close when a click inside the panel bubbles to the overlay', () => {
    openModal();
    const panel = fixture.nativeElement.querySelector('.app-modal-panel') as HTMLElement;
    panel.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(true);
    expect(dialogEl()).not.toBeNull();
  });

  it('ignores backdrop clicks when closeOnOverlayClick is false (destructive-confirmation case)', () => {
    host.closeOnOverlayClick.set(false);
    openModal();
    const overlay = fixture.nativeElement.querySelector('.app-modal-overlay') as HTMLElement;
    overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(host.open()).toBe(true);
  });

  it('closes via the close button', () => {
    openModal();
    const closeButton = dialogEl()?.querySelector('button') as HTMLButtonElement;
    closeButton.click();
    fixture.detectChanges();
    expect(host.open()).toBe(false);
  });

  it('locks body scroll while open and restores the previous value after close', () => {
    document.body.style.overflow = '';
    openModal();
    expect(document.body.style.overflow).toBe('hidden');

    host.open.set(false);
    fixture.detectChanges();
    TestBed.tick();
    expect(document.body.style.overflow).toBe('');
  });
});
