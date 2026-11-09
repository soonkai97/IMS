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
import { PRICE_LIST_ROUTES } from './price-list.routes';

describe('PRICE_LIST_ROUTES (module sales, then permission pricelist.manage)', () => {
  const held = new Set<string>();
  let salesOn = true;
  let router: Router;

  /** The guards of the parent run in order, as the router does: the first that refuses decides. */
  const answer = (): boolean | UrlTree => {
    for (const guard of PRICE_LIST_ROUTES[0].canActivate as CanActivateFn[]) {
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

  it('lets in a holder of pricelist.manage while the module is on', () => {
    held.add(PERMISSION.PriceListManage);

    expect(answer()).toBe(true);
  });

  it('shows the not-found page, never the forbidden one, while the module is off (even for a holder)', () => {
    salesOn = false;
    held.add(PERMISSION.PriceListManage);

    expect(urlOf(answer())).toBe('/not-found');
  });

  it('shows the forbidden page to so.manage and so.read: a selling price list is for pricelist.manage only', () => {
    held.add(PERMISSION.SoManage);
    held.add(PERMISSION.SoRead);

    expect(urlOf(answer())).toBe('/forbidden');
  });

  it('the new page comes before :id, which would take it for a list, and both ask before leaving', () => {
    const children = PRICE_LIST_ROUTES[0].children ?? [];
    const paths = children.map(child => child.path);

    expect(paths.indexOf('new')).toBeLessThan(paths.indexOf(':id'));
    for (const path of ['new', ':id']) {
      expect(children.find(child => child.path === path)?.canDeactivate).toHaveLength(1);
    }
  });
});
