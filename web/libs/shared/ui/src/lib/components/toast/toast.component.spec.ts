import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiToastComponent } from './toast.component';

describe('UiToastComponent long messages (L00)', () => {
  let spectator: Spectator<UiToastComponent>;
  const createComponent = createComponentFactory({
    component: UiToastComponent,
    detectChanges: false
  });

  it('shows the whole message, wrapped, never clamped or cut', () => {
    const message = 'Server said '.repeat(60);
    spectator = createComponent({ props: { kind: 'error', message } as never });
    spectator.detectChanges();

    const text = spectator.query('.ui-toast-message');
    expect(spectator.element.getAttribute('role')).toBe('alert');
    expect(text?.textContent?.trim()).toBe(message.trim());
    expect(text?.hasAttribute('title')).toBe(false);
  });
});

describe('UiToastComponent kinds, outputs and a 500-character value (R13)', () => {
  let spectator: Spectator<UiToastComponent>;
  const createComponent = createComponentFactory({
    component: UiToastComponent,
    detectChanges: false
  });

  it.each([
    ['info', null],
    ['success', null],
    ['error', 'alert']
  ] as const)('a %s toast has role %s and its kind class', (kind, role) => {
    spectator = createComponent({ props: { kind, message: 'Saved.' } as never });
    spectator.detectChanges();

    expect(spectator.element.getAttribute('role')).toBe(role);
    expect(spectator.element.classList.contains(`--${kind}`)).toBe(true);
  });

  it('keeps the action and dismiss outputs', () => {
    spectator = createComponent({
      props: { kind: 'info', message: 'Draft saved.', actionLabel: 'View' } as never
    });
    spectator.detectChanges();
    const actioned = jest.fn();
    const dismissed = jest.fn();
    spectator.output('$actioned').subscribe(actioned);
    spectator.output('$dismissed').subscribe(dismissed);

    spectator.click('.ui-toast-action');
    spectator.click('.ui-toast-close');

    expect(actioned).toHaveBeenCalledTimes(1);
    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('shows a 500-character unbroken message whole', () => {
    const message = 'W'.repeat(500);
    spectator = createComponent({ props: { kind: 'error', message } as never });
    spectator.detectChanges();

    expect(spectator.query('.ui-toast-message')?.textContent).toBe(message);
  });
});
