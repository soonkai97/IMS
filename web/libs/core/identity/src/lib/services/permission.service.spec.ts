import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { UserInfo } from '@ims/api';
import { AuthService } from './auth.service';
import { PermissionService } from './permission.service';

const WAREHOUSE_ID = '00000000-0000-4000-8000-000000000003';

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
  });

  it('is built from the /api/auth/me payload', () => {
    user.set({
      id: '00000000-0000-4000-8000-000000000001',
      username: 'sup',
      displayName: 'Sup',
      role: 'Supervisor',
      permissions: ['stock.read', 'document.approve'],
      warehouseIds: [WAREHOUSE_ID]
    });

    expect(spectator.service.has('document.approve')).toBe(true);
    expect(spectator.service.has('users.manage')).toBe(false);
    expect(spectator.service.hasAny(['users.manage', 'stock.read'])).toBe(true);
    expect(spectator.service.$role()).toBe('Supervisor');
    expect(spectator.service.$warehouseIds()).toEqual([WAREHOUSE_ID]);
    expect(spectator.service.$seesAllWarehouses()).toBe(false);
    expect(spectator.service.$profile().canApprove).toBe(true);
  });

  it('treats an empty requirement as everybody and Admin/Owner as seeing all warehouses', () => {
    user.set({
      id: '00000000-0000-4000-8000-000000000001',
      username: 'a',
      displayName: 'A',
      role: 'Admin',
      permissions: [],
      warehouseIds: []
    });

    expect(spectator.service.hasAny([])).toBe(true);
    expect(spectator.service.$seesAllWarehouses()).toBe(true);
  });
});
