import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/**
 * Publishes the element's real height as a custom property on `<html>` (`appPublishHeight="--ims-action-bar-height"`), kept current
 * by a ResizeObserver and removed with the element. The sticky action bar wraps at 320px, so the page's scroll padding needs its
 * real height to scroll a focused control clear of it.
 *
 * `appPublishClearance="--ims-action-bar-clearance"` also publishes how far the element's top edge is above the bottom of the
 * window, kept current on scroll and resize. A bar that has not reached the bottom yet (a short page) sits higher than its height
 * suggests; the toast host stands clear of whatever the bar really covers.
 */
@Directive({ selector: '[appPublishHeight]' })
export class UiPublishHeightDirective {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly document = inject(DOCUMENT);
  private readonly root = this.document.documentElement;

  $property = input.required<string>({ alias: 'appPublishHeight' });
  $clearance = input<string | undefined>(undefined, { alias: 'appPublishClearance' });

  constructor() {
    effect(onCleanup => {
      const property = this.$property();
      const clearance = this.$clearance();
      const view = this.document.defaultView;
      if (typeof ResizeObserver === 'undefined' || !view) {
        return;
      }
      const publish = (): void => {
        const box = this.element.getBoundingClientRect();
        this.root.style.setProperty(property, `${box.height}px`);
        if (clearance) {
          this.root.style.setProperty(clearance, `${Math.max(0, view.innerHeight - box.top)}px`);
        }
      };
      const observer = new ResizeObserver(publish);
      observer.observe(this.element);
      if (clearance) {
        view.addEventListener('scroll', publish, { passive: true });
        view.addEventListener('resize', publish);
      }
      onCleanup(() => {
        observer.disconnect();
        view.removeEventListener('scroll', publish);
        view.removeEventListener('resize', publish);
        this.root.style.removeProperty(property);
        if (clearance) {
          this.root.style.removeProperty(clearance);
        }
      });
    });
  }
}
