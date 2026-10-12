import { inject } from '@angular/core';
import { Router, ViewTransitionInfo, ViewTransitionsFeatureOptions } from '@angular/router';

/**
 * The options of the route fade (`withViewTransitions`). A navigation that stays on the same page (a new query string or
 * fragment, such as a filter or a tab) does not fade: the page would flash on every keystroke of a filter. The fade itself is
 * in `@ims/styles` (`_route-fade.scss`); it is a crossfade of one `fast` duration and so is instant under reduced motion.
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
