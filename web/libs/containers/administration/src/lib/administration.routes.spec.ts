import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  RouterStateSnapshot,
  UrlTree,
  provideRouter
} from '@angular/router';
import { PermissionService } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { unsavedChangesGuard } from '@ims/core/navigation';
import { ROLE_ROUTES } from './administration.routes';

const routeAt = (path: string) => ROLE_ROUTES.find(route => route.path === path);

describe('ROLE_ROUTES (the list and a role read with roles.read, a new role needs roles.manage)', () => {
  const held = new Set<string>();

  const run = (path = ''): boolean | UrlTree => {
    const guards = routeAt(path)?.canActivate as CanActivateFn[] | undefined;
    expect(guards).toHaveLength(1);
    return TestBed.runInInjectionContext(() =>
      (guards as CanActivateFn[])[0]({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    ) as boolean | UrlTree;
  };

  beforeEach(() => {
    held.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: PermissionService,
          useValue: { hasAny: (codes: string[]) => codes.some(code => held.has(code)) }
        }
      ]
    });
  });

  it('lets a holder of roles.read in', () => {
    held.add(PERMISSION.RolesRead);

    expect(run()).toBe(true);
  });

  it.each([
    ['nothing', []],
    ['users.manage and masterdata.manage', [PERMISSION.UsersManage, PERMISSION.MasterDataManage]],
    ['roles.manage without roles.read', [PERMISSION.RolesManage]]
  ])('sends a person holding %s to the forbidden page', (_label, codes) => {
    codes.forEach(code => held.add(code));

    const result = run();

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/forbidden');
  });

  it('puts new before :id, so the address /roles/new is never read as a role id', () => {
    expect(ROLE_ROUTES.map(route => route.path)).toEqual(['', 'new', ':id']);
  });

  it('opens a role with roles.read and sends anyone else to the forbidden page', () => {
    expect(String(run(':id'))).toBe('/forbidden');
    held.add(PERMISSION.RolesRead);

    expect(run(':id')).toBe(true);
  });

  it('opens the new-role form only with roles.manage: roles.read alone is not enough', () => {
    held.add(PERMISSION.RolesRead);
    expect(String(run('new'))).toBe('/forbidden');

    held.add(PERMISSION.RolesManage);

    expect(run('new')).toBe(true);
  });

  it.each(['new', ':id'])('asks before leaving unsaved edits on %s', path => {
    expect(routeAt(path)?.canDeactivate).toEqual([unsavedChangesGuard]);
  });
});
