import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { PermissionService } from '@ims/core/identity';
import { PERMISSION, ROLE } from '@ims/models';
import { NAV_BADGE_SOURCES, NavigationService } from './navigation.service';

const PERMISSIONS_BY_ROLE: Record<string, string[]> = {
  [ROLE.Admin]: Object.values(PERMISSION),
  [ROLE.Owner]: [
    PERMISSION.UsersRead,
    PERMISSION.MasterDataRead,
    PERMISSION.DocumentApprove,
    PERMISSION.StockRead,
    PERMISSION.ReportsRead,
    PERMISSION.AuditRead
  ],
  [ROLE.WarehouseStaff]: [
    PERMISSION.MasterDataRead,
    PERMISSION.DocumentCreate,
    PERMISSION.DocumentPost,
    PERMISSION.StockRead
  ]
};

describe('NavigationService (menus follow the role)', () => {
  const role = signal<string>(ROLE.Admin);
  const waiting = signal(0);
  let spectator: SpectatorService<NavigationService>;
  const createService = createServiceFactory({
    service: NavigationService,
    providers: [
      {
        provide: PermissionService,
        useValue: {
          $role: role.asReadonly(),
          hasAny: (permissions: string[]) =>
            permissions.length === 0 ||
            permissions.some(p => PERMISSIONS_BY_ROLE[role()].includes(p))
        }
      },
      {
        provide: NAV_BADGE_SOURCES,
        useValue: [{ menuId: 'approvals', $count: waiting.asReadonly() }]
      }
    ]
  });

  const ids = () => spectator.service.$entries().map(entry => entry.id);

  beforeEach(() => {
    role.set(ROLE.Admin);
    waiting.set(0);
    spectator = createService();
  });

  it('warehouse staff do not see approvals, reports, users, settings or audit', () => {
    role.set(ROLE.WarehouseStaff);

    expect(ids()).toEqual(
      expect.arrayContaining(['home', 'receipts', 'issues', 'stock', 'low-stock', 'products'])
    );
    for (const hidden of ['approvals', 'reports', 'users', 'settings', 'audit']) {
      expect(ids()).not.toContain(hidden);
    }
  });

  it('owner sees approvals, reports and audit but not users or settings', () => {
    role.set(ROLE.Owner);

    expect(ids()).toEqual(expect.arrayContaining(['approvals', 'reports', 'audit']));
    expect(ids()).not.toContain('users');
    expect(ids()).not.toContain('settings');
  });

  it('admin sees everything', () => {
    expect(ids()).toEqual(expect.arrayContaining(['users', 'settings', 'audit', 'approvals']));
  });

  it('phone tabs are four per role and the rest moves under More', () => {
    role.set(ROLE.WarehouseStaff);

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
});
