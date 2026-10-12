import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/**
 * Publishes the element's real height as a custom property on `<html>` (`<footer appPublishHeight="--ims-action-bar-height">`),
 * kept current by a ResizeObserver and removed when the element goes away. The sticky action bar uses it so the page's scroll
 * padding is the bar's real height, which changes when its buttons wrap (320px, a long summary): a focused control is then always
 * scrolled clear of the bar instead of under it.
 */
@Directive({ selector: '[appPublishHeight]' })
export class UiPublishHeightDirective {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly root = inject(DOCUMENT).documentElement;

  $property = input.required<string>({ alias: 'appPublishHeight' });

  constructor() {
    effect(onCleanup => {
      const property = this.$property();
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() =>
        this.root.style.setProperty(property, `${this.element.getBoundingClientRect().height}px`)
      );
      observer.observe(this.element);
      onCleanup(() => {
        observer.disconnect();
        this.root.style.removeProperty(property);
      });
    });
  }
}
