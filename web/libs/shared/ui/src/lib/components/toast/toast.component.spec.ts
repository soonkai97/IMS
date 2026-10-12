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
    expect(spectator.element.hasAttribute('role')).toBe(false);
    expect(text?.textContent?.trim()).toBe(message.trim());
    expect(text?.hasAttribute('title')).toBe(false);
  });
});
