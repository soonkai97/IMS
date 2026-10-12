import {
  DestroyRef,
  Directive,
  ElementRef,
  afterNextRender,
  inject,
  input,
  signal
} from '@angular/core';

/**
 * Makes a box that can scroll sideways a labelled, focusable region (WCAG 2.1.1, axe `scrollable-region-focusable`), and
 * only while it really scrolls: a table that fits has no extra tab stop and no landmark (L00). The first child is watched
 * as well, because its size changes when rows arrive. Styling (`overflow-x: auto`) stays with the host's stylesheet.
 */
@Directive({
  selector: '[appScrollRegion]',
  host: {
    '[attr.role]': '$scrolls() ? "region" : null',
    '[attr.tabindex]': '$scrolls() ? 0 : null',
    '[attr.aria-label]': '$scrolls() ? $label() : null',
    '[attr.data-scroll-region]': '$scrolls() ? "" : null'
  }
})
export class UiScrollRegionDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  /** The accessible name of the region. */
  readonly $label = input.required<string>({ alias: 'appScrollRegion' });
  readonly $scrolls = signal(false);

  constructor() {
    // jsdom has no layout and no ResizeObserver: there the box never scrolls.
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const element = this.host.nativeElement;
      const measure = (): void => this.$scrolls.set(element.scrollWidth > element.clientWidth + 1);
      const observer = new ResizeObserver(measure);
      observer.observe(element);
      if (element.firstElementChild) {
        observer.observe(element.firstElementChild);
      }
      this.destroyRef.onDestroy(() => observer.disconnect());
      measure();
    });
  }
}
