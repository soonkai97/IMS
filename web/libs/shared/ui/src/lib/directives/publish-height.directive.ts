import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/**
 * Publishes the element's real height as a custom property on `<html>` (`appPublishHeight="--ims-action-bar-height"`), kept current
 * by a ResizeObserver and removed with the element. The sticky action bar wraps at 320px, so the page's scroll padding needs its
 * real height to scroll a focused control clear of it.
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
