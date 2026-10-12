import { DestroyRef, Signal, effect, inject, signal, untracked } from '@angular/core';
import { LOADING_DELAY_MS, LOADING_MIN_SHOW_MS } from '../constants';

export interface DelayedLoadingOptions {
  /** How long the request must be pending before the indicator shows. */
  readonly delay?: number;
  /** How long the indicator stays once it showed. */
  readonly minShow?: number;
}

/**
 * Turns "a request is pending" into "show the skeleton or spinner": true only after the request has been pending for `delay` ms
 * (an answer inside that time never shows one), then for at least `minShow` ms. Bind the skeleton to the returned signal, keep
 * the real `aria-busy` on the raw one. Injection context only (field initializer).
 */
export function delayedLoading(
  loading: Signal<boolean>,
  { delay = LOADING_DELAY_MS, minShow = LOADING_MIN_SHOW_MS }: DelayedLoadingOptions = {}
): Signal<boolean> {
  const visible = signal(false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let shownAt = 0;

  effect(() => {
    const isLoading = loading();
    untracked(() => {
      clearTimeout(timer);
      if (isLoading && !visible()) {
        timer = setTimeout(() => {
          shownAt = Date.now();
          visible.set(true);
        }, delay);
      } else if (!isLoading && visible()) {
        const remaining = Math.max(0, minShow - (Date.now() - shownAt));
        timer = setTimeout(() => visible.set(false), remaining);
      }
    });
  });
  inject(DestroyRef).onDestroy(() => clearTimeout(timer));

  return visible.asReadonly();
}
