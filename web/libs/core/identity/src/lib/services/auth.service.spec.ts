import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { AuthClient, AuthResult, UserInfo } from '@ims/api';
import { LEGACY_REFRESH_TOKEN_STORAGE_KEY, REFRESH_RETRY } from '../constants/identity.const';
import { AuthService } from './auth.service';
import { SessionDraftService } from './session-draft.service';

const USER: UserInfo = {
  id: '00000000-0000-4000-8000-000000000007',
  username: 'staff',
  displayName: 'Staff',
  role: 'WarehouseStaff',
  roleName: 'Warehouse staff',
  permissions: ['stock.read'],
  seesAllWarehouses: false,
  warehouseIds: ['00000000-0000-4000-8000-000000000001']
};

/** The cookie flow: the body never carries a refresh token. */
function authResult(access: string): AuthResult {
  return {
    accessToken: access,
    refreshToken: undefined,
    expiresAt: '2030-01-01T00:00:00Z',
    user: USER
  };
}

describe('AuthService', () => {
  let spectator: SpectatorService<AuthService>;
  const createService = createServiceFactory({
    service: AuthService,
    providers: [
      mockProvider(AuthClient),
      mockProvider(Router, { url: '/stock', navigate: jest.fn().mockResolvedValue(true) })
    ]
  });

  const client = () => spectator.inject(AuthClient);
  const router = () => spectator.inject(Router);

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spectator = createService();
    router().navigate.mockClear();
  });

  it('login keeps the access token in memory and stores nothing in localStorage', async () => {
    client().login.mockReturnValue(of(authResult('a1')));
    client().me.mockReturnValue(of(USER));

    await firstValueFrom(spectator.service.login.run({ username: 'staff', password: 'x' }));

    expect(spectator.service.token).toBe('a1');
    expect(spectator.service.$isAuthenticated()).toBe(true);
    expect(localStorage.length).toBe(0);
  });

  it('shares ONE refresh call between concurrent callers and sends no token in the body', async () => {
    const response = new Subject<AuthResult>();
    client().refresh.mockReturnValue(response);

    const first = firstValueFrom(spectator.service.refresh());
    const second = firstValueFrom(spectator.service.refresh());
    response.next(authResult('a2'));
    response.complete();

    await expect(Promise.all([first, second])).resolves.toEqual(['a2', 'a2']);
    expect(client().refresh).toHaveBeenCalledTimes(1);
    expect(client().refresh).toHaveBeenCalledWith({ refreshToken: undefined });
    expect(spectator.service.token).toBe('a2');
    expect(localStorage.length).toBe(0);
  });

  it('ends the session and redirects to login with the return URL when refresh fails', async () => {
    client().refresh.mockReturnValue(throwError(() => ({ status: 401 })));

    await expect(firstValueFrom(spectator.service.refresh())).rejects.toEqual({ status: 401 });

    expect(spectator.service.token).toBeUndefined();
    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/stock', reason: 'expired' }
    });
  });

  describe('a session that expires while the user has unsent work (QA defect 1)', () => {
    const OTHER_USER = '00000000-0000-4000-8000-000000000099';

    async function signInAs(user = USER): Promise<void> {
      client().refresh.mockReturnValueOnce(of({ ...authResult('a1'), user }));
      await firstValueFrom(spectator.service.restoreSession());
    }

    async function expire(): Promise<void> {
      client().refresh.mockReturnValue(throwError(() => ({ status: 401 })));
      await expect(firstValueFrom(spectator.service.refresh())).rejects.toBeDefined();
    }

    it('tells screens before the user is cleared, so they can keep their work', async () => {
      await signInAs();
      let userSeen: string | undefined;
      spectator.service.sessionExpiring$.subscribe(
        () => (userSeen = spectator.service.$user()?.id)
      );

      await expire();

      expect(userSeen).toBe(USER.id);
      expect(spectator.service.$user()).toBeUndefined();
    });

    it('sends the user to sign-in with the return URL and the plain "expired" reason when nothing was kept', async () => {
      await signInAs();

      await expire();

      expect(spectator.service.$sessionExpired()).toBe(true);
      expect(router().navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/stock', reason: 'expired' }
      });
    });

    it('uses the "expired-kept" reason when a screen kept a draft while the session ended', async () => {
      await signInAs();
      const keeper = TestBed.inject(SessionDraftService);
      spectator.service.sessionExpiring$.subscribe(() =>
        keeper.save('document.Receipt.new', { remark: 'x' })
      );

      await expire();

      expect(router().navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/stock', reason: 'expired-kept' }
      });
    });

    it('does not say "kept" for an older draft that was already in storage when nothing was saved now', async () => {
      await signInAs();
      TestBed.inject(SessionDraftService).save('document.Receipt.new', { remark: 'old' });

      await expire();

      expect(router().navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/stock', reason: 'expired' }
      });
    });

    it('does not say "kept" when storage refused the draft', async () => {
      await signInAs();
      const keeper = TestBed.inject(SessionDraftService);
      spectator.service.sessionExpiring$.subscribe(() => keeper.save('k', { remark: 'x' }));
      jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('QuotaExceededError');
      });

      await expire();
      jest.restoreAllMocks();

      expect(router().navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { returnUrl: '/stock', reason: 'expired' }
      });
    });

    it('keeps the draft through the expiry and gives it back once to the same user after signing in again', async () => {
      await signInAs();
      const keeper = TestBed.inject(SessionDraftService);
      spectator.service.sessionExpiring$.subscribe(() => keeper.save('k', { remark: 'x' }));
      await expire();

      await signInAs();

      expect(keeper.take('k')).toEqual({ remark: 'x' });
      expect(keeper.take('k')).toBeUndefined();
      expect(spectator.service.$sessionExpired()).toBe(false);
    });

    it('does not give the draft to a different user who signs in next, and removes it', async () => {
      await signInAs();
      const keeper = TestBed.inject(SessionDraftService);
      spectator.service.sessionExpiring$.subscribe(() => keeper.save('k', { remark: 'x' }));
      await expire();

      await signInAs({ ...USER, id: OTHER_USER });

      expect(keeper.take('k')).toBeUndefined();
      expect(sessionStorage.length).toBe(0);
    });

    it('removes every kept draft at an explicit sign-out and does not mark the session expired', async () => {
      await signInAs();
      TestBed.inject(SessionDraftService).save('k', { remark: 'x' });
      client().logout.mockReturnValue(of(undefined));

      await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });

      expect(sessionStorage.length).toBe(0);
      expect(spectator.service.$sessionExpired()).toBe(false);
      expect(router().navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
    });

    it('survives storage that refuses writes', async () => {
      await signInAs();
      jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('QuotaExceededError');
      });

      expect(TestBed.inject(SessionDraftService).save('k', {})).toBe(false);
      jest.restoreAllMocks();
    });
  });

  describe('a refresh that fails for another reason than a refused cookie', () => {
    const rateLimited = (seconds?: number) => ({
      isApiError: true,
      status: 429,
      extensions: seconds === undefined ? {} : { retryAfterSeconds: seconds }
    });

    beforeEach(() => {
      jest.useFakeTimers();
      jest.spyOn(Math, 'random').mockReturnValue(0.5); // no jitter
    });
    afterEach(() => {
      jest.useRealTimers();
      jest.restoreAllMocks();
    });

    async function signIn(): Promise<void> {
      client().refresh.mockReturnValueOnce(of(authResult('a1')));
      await firstValueFrom(spectator.service.restoreSession());
      client().refresh.mockReset();
    }

    it('waits for the server Retry-After on a 429, then succeeds without ending the session', async () => {
      await signIn();
      client()
        .refresh.mockReturnValueOnce(throwError(() => rateLimited(3)))
        .mockReturnValueOnce(of(authResult('a2')));

      const done = firstValueFrom(spectator.service.refresh());
      await jest.advanceTimersByTimeAsync(2999);
      expect(client().refresh).toHaveBeenCalledTimes(1);
      await jest.advanceTimersByTimeAsync(1);

      await expect(done).resolves.toBe('a2');
      expect(client().refresh).toHaveBeenCalledTimes(2);
      expect(spectator.service.$isAuthenticated()).toBe(true);
      expect(router().navigate).not.toHaveBeenCalled();
    });

    it('adds jitter to the wait', async () => {
      await signIn();
      jest.spyOn(Math, 'random').mockReturnValue(1); // +25%
      client()
        .refresh.mockReturnValueOnce(throwError(() => rateLimited(4)))
        .mockReturnValueOnce(of(authResult('a2')));

      const done = firstValueFrom(spectator.service.refresh());
      await jest.advanceTimersByTimeAsync(4999);
      expect(client().refresh).toHaveBeenCalledTimes(1);
      await jest.advanceTimersByTimeAsync(1);

      await expect(done).resolves.toBe('a2');
    });

    it('stops retrying when the next wait would pass the total budget', async () => {
      await signIn();
      client().refresh.mockReturnValue(throwError(() => rateLimited(60)));

      const done = firstValueFrom(spectator.service.refresh()).catch((error: unknown) => error);
      await jest.advanceTimersByTimeAsync(REFRESH_RETRY.budgetMs);

      expect(await done).toMatchObject({ status: 429 });
      expect(client().refresh).toHaveBeenCalledTimes(2); // 0 s and 10 s; a third at 20 s would pass 15 s
      expect(spectator.service.$isAuthenticated()).toBe(true);
    });

    it.each([408, 500, 502, 503])('retries a %i', async status => {
      await signIn();
      client()
        .refresh.mockReturnValueOnce(
          throwError(() => ({ isApiError: true, status, extensions: {} }))
        )
        .mockReturnValueOnce(of(authResult('a2')));

      const done = firstValueFrom(spectator.service.refresh());
      await jest.advanceTimersByTimeAsync(1000);

      await expect(done).resolves.toBe('a2');
    });

    it.each([400, 404, 405, 413, 422])(
      'does not retry a %i: the answer is final and the session ends',
      async status => {
        await signIn();
        client().refresh.mockReturnValue(
          throwError(() => ({ isApiError: true, status, extensions: {} }))
        );

        await expect(firstValueFrom(spectator.service.refresh())).rejects.toMatchObject({ status });

        expect(client().refresh).toHaveBeenCalledTimes(1);
        expect(spectator.service.$isAuthenticated()).toBe(false);
        expect(router().navigate).toHaveBeenCalledWith(['/login'], expect.anything());
      }
    );

    it('logout during a pending back-off cancels the retry: no sign-in again, no "session expired"', async () => {
      await signIn();
      client().refresh.mockReturnValue(throwError(() => rateLimited(3)));
      client().logout.mockReturnValue(of(undefined));
      const pending = firstValueFrom(spectator.service.refresh()).catch((error: unknown) => error);
      await jest.advanceTimersByTimeAsync(1000);

      await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });
      client().refresh.mockReturnValue(of(authResult('late')));
      await jest.advanceTimersByTimeAsync(20_000);

      expect(await pending).toBeInstanceOf(Error);
      expect(client().refresh).toHaveBeenCalledTimes(1);
      expect(spectator.service.token).toBeUndefined();
      expect(spectator.service.$isAuthenticated()).toBe(false);
      expect(router().navigate).toHaveBeenCalledTimes(1);
      expect(router().navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
    });

    it('a refresh that succeeds after logout is ignored', async () => {
      await signIn();
      const answer = new Subject<AuthResult>();
      client().refresh.mockReturnValue(answer);
      client().logout.mockReturnValue(of(undefined));
      const pending = firstValueFrom(spectator.service.refresh()).catch((error: unknown) => error);

      await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });
      answer.next(authResult('late'));
      answer.complete();

      expect(await pending).toBeInstanceOf(Error);
      expect(spectator.service.token).toBeUndefined();
      expect(spectator.service.$isAuthenticated()).toBe(false);
    });

    it('a new sign-in cancels a refresh that is waiting to retry', async () => {
      await signIn();
      client().refresh.mockReturnValue(throwError(() => rateLimited(3)));
      const pending = firstValueFrom(spectator.service.refresh()).catch((error: unknown) => error);
      client().login.mockReturnValue(of(authResult('fresh')));
      client().me.mockReturnValue(of(USER));

      await firstValueFrom(spectator.service.login.run({ username: 'staff', password: 'x' }));
      await jest.advanceTimersByTimeAsync(20_000);

      expect(await pending).toBeInstanceOf(Error);
      expect(client().refresh).toHaveBeenCalledTimes(1);
      expect(spectator.service.token).toBe('fresh');
    });

    it('caps a long Retry-After', async () => {
      await signIn();
      client()
        .refresh.mockReturnValueOnce(throwError(() => rateLimited(60)))
        .mockReturnValueOnce(of(authResult('a2')));

      const done = firstValueFrom(spectator.service.refresh());
      await jest.advanceTimersByTimeAsync(REFRESH_RETRY.maxDelayMs);

      await expect(done).resolves.toBe('a2');
    });

    it('backs off on a network error or a 5xx, and keeps the user signed in when it keeps failing', async () => {
      await signIn();
      client().refresh.mockReturnValue(
        throwError(() => ({ isApiError: true, status: 0, extensions: {} }))
      );

      const done = firstValueFrom(spectator.service.refresh()).catch((error: unknown) => error);
      await jest.advanceTimersByTimeAsync(10_000);

      expect(await done).toMatchObject({ status: 0 });
      expect(client().refresh).toHaveBeenCalledTimes(1 + REFRESH_RETRY.attempts);
      expect(spectator.service.token).toBe('a1');
      expect(spectator.service.$isAuthenticated()).toBe(true);
      expect(router().navigate).not.toHaveBeenCalled();
    });

    it('does not retry a refused cookie (403) and ends the session', async () => {
      await signIn();
      client().refresh.mockReturnValue(throwError(() => ({ status: 403 })));

      await expect(firstValueFrom(spectator.service.refresh())).rejects.toEqual({ status: 403 });

      expect(client().refresh).toHaveBeenCalledTimes(1);
      expect(spectator.service.$isAuthenticated()).toBe(false);
      expect(router().navigate).toHaveBeenCalled();
    });

    it('restoreSession does not wait on a 429: the app start goes on signed out', async () => {
      client().refresh.mockReturnValue(throwError(() => rateLimited(30)));

      await firstValueFrom(spectator.service.restoreSession());

      expect(client().refresh).toHaveBeenCalledTimes(1);
      expect(spectator.service.$isAuthenticated()).toBe(false);
    });
  });

  it('restoreSession is one refresh call: the cookie becomes a session', async () => {
    client().refresh.mockReturnValue(of(authResult('a2')));

    await firstValueFrom(spectator.service.restoreSession());

    expect(client().refresh).toHaveBeenCalledTimes(1);
    expect(client().me).not.toHaveBeenCalled();
    expect(spectator.service.token).toBe('a2');
    expect(spectator.service.$user()?.username).toBe('staff');
  });

  it('restoreSession treats a 401 as signed out, without a redirect or notice', async () => {
    client().refresh.mockReturnValue(throwError(() => ({ status: 401 })));

    await firstValueFrom(spectator.service.restoreSession());

    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).not.toHaveBeenCalled();
  });

  it('restoreSession deletes a refresh token left in localStorage by an older release', async () => {
    localStorage.setItem(LEGACY_REFRESH_TOKEN_STORAGE_KEY, 'old-secret');
    client().refresh.mockReturnValue(throwError(() => ({ status: 401 })));

    await firstValueFrom(spectator.service.restoreSession());

    expect(localStorage.getItem(LEGACY_REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('logout asks the server to revoke and clear the cookie, then ends the session', async () => {
    client().refresh.mockReturnValue(of(authResult('a2')));
    await firstValueFrom(spectator.service.restoreSession());
    client().logout.mockReturnValue(of(undefined));

    await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });

    expect(client().logout).toHaveBeenCalledWith({ refreshToken: undefined });
    expect(spectator.service.token).toBeUndefined();
    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
  });

  describe('changePassword', () => {
    const request = { currentPassword: 'old-password-1', newPassword: 'new-password-123' };

    async function signedIn(): Promise<void> {
      client().refresh.mockReturnValue(of(authResult('a2')));
      await firstValueFrom(spectator.service.restoreSession());
      router().navigate.mockClear();
    }

    it('sends both passwords once, then ends the session and opens sign-in with the "password-changed" notice', async () => {
      await signedIn();
      client().changePassword.mockReturnValue(of(undefined));

      await firstValueFrom(spectator.service.changePassword.run(request), {
        defaultValue: undefined
      });

      expect(client().changePassword).toHaveBeenCalledTimes(1);
      expect(client().changePassword.mock.calls[0][0]).toEqual(request);
      expect(spectator.service.token).toBeUndefined();
      expect(spectator.service.$isAuthenticated()).toBe(false);
      expect(spectator.service.$sessionExpired()).toBe(false);
      expect(router().navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { reason: 'password-changed' }
      });
    });

    it('keeps the session and the screen when the server refuses (a wrong current password)', async () => {
      await signedIn();
      const refusal = { isApiError: true, kind: 'validation', status: 400 };
      client().changePassword.mockReturnValue(throwError(() => refusal));

      await firstValueFrom(spectator.service.changePassword.run(request), {
        defaultValue: undefined
      });

      expect(spectator.service.$isAuthenticated()).toBe(true);
      expect(spectator.service.token).toBe('a2');
      expect(router().navigate).not.toHaveBeenCalled();
      expect(spectator.service.changePassword.$operation().error).toBe(refusal);
    });

    it('stores no password anywhere', async () => {
      await signedIn();
      client().changePassword.mockReturnValue(of(undefined));

      await firstValueFrom(spectator.service.changePassword.run(request), {
        defaultValue: undefined
      });

      expect(JSON.stringify({ ...localStorage })).not.toContain(request.newPassword);
      expect(JSON.stringify({ ...sessionStorage })).not.toContain(request.newPassword);
    });
  });

  it('logout still signs out locally when the server cannot be reached', async () => {
    client().logout.mockReturnValue(throwError(() => new Error('offline')));

    await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });

    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalled();
  });
});
