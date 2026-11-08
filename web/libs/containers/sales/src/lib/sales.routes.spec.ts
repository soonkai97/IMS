import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter
} from '@angular/router';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { MODULE_CODE, PERMISSION } from '@ims/models';
import { SALES_ORDER_ROUTES } from './sales.routes';

describe('SALES_ORDER_ROUTES (module sales, then permission so.read)', () => {
  const held = new Set<string>();
  let salesOn = true;
  let router: Router;

  /** The guards of the parent run in order, as the router does: the first that refuses decides. */
  const answer = (): boolean | UrlTree => {
    for (const guard of SALES_ORDER_ROUTES[0].canActivate as CanActivateFn[]) {
      const result = TestBed.runInInjectionContext(() =>
        guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      ) as boolean | UrlTree;
      if (result !== true) {
        return result;
      }
    }
    return true;
  };
  const urlOf = (result: boolean | UrlTree): string =>
    result instanceof UrlTree ? router.serializeUrl(result) : String(result);

  beforeEach(() => {
    held.clear();
    salesOn = true;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: PermissionService,
          useValue: { hasAny: (codes: string[]) => codes.some(code => held.has(code)) }
        },
        {
          provide: ModulesStore,
          useValue: { isEnabled: (code: string) => code === MODULE_CODE.Sales && salesOn }
        }
      ]
    });
    router = TestBed.inject(Router);
  });

  it('lets in a holder of so.read while the module is on', () => {
    held.add(PERMISSION.SoRead);

    expect(answer()).toBe(true);
  });

  it('shows the not-found page, never the forbidden one, while the module is off (even for a holder)', () => {
    salesOn = false;
    held.add(PERMISSION.SoRead);

    expect(urlOf(answer())).toBe('/not-found');
  });

  it('module off AND no so.read: still the not-found page, so a person without the right learns no more', () => {
    salesOn = false;

    expect(urlOf(answer())).toBe('/not-found');
  });

  it('shows the forbidden page to a user without so.read while the module is on', () => {
    held.add(PERMISSION.PoRead);

    expect(urlOf(answer())).toBe('/forbidden');
  });
});
