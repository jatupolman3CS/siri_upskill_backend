import { TestBed } from '@angular/core/testing';

import { ToastService } from './toast.service';

/**
 * Covers the trickiest part of Toast — the auto-dismiss timer lifecycle,
 * in particular pause-on-hover/focus not just stopping the timer but
 * resuming from the *remaining* time rather than restarting the full
 * duration (see toast.service.ts's doc comment).
 */
describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('adds a toast immediately with the given type and message', () => {
    service.success('Course saved.');
    expect(service.toasts()).toEqual([
      expect.objectContaining({ type: 'success', message: 'Course saved.' }),
    ]);
  });

  it('auto-dismisses success/info after their default duration, not a moment sooner', () => {
    service.success('Course saved.');
    vi.advanceTimersByTime(3999);
    expect(service.toasts().length).toBe(1);
    vi.advanceTimersByTime(1);
    expect(service.toasts().length).toBe(0);
  });

  it('gives error toasts a longer default duration than success — errors need to be read', () => {
    service.error('Payment failed.');
    vi.advanceTimersByTime(4000);
    expect(service.toasts().length).toBe(1);
    vi.advanceTimersByTime(2000);
    expect(service.toasts().length).toBe(0);
  });

  it('honors an explicit custom duration override', () => {
    service.info('Custom duration', 1000);
    vi.advanceTimersByTime(999);
    expect(service.toasts().length).toBe(1);
    vi.advanceTimersByTime(1);
    expect(service.toasts().length).toBe(0);
  });

  it('pause() stops the countdown entirely while paused', () => {
    const id = service.success('Course saved.');
    vi.advanceTimersByTime(3000);
    service.pause(id);
    vi.advanceTimersByTime(60_000);
    expect(service.toasts().length).toBe(1);
  });

  it('resume() continues from the remaining time, not a fresh full duration', () => {
    const id = service.success('Course saved.');
    vi.advanceTimersByTime(3000); // 1000ms of the 4000ms default should remain
    service.pause(id);
    service.resume(id);
    vi.advanceTimersByTime(999);
    expect(service.toasts().length).toBe(1);
    vi.advanceTimersByTime(1);
    expect(service.toasts().length).toBe(0);
  });

  it('a user who reads slowly (pause, wait, resume, wait) never loses the toast early', () => {
    const id = service.success('Course saved.');
    vi.advanceTimersByTime(1000);
    service.pause(id);
    vi.advanceTimersByTime(30_000); // reading for a long time while paused
    service.resume(id);
    vi.advanceTimersByTime(2999); // 3000ms of the original 4000ms remained
    expect(service.toasts().length).toBe(1);
    vi.advanceTimersByTime(1);
    expect(service.toasts().length).toBe(0);
  });

  it('dismiss() removes a toast immediately regardless of its timer state', () => {
    const id = service.success('Course saved.');
    service.dismiss(id);
    expect(service.toasts().length).toBe(0);
  });

  it('dismiss() is a safe no-op when called on an id that no longer exists', () => {
    const id = service.success('Course saved.');
    service.dismiss(id);
    expect(() => service.dismiss(id)).not.toThrow();
  });

  it('pause() then dismiss() does not throw and leaves no dangling timer', () => {
    const id = service.success('Course saved.');
    service.pause(id);
    expect(() => service.dismiss(id)).not.toThrow();
    vi.advanceTimersByTime(60_000);
    expect(service.toasts().length).toBe(0);
  });

  it('resume() on an id that was never paused (or already removed) is a safe no-op', () => {
    expect(() => service.resume('not-a-real-id')).not.toThrow();
  });

  it('stacks multiple toasts and dismisses each independently on its own timer', () => {
    service.success('First'); // 4000ms
    service.error('Second'); // 6000ms
    expect(service.toasts().length).toBe(2);

    vi.advanceTimersByTime(4000);
    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].type).toBe('error');

    vi.advanceTimersByTime(2000);
    expect(service.toasts().length).toBe(0);
  });
});
