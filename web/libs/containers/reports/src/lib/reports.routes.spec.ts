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
import { REPORT_ROUTES } from './reports.routes';

describe('REPORT_ROUTES: Accounting export (module accounting, then permission accounting.export)', () => {
  const held = new Set<string>();
  let accountingOn = true;
  let router: Router;

  const canActivate = (): (boolean | UrlTree)[] => {
    const guards = REPORT_ROUTES.find(route => route.path === 'accounting')?.canActivate as
      CanActivateFn[] | undefined;
    expect(guards).toHaveLength(2);
    // The router stops at the first guard that refuses; run them in order the same way.
    const answers: (boolean | UrlTree)[] = [];
    for (const guard of guards as CanActivateFn[]) {
      const answer = TestBed.runInInjectionContext(() =>
        guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      ) as boolean | UrlTree;
      answers.push(answer);
      if (answer !== true) {
        break;
      }
    }
    return answers;
  };
  const urlOf = (answer: boolean | UrlTree | undefined): string =>
    answer instanceof UrlTree ? router.serializeUrl(answer) : String(answer);

  beforeEach(() => {
    held.clear();
    accountingOn = true;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: PermissionService,
          useValue: { hasAny: (codes: string[]) => codes.some(code => held.has(code)) }
        },
        {
          provide: ModulesStore,
          useValue: { isEnabled: (code: string) => code === MODULE_CODE.Accounting && accountingOn }
        }
      ]
    });
    router = TestBed.inject(Router);
  });

  it('lets in a holder of accounting.export while the module is on', () => {
    held.add(PERMISSION.AccountingExport);

    expect(canActivate()).toEqual([true, true]);
  });

  it('the module off is the not-found page for everybody, a holder of the permission included', () => {
    accountingOn = false;
    held.add(PERMISSION.AccountingExport);

    const answers = canActivate();

    expect(urlOf(answers.at(-1))).toBe('/not-found');
  });

  it('the module off and no permission is also the not-found page, so nothing is learnt about the feature', () => {
    accountingOn = false;

    expect(urlOf(canActivate().at(-1))).toBe('/not-found');
  });

  it('the module on and no permission is the forbidden page', () => {
    held.add(PERMISSION.ReportsRead);

    expect(urlOf(canActivate().at(-1))).toBe('/forbidden');
  });

  it('the reports page itself still needs only reports.read and has no module', () => {
    const reports = REPORT_ROUTES.find(route => route.path === '');

    expect(reports?.canActivate).toHaveLength(1);
  });
});
