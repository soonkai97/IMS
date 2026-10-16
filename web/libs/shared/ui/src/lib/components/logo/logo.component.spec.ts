import { createComponentFactory } from '@ngneat/spectator/jest';
import { UiLogoComponent } from './logo.component';

describe('UiLogoComponent', () => {
  const createComponent = createComponentFactory(UiLogoComponent);

  it('is a decorative one-paint mark on the 64 unit grid of the design file', () => {
    const svg = createComponent().query('svg');

    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    expect(svg?.getAttribute('viewBox')).toBe('0 0 64 64');
    expect(svg?.querySelector('title')).toBeNull();
    expect(svg?.querySelector('path')?.getAttribute('fill')).toBe('currentColor');
  });
});
