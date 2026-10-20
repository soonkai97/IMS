import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { ModulesStore, PermissionService, profileOf } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { MENU_ITEMS } from '../constants/menu.const';
import { MenuItem } from '../types/menu.types';
import { MENU_ITEM_LIST, NAV_BADGE_SOURCES, NavigationService } from './navigation.service';

const ADMIN: string[] = Object.values(PERMISSION);
const OWNER: string[] = [
  PERMISSION.UsersRead,
  PERMISSION.MasterDataRead,
  PERMISSION.DocumentApprove,
  PERMISSION.StockRead,
  PERMISSION.ReportsRead,
  PERMISSION.AuditRead
];
const WAREHOUSE_STAFF: string[] = [
  PERMISSION.MasterDataRead,
  PERMISSION.DocumentCreate,
  PERMISSION.DocumentPost,
  PERMISSION.StockRead
];

describe('NavigationService (menus follow the permissions)', () => {
  const granted = signal<string[]>(ADMIN);
  const waiting = signal(0);
  const modulesOn = signal<string[]>([]);
  const items = signal<readonly MenuItem[]>(MENU_ITEMS);
  let spectator: SpectatorService<NavigationService>;
  const createService = createServiceFactory({
    service: NavigationService,
    providers: [
      {
        provide: PermissionService,
        useValue: {
          $profile: () => profileOf(new Set(granted())),
          hasAny: (permissions: string[]) =>
            permissions.length === 0 || permissions.some(p => granted().includes(p))
        }
      },
      {
        provide: ModulesStore,
        useValue: { isEnabled: (code: string) => modulesOn().includes(code) }
      },
      { provide: MENU_ITEM_LIST, useFactory: () => items() },
      {
        provide: NAV_BADGE_SOURCES,
        useValue: [{ menuId: 'approvals', $count: waiting.asReadonly() }]
      }
    ]
  });

  const ids = () => spectator.service.$entries().map(entry => entry.id);

  beforeEach(() => {
    granted.set(ADMIN);
    waiting.set(0);
    modulesOn.set([]);
    items.set(MENU_ITEMS);
    spectator = createService();
  });

  it('warehouse staff do not see approvals, reports, users, settings or audit', () => {
    granted.set(WAREHOUSE_STAFF);

    expect(ids()).toEqual(
      expect.arrayContaining(['home', 'receipts', 'issues', 'stock', 'low-stock', 'products'])
    );
    for (const hidden of ['approvals', 'reports', 'users', 'settings', 'audit']) {
      expect(ids()).not.toContain(hidden);
    }
  });

  it('owner sees approvals, reports and audit but not users or settings', () => {
    granted.set(OWNER);

    expect(ids()).toEqual(expect.arrayContaining(['approvals', 'reports', 'audit']));
    expect(ids()).not.toContain('users');
    expect(ids()).not.toContain('settings');
  });

  it('a reviewer who holds only the large-document tier still sees approvals', () => {
    granted.set([PERMISSION.DocumentApproveLarge, PERMISSION.StockRead, PERMISSION.ReportsRead]);

    expect(ids()).toContain('approvals');
    expect(spectator.service.$phoneTabs().map(entry => entry.id)).toEqual([
      'home',
      'approvals',
      'stock',
      'reports'
    ]);
  });

  it('Roles follows roles.read alone: users.manage does not show it, a reader of roles does', () => {
    granted.set([PERMISSION.UsersManage, PERMISSION.UsersRead]);
    expect(ids()).toContain('users');
    expect(ids()).not.toContain('roles');

    granted.set([PERMISSION.RolesRead]);
    expect(ids()).toContain('roles');
    expect(ids()).not.toContain('users');
  });

  it('admin sees everything', () => {
    expect(ids()).toEqual(
      expect.arrayContaining(['users', 'roles', 'settings', 'audit', 'approvals'])
    );
  });

  it('phone tabs come from the permissions and the rest moves under More', () => {
    granted.set(WAREHOUSE_STAFF);

    expect(spectator.service.$phoneTabs().map(entry => entry.id)).toEqual([
      'home',
      'receipts',
      'issues',
      'stock'
    ]);
    expect(spectator.service.$moreEntries().map(entry => entry.id)).toContain('products');
  });

  it('shows the waiting-approvals count as a badge', () => {
    waiting.set(3);

    expect(spectator.service.$entries().find(entry => entry.id === 'approvals')?.badge).toBe(3);
  });

  it('Modules (Settings > Modules) follows modules.manage alone and belongs to no module, so it is never hidden by a switch', () => {
    granted.set([PERMISSION.ModulesManage]);
    expect(ids()).toContain('modules');
    expect(ids()).not.toContain('settings');

    granted.set([PERMISSION.SettingsManage]);
    expect(ids()).toContain('settings');
    expect(ids()).not.toContain('modules');

    expect(MENU_ITEMS.filter(item => item.module !== undefined)).toEqual([]);
  });
});

describe('NavigationService (an item tied to a module: module x permission)', () => {
  const granted = signal<string[]>([]);
  const modulesOn = signal<string[]>([]);
  const lot: MenuItem = {
    id: 'lots',
    labelKey: 'nav.stock',
    icon: 'stock',
    route: '/lots',
    permissions: [PERMISSION.StockRead],
    group: 'stock',
    module: 'batch'
  };
  const anyone: MenuItem = { ...lot, id: 'free', route: '/free', permissions: [], module: 'batch' };
  const core: MenuItem = { ...lot, id: 'core', route: '/core', module: undefined };
  let spectator: SpectatorService<NavigationService>;
  const createService = createServiceFactory({
    service: NavigationService,
    providers: [
      {
        provide: PermissionService,
        useValue: {
          $profile: () => profileOf(new Set(granted())),
          hasAny: (permissions: string[]) =>
            permissions.length === 0 || permissions.some(p => granted().includes(p))
        }
      },
      {
        provide: ModulesStore,
        useValue: { isEnabled: (code: string) => modulesOn().includes(code) }
      },
      { provide: MENU_ITEM_LIST, useValue: [lot, anyone, core] }
    ]
  });
  const ids = () => spectator.service.$entries().map(entry => entry.id);

  beforeEach(() => {
    granted.set([]);
    modulesOn.set([]);
    spectator = createService();
  });

  it.each([
    ['on', ['batch'], 'held', [PERMISSION.StockRead], ['lots', 'free', 'core']],
    ['on', ['batch'], 'missing', [], ['free']],
    ['off', [], 'held', [PERMISSION.StockRead], ['core']],
    ['off', [], 'missing', [], []]
  ])(
    'module %s (%j), permission %s',
    (_moduleState, on, _permissionState, permissions, expected) => {
      modulesOn.set(on);
      granted.set(permissions);

      expect(ids()).toEqual(expected);
    }
  );

  it('an item without a module is core: it stays whatever the switches say', () => {
    granted.set([PERMISSION.StockRead]);
    modulesOn.set(['something-else']);

    expect(ids()).toEqual(['core']);
  });

  it('follows a switch at once, in both directions', () => {
    granted.set([PERMISSION.StockRead]);
    expect(ids()).toEqual(['core']);

    modulesOn.set(['batch']);
    expect(ids()).toEqual(['lots', 'free', 'core']);

    modulesOn.set([]);
    expect(ids()).toEqual(['core']);
  });
});
