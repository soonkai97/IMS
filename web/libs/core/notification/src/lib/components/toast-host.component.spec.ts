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

  it('has the polite region before any toast exists, so the first success is announced', () => {
    expect(politeRegion()).toBeTruthy();
    expect(politeRegion().textContent?.trim()).toBe('');
  });

  it('shows an error as its own alert (one node) and a success in the polite region without a role', () => {
    toast.error('Could not save.');
    toast.success('Saved.');
    spectator.detectChanges();

    const alerts = spectator.queryAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].tagName).toBe('APP-TOAST');
    expect(alerts[0].textContent).toContain('Could not save.');
    expect(politeRegion().textContent).toContain('Saved.');
    expect(politeRegion().textContent).not.toContain('Could not save.');
    expect(politeRegion().querySelector('[role]')).toBeNull();
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

  it('Escape on a toast dismisses it', () => {
    toast.error('Could not save.');
    spectator.detectChanges();

    spectator.dispatchKeyboardEvent('app-toast', 'keydown', 'Escape');

    expect(toast.$toasts()).toHaveLength(0);
  });

  it('shows nothing while a modal dialog is open (the dialog shows its own copy), and again after', () => {
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
    expect(spectator.queryAll('[role="alert"]')).toHaveLength(1);
  });

  it('removes an error when the user goes to another page', async () => {
    jest.useRealTimers();
    toast.error('Could not save.');
    toast.success('Saved.');

    await spectator.inject(Router).navigateByUrl('/');

    expect(toast.$toasts().map(item => item.kind)).toEqual(['success']);
  });
});

describe('ToastHostComponent inside a dialog', () => {
  let spectator: Spectator<ToastHostComponent>;
  const createComponent = createComponentFactory({
    component: ToastHostComponent,
    providers: [provideRouter([])]
  });

  it('stays visible while a modal is open: it is the one that shows the toasts then', () => {
    spectator = createComponent({ props: { inDialog: true } as never });
    spectator.inject(UiModalState).enter();
    spectator.inject(ToastService).error('Could not save.');
    spectator.detectChanges();

    expect(spectator.element.hidden).toBe(false);
    expect(spectator.element.classList).toContain('--in-dialog');
    expect(spectator.queryAll('[role="alert"]')).toHaveLength(1);
  });
});
