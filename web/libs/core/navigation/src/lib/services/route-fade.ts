import { inject } from '@angular/core';
import { Router, ViewTransitionInfo, ViewTransitionsFeatureOptions } from '@angular/router';

/**
 * Options of the route fade (`withViewTransitions`; the CSS is `_route-fade.scss`). A navigation that stays on the same page (a new
 * query string or fragment) does not fade: the page would flash on every keystroke of a filter.
 */
export const ROUTE_FADE: ViewTransitionsFeatureOptions = {
  skipInitialTransition: true,
  onViewTransitionCreated: skipOnSamePage
};

/** Runs in an injection context (the router calls it so). */
function skipOnSamePage({ transition }: ViewTransitionInfo): void {
  const router = inject(Router);
  const target = router.getCurrentNavigation()?.finalUrl;
  if (
    target &&
    router.isActive(target, {
      paths: 'exact',
      matrixParams: 'exact',
      queryParams: 'ignored',
      fragment: 'ignored'
    })
  ) {
    transition.skipTransition();
  }
}
