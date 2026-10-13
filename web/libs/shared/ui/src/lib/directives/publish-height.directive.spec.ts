import { Component, signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiPublishHeightDirective } from './publish-height.directive';

@Component({
  imports: [UiPublishHeightDirective],
  template: `@if ($shown()) {
    <footer appPublishHeight="--test-bar-height" appPublishClearance="--test-bar-clearance">
      bar
    </footer>
  }`
})
class HostComponent {
  $shown = signal(true);
}

describe('UiPublishHeightDirective', () => {
  let spectator: Spectator<HostComponent>;
  let notify: () => void = () => undefined;
  let height = 40;
  let top = 700;
  const disconnect = jest.fn();
  const createComponent = createComponentFactory(HostComponent);
  const value = (): string => document.documentElement.style.getPropertyValue('--test-bar-height');
  const clearance = (): string =>
    document.documentElement.style.getPropertyValue('--test-bar-clearance');

  beforeEach(() => {
    height = 40;
    top = 700;
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
      .mockImplementation(() => ({ height, top }) as DOMRect);
    spectator = createComponent();
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

  it('publishes how far the top edge is above the bottom of the window, and follows a scroll (a bar that has not reached the bottom)', () => {
    expect(clearance()).toBe(`${window.innerHeight - 700}px`);

    top = window.innerHeight - 66;
    window.dispatchEvent(new Event('scroll'));

    expect(clearance()).toBe('66px');

    top = window.innerHeight + 200;
    window.dispatchEvent(new Event('scroll'));

    expect(clearance()).toBe('0px');
  });

  it('removes the properties and stops observing when the element goes away', () => {
    spectator.component.$shown.set(false);
    spectator.detectChanges();

    expect(value()).toBe('');
    expect(clearance()).toBe('');
    expect(disconnect).toHaveBeenCalled();
  });
});

@Component({
  imports: [UiPublishHeightDirective],
  template: `<section class="test-box">
    <header appPublishHeight="--test-head-height" publishScope=".test-box">head</header>
  </section>`
})
class ScopedHostComponent {}

describe('UiPublishHeightDirective with a scope (the header of an app-table)', () => {
  const createComponent = createComponentFactory(ScopedHostComponent);

  beforeEach(() => {
    (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
      constructor(private readonly callback: () => void) {}
      observe(): void {
        this.callback();
      }
      disconnect(): void {
        // nothing to release
      }
    };
    jest
      .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
      .mockImplementation(() => ({ height: 76 }) as DOMRect);
  });

  afterEach(() => {
    jest.restoreAllMocks();
    delete (globalThis as unknown as { ResizeObserver?: unknown }).ResizeObserver;
  });

  it('puts the property on the closest matching ancestor and leaves <html> alone', () => {
    const spectator = createComponent();

    expect(
      (spectator.query('.test-box') as HTMLElement).style.getPropertyValue('--test-head-height')
    ).toBe('76px');
    expect(document.documentElement.style.getPropertyValue('--test-head-height')).toBe('');
  });
});
