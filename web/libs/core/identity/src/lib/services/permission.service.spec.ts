import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { UserInfo } from '@ims/api';
import { AuthService } from './auth.service';
import { PermissionService } from './permission.service';

const WAREHOUSE_ID = '00000000-0000-4000-8000-000000000003';

function userInfo(overrides: Partial<UserInfo> = {}): UserInfo {
  return {
    id: '00000000-0000-4000-8000-000000000001',
    username: 'sup',
    displayName: 'Sup',
    role: 'Supervisor',
    roleName: 'Supervisor',
    permissions: ['stock.read', 'document.approve'],
    seesAllWarehouses: false,
    warehouseIds: [WAREHOUSE_ID],
    modules: [],
    ...overrides
  };
}

describe('PermissionService', () => {
  const user = signal<UserInfo | undefined>(undefined);
  let spectator: SpectatorService<PermissionService>;
  const createService = createServiceFactory({
    service: PermissionService,
    providers: [{ provide: AuthService, useValue: { $user: user.asReadonly() } }]
  });

  beforeEach(() => {
    user.set(undefined);
    spectator = createService();
  });

  it('has no permissions while signed out', () => {
    expect(spectator.service.has('stock.read')).toBe(false);
    expect(spectator.service.hasAny(['stock.read'])).toBe(false);
    expect(spectator.service.$seesAllWarehouses()).toBe(false);
  });

  it('is built from the /api/auth/me payload', () => {
    user.set(userInfo());

    expect(spectator.service.has('document.approve')).toBe(true);
    expect(spectator.service.has('users.manage')).toBe(false);
    expect(spectator.service.hasAny(['users.manage', 'stock.read'])).toBe(true);
    expect(spectator.service.$role()).toBe('Supervisor');
    expect(spectator.service.$warehouseIds()).toEqual([WAREHOUSE_ID]);
    expect(spectator.service.$seesAllWarehouses()).toBe(false);
    expect(spectator.service.$profile().canApprove).toBe(true);
  });

  it('treats an empty requirement as everybody', () => {
    user.set(userInfo({ permissions: [] }));

    expect(spectator.service.hasAny([])).toBe(true);
  });

  it('reads "sees every warehouse" from the user, never from the role name', () => {
    // A role called Admin that is limited to a warehouse, and a custom role that is not: the flag decides.
    user.set(userInfo({ role: 'Admin', seesAllWarehouses: false }));
    expect(spectator.service.$seesAllWarehouses()).toBe(false);

    user.set(userInfo({ role: 'Regional lead', seesAllWarehouses: true, warehouseIds: [] }));
    expect(spectator.service.$seesAllWarehouses()).toBe(true);
  });
});
