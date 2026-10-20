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
import { ROLE_ROUTES, SETTINGS_ROUTES } from './administration.routes';

const routeAt = (path: string) => ROLE_ROUTES.find(route => route.path === path);

describe('ROLE_ROUTES (the list and a role are gated by roles.read, nothing else)', () => {
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

  it('opens a role (:id) with roles.read and sends anyone else to the forbidden page', () => {
    expect(String(run(':id'))).toBe('/forbidden');
    held.add(PERMISSION.RolesManage);
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

  it('puts new before :id, so /roles/new is never read as a role id', () => {
    expect(ROLE_ROUTES.map(route => route.path)).toEqual(['', 'new', ':id']);
  });

  it.each(['new', ':id'])('asks before leaving unsaved edits on %s', path => {
    expect(routeAt(path)?.canDeactivate).toEqual([unsavedChangesGuard]);
  });
});

describe('SETTINGS_ROUTES (Settings needs settings.manage, Settings > Modules needs modules.manage, neither implies the other)', () => {
  const held = new Set<string>();

  const run = (path: string): boolean | UrlTree => {
    const guards = SETTINGS_ROUTES.find(route => route.path === path)?.canActivate as
      CanActivateFn[] | undefined;
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

  it('opens Modules for a holder of modules.manage', () => {
    held.add(PERMISSION.ModulesManage);

    expect(run('modules')).toBe(true);
  });

  it.each([
    ['nothing', []],
    ['settings.manage', [PERMISSION.SettingsManage]],
    ['roles.manage and users.manage', [PERMISSION.RolesManage, PERMISSION.UsersManage]]
  ])('sends a person holding %s to the forbidden page', (_label, codes) => {
    codes.forEach(code => held.add(code));

    expect(String(run('modules'))).toBe('/forbidden');
  });

  it('keeps Settings itself on settings.manage: modules.manage alone does not open it', () => {
    held.add(PERMISSION.ModulesManage);
    expect(String(run(''))).toBe('/forbidden');

    held.add(PERMISSION.SettingsManage);
    expect(run('')).toBe(true);
  });

  it('is not behind a module: the screen that turns a module on must open while every module is off', () => {
    const route = SETTINGS_ROUTES.find(item => item.path === 'modules');

    expect(route?.canActivate).toHaveLength(1);
    expect(route?.data).toBeUndefined();
  });
});
