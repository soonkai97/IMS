import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiIconComponent } from './icon.component';
import { ICON_PATHS, IconName } from './icon.paths';

describe('UiIconComponent', () => {
  let spectator: Spectator<UiIconComponent>;
  const createComponent = createComponentFactory({ component: UiIconComponent });

  const stroke = (): string | null => spectator.query('path')?.getAttribute('stroke-width') ?? null;

  it.each(Object.keys(ICON_PATHS) as IconName[])('renders %s', name => {
    spectator = createComponent({ props: { name } as never });

    expect(spectator.query('path')?.getAttribute('d')).toBe(ICON_PATHS[name]);
    expect(spectator.query('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('is 24px with a 1.75px stroke by default', () => {
    spectator = createComponent({ props: { name: 'check' } as never });

    expect(spectator.element.getAttribute('data-size')).toBe('24');
    expect(stroke()).toBe('1.75');
  });

  it.each([
    [16, '1.5'],
    [20, '1.75'],
    [24, '1.75'],
    [40, '1.75']
  ] as const)('a %ipx icon has a %spx stroke', (size, expected) => {
    spectator = createComponent({ props: { name: 'check', size } as never });

    expect(spectator.element.getAttribute('data-size')).toBe(String(size));
    expect(stroke()).toBe(expected);
  });
});
