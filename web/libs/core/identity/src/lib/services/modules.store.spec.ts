import { signal } from '@angular/core';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { ModuleDto, ModulesClient, UserInfo } from '@ims/api';
import { RECHECK_QUIET_MS } from '../constants/identity.const';
import { AuthService } from './auth.service';
import { ModulesStore } from './modules.store';

function userWith(modules: string[]): UserInfo {
  return {
    id: '00000000-0000-4000-8000-000000000001',
    username: 'u',
    displayName: 'U',
    role: 'Admin',
    roleName: 'Administrator',
    permissions: [],
    seesAllWarehouses: true,
    warehouseIds: [],
    modules
  };
}

const moduleDto = (code: string, enabled: boolean): ModuleDto => ({
  code,
  name: code.toUpperCase(),
  description: `${code} description`,
  enabled,
  requires: []
});

describe('ModulesStore', () => {
  const user = signal<UserInfo | undefined>(undefined);
  let spectator: SpectatorService<ModulesStore>;
  const createService = createServiceFactory({
    service: ModulesStore,
    providers: [
      mockProvider(ModulesClient),
      { provide: AuthService, useValue: { $user: user.asReadonly() } }
    ]
  });
  const client = () => spectator.inject(ModulesClient);

  beforeEach(() => {
    jest.restoreAllMocks();
    user.set(userWith(['purchasing']));
    spectator = createService();
  });

  describe('isEnabled', () => {
    it('starts from UserInfo.modules and knows no module for a signed-out visitor', () => {
      expect(spectator.service.isEnabled('purchasing')).toBe(true);
      expect(spectator.service.isEnabled('batch')).toBe(false);

      user.set(undefined);
      expect(spectator.service.isEnabled('purchasing')).toBe(false);
    });

    it('follows a new UserInfo (the refresh every 5 minutes) and drops what a fresher list said before', () => {
      client().list.mockReturnValue(of([moduleDto('purchasing', false)]));
      spectator.service.list.load(undefined);
      expect(spectator.service.isEnabled('purchasing')).toBe(false);

      user.set(userWith(['purchasing']));

      expect(spectator.service.isEnabled('purchasing')).toBe(true);
    });

    it('takes the list from GET /api/modules as fresher than UserInfo', () => {
      client().list.mockReturnValue(of([moduleDto('purchasing', false), moduleDto('batch', true)]));

      spectator.service.list.load(undefined);

      expect(spectator.service.isEnabled('purchasing')).toBe(false);
      expect(spectator.service.isEnabled('batch')).toBe(true);
      expect(spectator.service.list.$data()).toHaveLength(2);
    });
  });

  describe('switchModule', () => {
    it('sends enabled and the reason, then reloads the list so the menu follows', async () => {
      client().switch.mockReturnValue(
        of({ ...moduleDto('purchasing', false), rolesRegainingGrants: 0 })
      );
      client().list.mockReturnValue(of([moduleDto('purchasing', false)]));

      const result = await firstValueFrom(
        spectator.service.switchModule.run({
          code: 'purchasing',
          enabled: false,
          reason: 'Not needed any more'
        })
      );

      expect(client().switch).toHaveBeenCalledWith(
        'purchasing',
        { enabled: false, reason: 'Not needed any more' },
        expect.anything()
      );
      expect(result.rolesRegainingGrants).toBe(0);
      expect(client().list).toHaveBeenCalledTimes(1);
      expect(spectator.service.isEnabled('purchasing')).toBe(false);
    });

    it('shows no toast for its failures (the screen does) and keeps the old state', async () => {
      client().switch.mockReturnValue(throwError(() => ({ status: 409 })));

      await firstValueFrom(
        spectator.service.switchModule.run({ code: 'purchasing', enabled: false, reason: 'abcde' }),
        { defaultValue: undefined }
      );

      const context = client().switch.mock.calls[0][2];
      expect(context).toBeDefined();
      expect(spectator.service.switchModule.$isFailed()).toBe(true);
      expect(spectator.service.isEnabled('purchasing')).toBe(true);
    });
  });

  describe('checkAfterNotFound (a 404 may be a module that was turned off)', () => {
    it('returns the modules that were on and are now off, and the menu already follows', async () => {
      client().list.mockReturnValue(of([moduleDto('purchasing', false)]));

      const turnedOff = await firstValueFrom(spectator.service.checkAfterNotFound());

      expect(turnedOff.map(module => module.code)).toEqual(['purchasing']);
      expect(spectator.service.isEnabled('purchasing')).toBe(false);
      expect(spectator.service.list.$data()).toHaveLength(1);
    });

    it('returns nothing when the 404 was something else', async () => {
      client().list.mockReturnValue(of([moduleDto('purchasing', true)]));

      expect(await firstValueFrom(spectator.service.checkAfterNotFound())).toEqual([]);
    });

    it('returns nothing, and does not fail, when the list cannot be read', async () => {
      client().list.mockReturnValue(throwError(() => ({ status: 500 })));

      expect(await firstValueFrom(spectator.service.checkAfterNotFound())).toEqual([]);
    });

    it('sends one request to callers that ask together', async () => {
      const answer = new Subject<ModuleDto[]>();
      client().list.mockReturnValue(answer);

      const first = firstValueFrom(spectator.service.checkAfterNotFound());
      const second = firstValueFrom(spectator.service.checkAfterNotFound());
      answer.next([moduleDto('purchasing', false)]);
      answer.complete();

      expect((await first).map(module => module.code)).toEqual(['purchasing']);
      expect((await second).map(module => module.code)).toEqual(['purchasing']);
      expect(client().list).toHaveBeenCalledTimes(1);
    });

    it('does not ask again for a few seconds after a check that found nothing (a scan miss is a 404 too)', async () => {
      const now = jest.spyOn(Date, 'now').mockReturnValue(1_000_000);
      client().list.mockReturnValue(of([moduleDto('purchasing', true)]));

      await firstValueFrom(spectator.service.checkAfterNotFound());
      now.mockReturnValue(1_000_000 + RECHECK_QUIET_MS - 1);
      expect(await firstValueFrom(spectator.service.checkAfterNotFound())).toEqual([]);
      expect(client().list).toHaveBeenCalledTimes(1);

      now.mockReturnValue(1_000_000 + RECHECK_QUIET_MS);
      await firstValueFrom(spectator.service.checkAfterNotFound());
      expect(client().list).toHaveBeenCalledTimes(2);
    });

    it('asks again at once after a check that did find a turned-off module', async () => {
      client().list.mockReturnValue(of([moduleDto('purchasing', false)]));
      await firstValueFrom(spectator.service.checkAfterNotFound());

      client().list.mockReturnValue(of([moduleDto('purchasing', false)]));
      await firstValueFrom(spectator.service.checkAfterNotFound());

      expect(client().list).toHaveBeenCalledTimes(2);
    });
  });
});
