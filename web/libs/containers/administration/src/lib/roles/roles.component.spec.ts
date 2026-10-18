import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { NEVER, of } from 'rxjs';
import { PermissionDto, PermissionGroup, PermissionsClient, RoleDto, RolesClient } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { provideUiText } from '@ims/core/i18n';
import { RolesStore } from '@ims/features/administration';
import { PERMISSION } from '@ims/models';
import { RolesPageComponent } from './roles.component';

const permission = (code: string, adminCore: boolean): PermissionDto => ({
  code,
  group: PermissionGroup.Administration,
  description: code,
  isDangerous: false,
  adminCore,
  requires: []
});
const CATALOG = [
  permission('users.manage', true),
  permission('roles.manage', true),
  permission('stock.read', false)
];
const role = (name: string, permissions: string[], extra: Partial<RoleDto> = {}): RoleDto => ({
  id: name,
  code: name,
  name,
  description: `${name} description`,
  isSystem: false,
  isActive: true,
  permissions,
  userCount: 2,
  concurrencyToken: 't',
  ...extra
});

describe('RolesPageComponent (RP01: a read-only list, kinds decided by the data)', () => {
  let spectator: Spectator<RolesPageComponent>;
  const roles = signal<RoleDto[] | undefined>(undefined);
  const catalog = signal<PermissionDto[] | undefined>(CATALOG);
  const listFailed = signal(false);
  const catalogFailed = signal(false);
  const catalogLoading = signal(false);
  const held = signal<string[]>([PERMISSION.RolesRead]);
  const load = jest.fn();
  const listReload = jest.fn();
  const catalogReload = jest.fn();
  const createComponent = createComponentFactory({
    component: RolesPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      provideUiText(),
      { provide: PermissionService, useValue: { has: (code: string) => held().includes(code) } }
    ],
    // The page provides its own store, so a test replaces it at the component.
    componentProviders: [
      {
        provide: RolesStore,
        useValue: {
          list: {
            $data: roles,
            $isLoading: signal(false),
            $isFailed: listFailed,
            load,
            reload: listReload
          },
          catalog: {
            $data: catalog,
            $isLoading: catalogLoading,
            $isFailed: catalogFailed,
            load: jest.fn(),
            reload: catalogReload
          }
        }
      }
    ]
  });

  const text = () => spectator.element.textContent ?? '';
  const rowOf = (name: string) =>
    Array.from(spectator.element.querySelectorAll('tr')).find(row =>
      row.textContent?.includes(name)
    );

  afterEach(() => jest.useRealTimers());

  beforeEach(() => {
    jest.useFakeTimers();
    load.mockClear();
    listReload.mockClear();
    catalogReload.mockClear();
    held.set([PERMISSION.RolesRead]);
    catalog.set(CATALOG);
    listFailed.set(false);
    catalogFailed.set(false);
    catalogLoading.set(false);
    roles.set([
      role('Chief', ['users.manage', 'roles.manage', 'stock.read'], { isSystem: true }),
      role('Standard', ['stock.read'], { isSystem: true }),
      role('Night clerk', ['stock.read'], { isActive: false, userCount: 0 })
    ]);
    spectator = createComponent();
    spectator.detectChanges();
  });

  it('marks the locked, standard and custom roles by words and counts the permissions', () => {
    expect(rowOf('Chief')?.textContent).toContain('System, locked');
    expect(rowOf('Chief')?.textContent).toContain('3 of 3');
    expect(rowOf('Standard')?.textContent).toMatch(/System(?!,)/);
    expect(rowOf('Night clerk')?.textContent).toContain('Custom');
    expect(rowOf('Night clerk')?.textContent).toContain('Inactive');
    expect(rowOf('Standard')?.textContent).toContain('2');
  });

  it('lists the standard roles first (the most powerful first), then the created ones', () => {
    roles.set([
      role('Zed', ['stock.read']),
      role('Small', ['stock.read'], { isSystem: true }),
      role('Chief', ['users.manage', 'roles.manage', 'stock.read'], { isSystem: true }),
      role('Alpha', [])
    ]);
    spectator.detectChanges();

    const names = Array.from(spectator.element.querySelectorAll('tbody tr')).map(row =>
      row.querySelector('th, td')?.textContent?.trim()
    );
    expect(names).toEqual(['Chief', 'Small', 'Alpha', 'Zed']);
  });

  it('shows no role, and so never a wrong kind, until the permission catalog is there', () => {
    catalog.set(undefined);
    catalogLoading.set(true);
    spectator.detectChanges();

    expect(rowOf('Chief')).toBeUndefined();
    expect(text()).not.toContain('System');
  });

  it('says that a role change applies within about 5 minutes', () => {
    expect(text()).toContain('within about 5 minutes');
  });

  it('shows "View only" to a person without roles.manage and not to one with it', () => {
    expect(text()).toContain('View only');

    held.set([PERMISSION.RolesRead, PERMISSION.RolesManage]);
    spectator.detectChanges();

    expect(text()).not.toContain('View only');
  });

  it('offers New role (to /roles/new) only to a person with roles.manage', () => {
    const link = () => spectator.element.querySelector('a[href="/roles/new"]');
    expect(link()).toBeNull();

    held.set([PERMISSION.RolesRead, PERMISSION.RolesManage]);
    spectator.detectChanges();

    expect(link()?.textContent).toContain('New role');
  });

  it('opens a role from its row, even for a person who may only read', () => {
    expect(spectator.component.rowLink(role('Night clerk', []))).toEqual(['/roles', 'Night clerk']);
  });

  it('loads the active roles first and the inactive ones when the box is ticked', () => {
    expect(load).toHaveBeenLastCalledWith({ search: undefined, includeInactive: false });

    spectator.component.includeInactive.setValue(true);
    spectator.detectChanges();

    expect(load).toHaveBeenLastCalledWith({ search: undefined, includeInactive: true });
  });

  it('sends the search text to the API after the typing pause', () => {
    spectator.component.$searchInput.set('  clerk ');
    spectator.detectChanges();
    jest.advanceTimersByTime(1000);
    spectator.detectChanges();

    expect(load).toHaveBeenLastCalledWith({ search: 'clerk', includeInactive: false });
  });

  it('never sends more than the 100 characters the API accepts, and the box says so', () => {
    spectator.component.$searchInput.set('x'.repeat(130));
    spectator.detectChanges();
    jest.advanceTimersByTime(1000);
    spectator.detectChanges();

    const [{ search }] = load.mock.calls[load.mock.calls.length - 1] as [{ search: string }];
    expect(search.length).toBe(100);
    expect(spectator.element.querySelector('input[type="search"]')?.getAttribute('maxlength')).toBe(
      '100'
    );
  });

  it('shows 25 roles to a page and the pager', () => {
    roles.set(Array.from({ length: 30 }, (_, i) => role(`Role ${i}`, [])));
    spectator.detectChanges();

    expect(spectator.element.querySelectorAll('tbody tr').length).toBe(25);
    expect(text()).toContain('1-25 of 30');

    spectator.component.onPageChange(2);
    spectator.detectChanges();

    expect(spectator.element.querySelectorAll('tbody tr').length).toBe(5);
  });

  it('says so when nothing matches the search and when there are no roles', () => {
    roles.set([]);
    spectator.detectChanges();
    expect(text()).toContain('There are no roles to show.');

    spectator.component.$searchInput.set('zzz');
    spectator.detectChanges();
    jest.advanceTimersByTime(1000);
    spectator.detectChanges();

    expect(text()).toContain('No role matches "zzz".');
  });

  it.each([
    ['the roles', listFailed],
    ['the permission catalog', catalogFailed]
  ])('a failed load of %s shows the error and Retry loads both again', (_what, failed) => {
    failed.set(true);
    spectator.detectChanges();

    expect(rowOf('Chief')).toBeUndefined();
    const retry = Array.from(spectator.element.querySelectorAll('button')).find(button =>
      button.textContent?.includes('Retry')
    );
    expect(retry).toBeDefined();

    retry?.click();

    expect(listReload).toHaveBeenCalledTimes(1);
    expect(catalogReload).toHaveBeenCalledTimes(1);
  });
});

describe('RolesPageComponent store (what one signed-in person read is not shown to the next)', () => {
  const rolesClient = { list: jest.fn() };
  const permissionsClient = { list: jest.fn() };
  const createComponent = createComponentFactory({
    component: RolesPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      provideUiText(),
      { provide: PermissionService, useValue: { has: () => false } },
      { provide: RolesClient, useValue: rolesClient },
      { provide: PermissionsClient, useValue: permissionsClient }
    ]
  });

  it('gives every page its own store: a new page starts empty while the answer is slow', () => {
    rolesClient.list.mockReturnValueOnce(of([role('Previous user role', ['stock.read'])]));
    permissionsClient.list.mockReturnValue(of(CATALOG));
    const first = createComponent();
    first.detectChanges();
    expect(first.element.textContent).toContain('Previous user role');
    first.fixture.destroy();

    rolesClient.list.mockReturnValueOnce(NEVER);
    const second = createComponent();
    second.detectChanges();

    expect(second.element.textContent).not.toContain('Previous user role');
  });
});
