import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ViewTransitionInfo } from '@angular/router';
import { ROUTE_FADE } from './route-fade';

describe('ROUTE_FADE (R10 route fade)', () => {
  const finalUrl = {} as UrlTree;
  const skipTransition = jest.fn();
  const info = { transition: { skipTransition } } as unknown as ViewTransitionInfo;

  function run(router: Partial<Router>): void {
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: router }] });
    TestBed.runInInjectionContext(() => ROUTE_FADE.onViewTransitionCreated?.(info));
  }

  beforeEach(() => skipTransition.mockClear());

  it('does not fade the first page load', () => {
    expect(ROUTE_FADE.skipInitialTransition).toBe(true);
  });

  it('skips the fade when the target is the page already shown (a filter or tab changes the query)', () => {
    const isActive = jest.fn().mockReturnValue(true);

    run({ getCurrentNavigation: () => ({ finalUrl }) as never, isActive });

    expect(skipTransition).toHaveBeenCalledTimes(1);
    expect(isActive).toHaveBeenCalledWith(
      finalUrl,
      expect.objectContaining({ paths: 'exact', queryParams: 'ignored' })
    );
  });

  it('fades when the target is another page', () => {
    run({ getCurrentNavigation: () => ({ finalUrl }) as never, isActive: () => false });

    expect(skipTransition).not.toHaveBeenCalled();
  });
});
