import { createComponentFactory } from '@ngneat/spectator/jest';
import { UiBannerComponent } from './banner.component';

describe('UiBannerComponent roles', () => {
  const createComponent = createComponentFactory({
    component: UiBannerComponent,
    detectChanges: false
  });
  const render = (props: Record<string, unknown>): HTMLElement => {
    const spectator = createComponent({ props: props as never });
    spectator.detectChanges();
    return spectator.element;
  };

  it('danger is an alert, the other kinds are polite status messages', () => {
    expect(render({ kind: 'danger' }).getAttribute('role')).toBe('alert');
    expect(render({ kind: 'warning' }).getAttribute('role')).toBe('status');
  });

  it('alert makes a warning an alert too (the answer to something the user just did)', () => {
    const element = render({ kind: 'warning', alert: true });

    expect(element.getAttribute('role')).toBe('alert');
    expect(element.classList.contains('--warning')).toBe(true);
  });
});
