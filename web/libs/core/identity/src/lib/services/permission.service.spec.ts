import { signal } from '@angular/core';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { UserInfo } from '@ims/api';
import { AuthService } from './auth.service';
import { PermissionService } from './permission.service';

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
      id: 1,
      username: 'sup',
      displayName: 'Sup',
      role: 'Supervisor',
      permissions: ['stock.read', 'document.approve'],
      warehouseIds: [3]
    });

    expect(spectator.service.has('document.approve')).toBe(true);
    expect(spectator.service.has('users.manage')).toBe(false);
    expect(spectator.service.hasAny(['users.manage', 'stock.read'])).toBe(true);
    expect(spectator.service.$role()).toBe('Supervisor');
    expect(spectator.service.$warehouseIds()).toEqual([3]);
    expect(spectator.service.$seesAllWarehouses()).toBe(false);
  });

  it('treats an empty requirement as everybody and Admin/Owner as seeing all warehouses', () => {
    user.set({
      id: 1,
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
