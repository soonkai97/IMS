import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/**
 * Publishes the element's real height as a custom property on `<html>` (`appPublishHeight="--ims-action-bar-height"`), kept current
 * by a ResizeObserver and removed with the element. The sticky action bar wraps at 320px, so the page's scroll padding needs its
 * real height to scroll a focused control clear of it. With `publishScope` (a selector) the property goes on the closest matching ancestor
 * instead, so several of them on a page do not fight (the header of each app-table).
 */
@Directive({ selector: '[appPublishHeight]' })
export class UiPublishHeightDirective {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly root = inject(DOCUMENT).documentElement;

  $property = input.required<string>({ alias: 'appPublishHeight' });
  $scope = input<string | undefined>(undefined, { alias: 'publishScope' });

  constructor() {
    effect(onCleanup => {
      const property = this.$property();
      const scope = this.$scope();
      const target = (scope ? this.element.closest<HTMLElement>(scope) : null) ?? this.root;
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      const observer = new ResizeObserver(() =>
        target.style.setProperty(property, `${this.element.getBoundingClientRect().height}px`)
      );
      observer.observe(this.element);
      onCleanup(() => {
        observer.disconnect();
        target.style.removeProperty(property);
      });
    });
  }
}
