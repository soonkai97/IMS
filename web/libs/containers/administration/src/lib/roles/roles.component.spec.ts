import { signal } from '@angular/core';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { PermissionDto, PermissionGroup, RoleDto } from '@ims/api';
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
  const held = signal<string[]>([PERMISSION.RolesRead]);
  const load = jest.fn();
  const createComponent = createComponentFactory({
    component: RolesPageComponent,
    detectChanges: false,
    providers: [
      provideUiText(),
      { provide: PermissionService, useValue: { has: (code: string) => held().includes(code) } },
      {
        provide: RolesStore,
        useValue: {
          list: {
            $data: roles,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load,
            reload: jest.fn()
          },
          catalog: { $data: signal(CATALOG), load: jest.fn(), reload: jest.fn() }
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
    held.set([PERMISSION.RolesRead]);
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

  it('says that a role change applies within about 5 minutes', () => {
    expect(text()).toContain('within about 5 minutes');
  });

  it('shows "View only" to a person without roles.manage and not to one with it', () => {
    expect(text()).toContain('View only');

    held.set([PERMISSION.RolesRead, PERMISSION.RolesManage]);
    spectator.detectChanges();

    expect(text()).not.toContain('View only');
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
});
