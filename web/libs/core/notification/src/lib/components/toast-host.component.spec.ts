import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { ToastService } from '../services/toast.service';
import { ToastHostComponent } from './toast-host.component';

describe('ToastHostComponent', () => {
  let spectator: Spectator<ToastHostComponent>;
  let toast: ToastService;
  const createComponent = createComponentFactory({
    component: ToastHostComponent,
    providers: [provideRouter([])]
  });

  const alertRegion = () => spectator.query('[aria-live="assertive"]') as HTMLElement;
  const statusRegion = () => spectator.query('[aria-live="polite"]') as HTMLElement;

  beforeEach(() => {
    jest.useFakeTimers();
    spectator = createComponent();
    toast = spectator.inject(ToastService);
  });

  afterEach(() => jest.useRealTimers());

  it('has both live regions before any toast exists, so the first one is announced', () => {
    expect(alertRegion()).toBeTruthy();
    expect(statusRegion()).toBeTruthy();
    expect(alertRegion().textContent?.trim()).toBe('');
    expect(statusRegion().textContent?.trim()).toBe('');
  });

  it('puts a failure in the alert region only, and a success in the status region only', () => {
    toast.error('Could not save.');
    toast.success('Saved.');
    spectator.detectChanges();

    expect(alertRegion().textContent).toContain('Could not save.');
    expect(alertRegion().textContent).not.toContain('Saved.');
    expect(statusRegion().textContent).toContain('Saved.');
    expect(statusRegion().textContent).not.toContain('Could not save.');
    // The toasts add no live role of their own: one failure is one announcement.
    expect(spectator.queryAll('app-toast[role]')).toHaveLength(0);
    expect(spectator.queryAll('[aria-live]')).toHaveLength(2);
  });

  it('never takes focus', () => {
    const trigger = document.createElement('button');
    document.body.append(trigger);
    trigger.focus();

    toast.error('Could not save.');
    toast.success('Saved.');
    spectator.detectChanges();

    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('keeps a success on screen while the pointer is over it and lets it go after', () => {
    toast.success('Saved.');
    spectator.detectChanges();
    const item = spectator.query('app-toast') as HTMLElement;
    jest.advanceTimersByTime(3500);

    spectator.dispatchMouseEvent(item, 'mouseenter');
    jest.advanceTimersByTime(60_000);
    expect(toast.$toasts()).toHaveLength(1);

    spectator.dispatchMouseEvent(item, 'mouseleave');
    jest.advanceTimersByTime(1000);
    expect(toast.$toasts()).toHaveLength(0);
  });

  it('removes an error when the user goes to another page', async () => {
    jest.useRealTimers();
    toast.error('Could not save.');
    toast.success('Saved.');

    await spectator.inject(Router).navigateByUrl('/');

    expect(toast.$toasts().map(item => item.kind)).toEqual(['success']);
  });
});
