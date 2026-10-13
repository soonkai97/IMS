import { signal } from '@angular/core';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { PermissionProfile, PermissionService } from '@ims/core/identity';
import { LIST_SCOPE, ListScopePreference } from './list-scope.preference';

const USER_ID = '00000000-0000-4000-8000-000000000007';

describe('ListScopePreference', () => {
  const profile = signal<Pick<PermissionProfile, 'mineFirst'>>({ mineFirst: true });
  let spectator: SpectatorService<ListScopePreference>;
  const createService = createServiceFactory({
    service: ListScopePreference,
    providers: [mockProvider(PermissionService, { $profile: profile, $userId: signal(USER_ID) })]
  });

  beforeEach(() => {
    localStorage.clear();
    spectator = createService();
  });

  it('starts people who create but do not review on Mine, everyone else on All', () => {
    profile.set({ mineFirst: true });
    expect(spectator.service.initial()).toBe(LIST_SCOPE.Mine);
    profile.set({ mineFirst: false });
    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
  });

  it('remembers the choice per user, whatever the default is', () => {
    profile.set({ mineFirst: true });

    spectator.service.remember(LIST_SCOPE.All);

    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
    expect(localStorage.getItem(`ims.documents.scope.${USER_ID}`)).toBe('all');
  });

  it('does not read a choice saved under the old numeric user id (ids are GUIDs now)', () => {
    profile.set({ mineFirst: false });
    localStorage.setItem('ims.documents.scope.7', 'mine');

    expect(spectator.service.initial()).toBe(LIST_SCOPE.All);
  });

  it('ignores a stored value it does not know and survives unavailable storage', () => {
    profile.set({ mineFirst: false });
    localStorage.setItem(`ims.documents.scope.${USER_ID}`, 'everything');
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
