import { Router } from '@angular/router';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { AuthClient, AuthResult, UserInfo } from '@ims/api';
import { LEGACY_REFRESH_TOKEN_STORAGE_KEY } from '../constants/identity.const';
import { AuthService } from './auth.service';

const USER: UserInfo = {
  id: 7,
  username: 'staff',
  displayName: 'Staff',
  role: 'WarehouseStaff',
  permissions: ['stock.read'],
  warehouseIds: [1]
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
    client().refresh.mockReturnValue(throwError(() => new Error('revoked')));

    await expect(firstValueFrom(spectator.service.refresh())).rejects.toThrow('revoked');

    expect(spectator.service.token).toBeUndefined();
    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/stock', reason: 'expired' }
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

  it('logout still signs out locally when the server cannot be reached', async () => {
    client().logout.mockReturnValue(throwError(() => new Error('offline')));

    await firstValueFrom(spectator.service.logout(), { defaultValue: undefined });

    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalled();
  });
});
