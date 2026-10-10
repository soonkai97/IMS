import { Router } from '@angular/router';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Subject, firstValueFrom, of, throwError } from 'rxjs';
import { AuthClient, AuthResult, UserInfo } from '@ims/api';
import { REFRESH_TOKEN_STORAGE_KEY } from '../constants/identity.const';
import { AuthService } from './auth.service';

const USER: UserInfo = {
  id: 7,
  username: 'staff',
  displayName: 'Staff',
  role: 'WarehouseStaff',
  permissions: ['stock.read'],
  warehouseIds: [1]
};

function authResult(access: string, refresh: string): AuthResult {
  return {
    accessToken: access,
    refreshToken: refresh,
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
  });

  it('login keeps the access token in memory, stores the refresh token and loads the user', async () => {
    client().login.mockReturnValue(of(authResult('a1', 'r1')));
    client().me.mockReturnValue(of(USER));

    await firstValueFrom(spectator.service.login.run({ username: 'staff', password: 'x' }));

    expect(spectator.service.token).toBe('a1');
    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBe('r1');
    expect(spectator.service.$isAuthenticated()).toBe(true);
  });

  it('shares ONE refresh call between concurrent callers and rotates the stored token', async () => {
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, 'r1');
    const response = new Subject<AuthResult>();
    client().refresh.mockReturnValue(response);

    const first = firstValueFrom(spectator.service.refresh());
    const second = firstValueFrom(spectator.service.refresh());
    response.next(authResult('a2', 'r2'));
    response.complete();

    await expect(Promise.all([first, second])).resolves.toEqual(['a2', 'a2']);
    expect(client().refresh).toHaveBeenCalledTimes(1);
    expect(client().refresh).toHaveBeenCalledWith({ refreshToken: 'r1' });
    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBe('r2');
  });

  it('ends the session and redirects to login with the return URL when refresh fails', async () => {
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, 'r1');
    client().refresh.mockReturnValue(throwError(() => new Error('revoked')));

    await expect(firstValueFrom(spectator.service.refresh())).rejects.toThrow('revoked');

    expect(localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
    expect(spectator.service.$isAuthenticated()).toBe(false);
    expect(router().navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/stock', reason: 'expired' }
    });
  });

  it('refresh without a stored refresh token fails and signs out', async () => {
    await expect(firstValueFrom(spectator.service.refresh())).rejects.toThrow();

    expect(client().refresh).not.toHaveBeenCalled();
    expect(router().navigate).toHaveBeenCalled();
  });

  it('restoreSession turns a stored refresh token into a session', async () => {
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, 'r1');
    client().refresh.mockReturnValue(of(authResult('a2', 'r2')));
    client().me.mockReturnValue(of(USER));

    await firstValueFrom(spectator.service.restoreSession());

    expect(spectator.service.$user()?.username).toBe('staff');
  });

  it('restoreSession without a stored token stays signed out and calls nothing', async () => {
    await firstValueFrom(spectator.service.restoreSession());

    expect(client().refresh).not.toHaveBeenCalled();
    expect(spectator.service.$isAuthenticated()).toBe(false);
  });
});
