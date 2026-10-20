import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter
} from '@angular/router';
import { FORBIDDEN_ROUTE, NOT_FOUND_ROUTE } from '../constants/identity.const';
import { ModulesStore } from '../services/modules.store';
import { PermissionService } from '../services/permission.service';
import { requireModule, requirePermission } from './identity.guards';

describe('requireModule (a route of a module that is off is "not found", never "forbidden")', () => {
  const on = new Set<string>();
  const held = new Set<string>();

  const run = (...guards: ReturnType<typeof requireModule>[]): boolean | UrlTree => {
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
    TestBed.inject(Router).serializeUrl(result as UrlTree);

  beforeEach(() => {
    on.clear();
    held.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ModulesStore, useValue: { isEnabled: (code: string) => on.has(code) } },
        {
          provide: PermissionService,
          useValue: { hasAny: (codes: string[]) => codes.some(code => held.has(code)) }
        }
      ]
    });
  });

  it('lets the route open while the module is on', () => {
    on.add('batch');

    expect(run(requireModule('batch'))).toBe(true);
  });

  it('sends the user to the not-found page while the module is off', () => {
    expect(urlOf(run(requireModule('batch')))).toBe(NOT_FOUND_ROUTE);
  });

  it('only the named module counts: another one being on does not open the route', () => {
    on.add('accounting');

    expect(urlOf(run(requireModule('batch')))).toBe(NOT_FOUND_ROUTE);
  });

  describe('module x permission matrix, with the module guard first as the routes put it', () => {
    it.each([
      ['on', true, 'held', true, true, undefined],
      ['on', true, 'missing', false, false, FORBIDDEN_ROUTE],
      ['off', false, 'held', true, false, NOT_FOUND_ROUTE],
      ['off', false, 'missing', false, false, NOT_FOUND_ROUTE]
    ])('module %s, permission %s', (_m, moduleOn, _p, permissionHeld, opens, redirectedTo) => {
      if (moduleOn) {
        on.add('batch');
      }
      if (permissionHeld) {
        held.add('lot.read');
      }

      const result = run(requireModule('batch'), requirePermission('lot.read'));

      if (opens) {
        expect(result).toBe(true);
      } else {
        expect(urlOf(result)).toBe(redirectedTo);
      }
    });
  });
});
