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
import { PURCHASE_ORDER_ROUTES } from './purchasing.routes';

describe('PURCHASE_ORDER_ROUTES (module purchasing, then permission po.read)', () => {
  const held = new Set<string>();
  let purchasingOn = true;
  let router: Router;

  /** The guards of the parent run in order, as the router does: the first that refuses decides. */
  const answer = (
    guards = PURCHASE_ORDER_ROUTES[0].canActivate as CanActivateFn[]
  ): boolean | UrlTree => {
    for (const guard of guards) {
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
    purchasingOn = true;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: PermissionService,
          useValue: { hasAny: (codes: string[]) => codes.some(code => held.has(code)) }
        },
        {
          provide: ModulesStore,
          useValue: { isEnabled: (code: string) => code === MODULE_CODE.Purchasing && purchasingOn }
        }
      ]
    });
    router = TestBed.inject(Router);
  });

  it('lets in a holder of po.read while the module is on', () => {
    held.add(PERMISSION.PoRead);

    expect(answer()).toBe(true);
  });

  it('shows the not-found page, never the forbidden one, while the module is off (even for a holder)', () => {
    purchasingOn = false;
    held.add(PERMISSION.PoRead);

    expect(urlOf(answer())).toBe('/not-found');
  });

  it('module off AND no po.read: still the not-found page, so a person without the right learns no more', () => {
    purchasingOn = false;
    held.add(PERMISSION.StockRead);

    expect(urlOf(answer())).toBe('/not-found');
  });

  it('shows the forbidden page to a user without po.read while the module is on', () => {
    held.add(PERMISSION.StockRead);

    expect(urlOf(answer())).toBe('/forbidden');
  });

  it.each([['new'], [':id/edit']])(
    'the editor (%s) needs po.manage AND cost.view: each one alone is the forbidden page, and it asks before leaving',
    path => {
      const route = PURCHASE_ORDER_ROUTES[0].children?.find(child => child.path === path);
      const guards = route?.canActivate as CanActivateFn[];

      held.add(PERMISSION.PoManage);
      expect(urlOf(answer(guards))).toBe('/forbidden');
      held.clear();
      held.add(PERMISSION.CostView);
      expect(urlOf(answer(guards))).toBe('/forbidden');
      held.add(PERMISSION.PoManage);
      expect(answer(guards)).toBe(true);
      expect(route?.canDeactivate).toHaveLength(1);
    }
  );
});
