import { signal } from '@angular/core';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { PermissionService } from '@ims/core/identity';
import { ROLE } from '@ims/models';
import { LIST_SCOPE, ListScopePreference } from './list-scope.preference';

describe('ListScopePreference', () => {
  const role = signal<string | undefined>(ROLE.Clerk);
  let spectator: SpectatorService<ListScopePreference>;
  const createService = createServiceFactory({
    service: ListScopePreference,
    providers: [mockProvider(PermissionService, { $role: role, $userId: signal(7) })]
  });

  beforeEach(() => {
    localStorage.clear();
    spectator = createService();
  });

  it('starts warehouse staff and clerks on Mine, everyone else on All', () => {
    role.set(ROLE.WarehouseStaff);
    expect(spectator.service.initial()).toBe(LIST_SCOPE.Mine);
    role.set(ROLE.Clerk);
    expect(spectator.service.initial()).toBe(LIST_SCOPE.Mine);
    role.set(ROLE.Supervisor);
    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
  });

  it('remembers the choice per user, whatever the role default is', () => {
    role.set(ROLE.Clerk);

    spectator.service.remember(LIST_SCOPE.All);

    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
    expect(localStorage.getItem('ims.documents.scope.7')).toBe('all');
  });

  it('ignores a stored value it does not know and survives unavailable storage', () => {
    role.set(ROLE.Supervisor);
    localStorage.setItem('ims.documents.scope.7', 'everything');
    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);

    const getItem = jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const setItem = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
    expect(() => spectator.service.remember(LIST_SCOPE.Mine)).not.toThrow();
    getItem.mockRestore();
    setItem.mockRestore();
  });
});
