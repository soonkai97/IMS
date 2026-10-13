import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiModalState } from '@ims/ui';
import { ToastService } from '../services/toast.service';
import { ToastHostComponent } from './toast-host.component';

describe('ToastHostComponent', () => {
  let spectator: Spectator<ToastHostComponent>;
  let toast: ToastService;
  const createComponent = createComponentFactory({
    component: ToastHostComponent,
    providers: [provideRouter([])]
  });

  const politeRegion = () => spectator.query('[aria-live="polite"]') as HTMLElement;

  beforeEach(() => {
    jest.useFakeTimers();
    spectator = createComponent();
    toast = spectator.inject(ToastService);
  });

  afterEach(() => jest.useRealTimers());

  it('keeps the polite region from the start; shows an error as its own alert and a success in it, taking no focus', () => {
    expect(politeRegion().textContent?.trim()).toBe('');
    const trigger = document.createElement('button');
    document.body.append(trigger);
    trigger.focus();

    toast.error('Could not save.');
    toast.success('Saved.');
    spectator.detectChanges();

    const alerts = spectator.queryAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].textContent).toContain('Could not save.');
    expect(politeRegion().textContent).toContain('Saved.');
    expect(politeRegion().querySelector('[role]')).toBeNull();
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

  it('Escape on a toast dismisses it', () => {
    toast.error('Could not save.');
    spectator.detectChanges();

    spectator.dispatchKeyboardEvent('app-toast', 'keydown', 'Escape');

    expect(toast.$toasts()).toHaveLength(0);
  });

  it('shows nothing while a modal is open, and a closing dialog takes its errors with it (no second announcement)', () => {
    toast.error('Could not save.');
    spectator.detectChanges();
    const modal = spectator.inject(UiModalState);

    modal.enter();
    spectator.detectChanges();
    expect(spectator.element.hidden).toBe(true);
    expect(spectator.queryAll('[role="alert"]')).toHaveLength(0);

    modal.leave();
    spectator.detectChanges();
    expect(spectator.element.hidden).toBe(false);
    expect(toast.$toasts()).toHaveLength(0);
  });

  it('brings a new error into view without moving focus, and does so for no success', () => {
    const scroll = jest.fn();
    Element.prototype.scrollIntoView = scroll;

    toast.success('Saved.');
    spectator.detectChanges();
    expect(scroll).not.toHaveBeenCalled();

    toast.error('Could not save.');
    spectator.detectChanges();
    expect(scroll).toHaveBeenCalledWith({ block: 'nearest', behavior: 'smooth' });
  });

  it('removes an error when the user goes to another page', async () => {
    jest.useRealTimers();
    toast.error('Could not save.');
    toast.success('Saved.');

    await spectator.inject(Router).navigateByUrl('/');

    expect(toast.$toasts().map(item => item.kind)).toEqual(['success']);
  });
});
