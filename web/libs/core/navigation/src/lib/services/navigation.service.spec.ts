import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { PermissionService, profileOf } from '@ims/core/identity';
import { PERMISSION } from '@ims/models';
import { NAV_BADGE_SOURCES, NavigationService } from './navigation.service';

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
        provide: NAV_BADGE_SOURCES,
        useValue: [{ menuId: 'approvals', $count: waiting.asReadonly() }]
      }
    ]
  });

  const ids = () => spectator.service.$entries().map(entry => entry.id);

  beforeEach(() => {
    granted.set(ADMIN);
    waiting.set(0);
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
});
