import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiToastComponent } from './toast.component';

describe('UiToastComponent long messages (L00)', () => {
  let spectator: Spectator<UiToastComponent>;
  const createComponent = createComponentFactory({
    component: UiToastComponent,
    detectChanges: false
  });

  it('keeps the whole message in the live region and in the title, whatever the clamp shows', () => {
    const message = 'Server said '.repeat(60);
    spectator = createComponent({ props: { kind: 'error', message } as never });
    spectator.detectChanges();

    const text = spectator.query('.ui-toast-message');
    expect(spectator.element.getAttribute('role')).toBe('alert');
    expect(text?.textContent?.trim()).toBe(message.trim());
    expect(text?.getAttribute('title')).toBe(message);
  });
});
