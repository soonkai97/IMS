import { ViewportScroller } from '@angular/common';
import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter, withInMemoryScrolling } from '@angular/router';
import { ROUTE_SCROLLING } from './route-scrolling';

@Component({ selector: 'app-scroll-page', template: '' })
class PageComponent {}

@Component({ selector: 'app-scroll-root', imports: [RouterOutlet], template: '<router-outlet />' })
class RootComponent {}

describe('ROUTE_SCROLLING (phone QA: a new page opens at its top)', () => {
  const scrollToPosition = jest.fn();
  const scrollToAnchor = jest.fn();

  /** The router starts its scroller when the app is bootstrapped, so the test bootstraps a root with an outlet. */
  async function bootstrap(options: typeof ROUTE_SCROLLING): Promise<Router> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [
            { path: 'a', component: PageComponent },
            { path: 'b', component: PageComponent }
          ],
          withInMemoryScrolling(options)
        ),
        {
          provide: ViewportScroller,
          useValue: {
            scrollToPosition,
            scrollToAnchor,
            getScrollPosition: () => [0, 640],
            setOffset: jest.fn(),
            setHistoryScrollRestoration: jest.fn()
          }
        }
      ]
    });
    const host = window.document.createElement('app-scroll-root');
    window.document.body.appendChild(host);
    const app = TestBed.inject(ApplicationRef);
    app.bootstrap(RootComponent, host);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/a');
    await app.whenStable();
    scrollToPosition.mockClear();
    scrollToAnchor.mockClear();
    return router;
  }

  afterEach(() => window.document.querySelectorAll('app-scroll-root').forEach(e => e.remove()));

  it('is the option set the app registers: restore on Back, top for a new page, anchors on', () => {
    expect(ROUTE_SCROLLING).toEqual({
      scrollPositionRestoration: 'enabled',
      anchorScrolling: 'enabled'
    });
  });

  it('scrolls to the top when another page opens', async () => {
    const router = await bootstrap(ROUTE_SCROLLING);

    await router.navigateByUrl('/b');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(scrollToPosition).toHaveBeenCalledWith([0, 0]);
  });

  it('leaves the position alone without the option (the bug this protects against)', async () => {
    const router = await bootstrap({});

    await router.navigateByUrl('/b');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(scrollToPosition).not.toHaveBeenCalled();
  });

  it('a #fragment link still scrolls to its element', async () => {
    const router = await bootstrap(ROUTE_SCROLLING);

    await router.navigateByUrl('/b#details');
    await TestBed.inject(ApplicationRef).whenStable();

    expect(scrollToAnchor).toHaveBeenCalledWith('details');
  });
});
