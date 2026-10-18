import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiBannerComponent } from './banner.component';

describe('UiBannerComponent', () => {
  let spectator: Spectator<UiBannerComponent>;
  const createComponent = createComponentFactory({
    component: UiBannerComponent,
    detectChanges: false
  });

  it.each([
    ['info', 'status'],
    ['success', 'status'],
    ['warning', 'status'],
    ['danger', 'alert']
  ] as const)(
    'a %s banner is a %s and draws an icon, so colour is never the only cue',
    (kind, role) => {
      spectator = createComponent({ props: { kind } as never });
      spectator.detectChanges();

      expect(spectator.element.getAttribute('role')).toBe(role);
      expect(spectator.element.classList.contains(`--${kind}`)).toBe(true);
      expect(spectator.query('.ui-banner-icon svg')).toBeTruthy();
    }
  );

  it('keeps its inputs: alert makes any kind an alert, the heading is a paragraph', () => {
    spectator = createComponent({
      props: { kind: 'warning', alert: true, heading: 'Returned' } as never
    });
    spectator.detectChanges();

    expect(spectator.element.getAttribute('role')).toBe('alert');
    expect(spectator.element.getAttribute('tabindex')).toBe('-1');
    expect(spectator.query('.ui-banner-heading')?.textContent).toBe('Returned');
  });

  it('shows a 500-character heading whole, inside the content block that wraps it', () => {
    const heading = 'W'.repeat(500);
    spectator = createComponent({ props: { heading } as never });
    spectator.detectChanges();

    const content = spectator.query('.ui-banner-content');
    expect(content?.querySelector('.ui-banner-heading')?.textContent).toBe(heading);
  });
});
