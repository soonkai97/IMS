import { Component, signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiPublishHeightDirective } from './publish-height.directive';

@Component({
  imports: [UiPublishHeightDirective],
  template: `@if ($shown()) {
    <footer appPublishHeight="--test-bar-height">bar</footer>
  }`
})
class HostComponent {
  $shown = signal(true);
}

describe('UiPublishHeightDirective', () => {
  let spectator: Spectator<HostComponent>;
  let notify: () => void = () => undefined;
  let height = 40;
  const disconnect = jest.fn();
  const createComponent = createComponentFactory({
    component: HostComponent,
    detectChanges: false
  });
  const value = (): string => document.documentElement.style.getPropertyValue('--test-bar-height');

  beforeEach(() => {
    height = 40;
    disconnect.mockClear();
    (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
      constructor(callback: () => void) {
        notify = callback;
      }
      observe(): void {
        notify();
      }
      disconnect = disconnect;
    };
    jest
      .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
      .mockImplementation(() => ({ height }) as DOMRect);
    spectator = createComponent();
    spectator.detectChanges();
  });

  afterEach(() => {
    jest.restoreAllMocks();
    delete (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver;
  });

  it('publishes the height at once and again whenever it changes (a bar whose buttons wrap grows)', () => {
    expect(value()).toBe('40px');

    height = 152;
    notify();

    expect(value()).toBe('152px');
  });

  it('removes the property and stops observing when the element goes away', () => {
    spectator.component.$shown.set(false);
    spectator.detectChanges();

    expect(value()).toBe('');
    expect(disconnect).toHaveBeenCalled();
  });
});
