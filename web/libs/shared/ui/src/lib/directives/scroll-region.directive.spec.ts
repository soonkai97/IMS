import { Component } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiScrollRegionDirective } from './scroll-region.directive';

@Component({
  imports: [UiScrollRegionDirective],
  template: `<div class="box" appScrollRegion="Receipts"><table></table></div>`
})
class HostComponent {}

describe('UiScrollRegionDirective (L00)', () => {
  let spectator: Spectator<HostComponent>;
  let notify: () => void = () => undefined;
  let scrollWidth = 100;
  const createComponent = createComponentFactory({
    component: HostComponent,
    detectChanges: false
  });
  const box = (): HTMLElement => spectator.query('.box') as HTMLElement;

  beforeEach(() => {
    scrollWidth = 100;
    (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
      constructor(callback: () => void) {
        notify = callback;
      }
      observe(): void {
        // The test drives the callback itself.
      }
      disconnect(): void {
        // Nothing to release.
      }
    };
    spectator = createComponent();
    Object.defineProperty(box(), 'clientWidth', { value: 100, configurable: true });
    Object.defineProperty(box(), 'scrollWidth', { get: () => scrollWidth, configurable: true });
    spectator.detectChanges();
  });

  it('is no region and no tab stop while its content fits', () => {
    expect(box().hasAttribute('role')).toBe(false);
    expect(box().hasAttribute('tabindex')).toBe(false);
    expect(box().hasAttribute('data-scroll-region')).toBe(false);
  });

  it('becomes a named, focusable region as soon as its content is wider than the box, and stops when it fits again', () => {
    scrollWidth = 300;
    notify();
    spectator.detectChanges();

    expect(box().getAttribute('role')).toBe('region');
    expect(box().getAttribute('tabindex')).toBe('0');
    expect(box().getAttribute('aria-label')).toBe('Receipts');
    expect(box().hasAttribute('data-scroll-region')).toBe(true);

    scrollWidth = 100;
    notify();
    spectator.detectChanges();

    expect(box().hasAttribute('role')).toBe(false);
    expect(box().hasAttribute('tabindex')).toBe(false);
  });
});
