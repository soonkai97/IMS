import { InMemoryScrollingOptions } from '@angular/router';

/**
 * Options of `withInMemoryScrolling`. The window scrolls (the shell has no inner scroll box). A new page opens at its top: without
 * this, a page opened from a scrolled list lands mid-list with its title and primary button out of view. Back and forward restore
 * the old position; a `#fragment` link scrolls to its element. The jump is instant (no `scroll-behavior: smooth` is set anywhere),
 * so reduced motion needs no exception.
 */
export const ROUTE_SCROLLING: InMemoryScrollingOptions = {
  scrollPositionRestoration: 'enabled',
  anchorScrolling: 'enabled'
};
