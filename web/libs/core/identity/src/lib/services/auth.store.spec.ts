import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { Observable, firstValueFrom, of, throwError } from 'rxjs';
import { AuthClient, AuthResult, UserInfo } from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { API_STATUS, HTTP_STATUS } from '@ims/models';
import { AuthStore } from './auth.store';

const USER = { id: 'u1', username: 'staff' } as UserInfo;
const RESULT = { accessToken: 'a1', user: USER } as AuthResult;

/** The session's wire behaviour (cookie-only bodies, which answers a screen shows itself) now lives in this store. */
describe('AuthStore', () => {
  let spectator: SpectatorService<AuthStore>;
  const createService = createServiceFactory({
    service: AuthStore,
    providers: [mockProvider(AuthClient)]
  });

  beforeEach(() => (spectator = createService()));

  const client = () => spectator.inject(AuthClient);
  const handled = (context: unknown, status: number) =>
    isHandledInline(context as HttpContext, status);

  it('login sends the credentials and marks every answer as shown by the sign-in banner (no toast)', async () => {
    client().login.mockReturnValue(of(RESULT));

    const result = await firstValueFrom(
      spectator.service.login.call({ username: 'staff', password: 'x' })
    );

    expect(result).toBe(RESULT);
    const [body, context] = client().login.mock.calls[0];
    expect(body).toEqual({ username: 'staff', password: 'x' });
    expect(handled(context, HTTP_STATUS.Unauthorized)).toBe(true);
    expect(handled(context, 500)).toBe(true);
  });

  it('refresh sends an empty body: the refresh token is an HttpOnly cookie the app never touches', async () => {
    client().refresh.mockReturnValue(of(RESULT));

    await firstValueFrom(spectator.service.refresh.call());

    expect(client().refresh).toHaveBeenCalledTimes(1);
    expect(client().refresh).toHaveBeenCalledWith({ refreshToken: undefined });
  });

  it('logout sends an empty body and no handled-inline marker', async () => {
    client().logout.mockReturnValue(of(undefined));

    await firstValueFrom(spectator.service.logout.call());

    expect(client().logout).toHaveBeenCalledWith({ refreshToken: undefined });
  });

  it('me at sign-in sends no context; me for a reload is marked handled-inline', async () => {
    client().me.mockReturnValue(of(USER));

    await firstValueFrom(spectator.service.me.call({ inline: false }));
    await firstValueFrom(spectator.service.me.call({ inline: true }));

    expect(client().me.mock.calls[0]).toEqual([]);
    expect(handled(client().me.mock.calls[1][0], 500)).toBe(true);
  });

  it('change password sends both passwords once and marks every answer as shown by the screen', async () => {
    client().changePassword.mockReturnValue(of(undefined));

    await firstValueFrom(
      spectator.service.changePassword.call({ currentPassword: 'old', newPassword: 'new' })
    );

    const [body, context] = client().changePassword.mock.calls[0];
    expect(body).toEqual({ currentPassword: 'old', newPassword: 'new' });
    expect(handled(context, HTTP_STATUS.Forbidden)).toBe(true);
  });

  it('every action records its failure and hands it to the caller untouched (the session decides what it means)', async () => {
    const failure = { status: 401 };
    for (const call of [
      client().login,
      client().refresh,
      client().logout,
      client().me,
      client().changePassword
    ]) {
      call.mockReturnValue(throwError(() => failure));
    }
    const { service } = spectator;
    const actions: {
      command: { $operation(): { status: string } };
      run: () => Observable<unknown>;
    }[] = [
      { command: service.login, run: () => service.login.call({ username: 'u', password: 'p' }) },
      { command: service.refresh, run: () => service.refresh.call() },
      { command: service.logout, run: () => service.logout.call() },
      { command: service.me, run: () => service.me.call({ inline: false }) },
      {
        command: service.changePassword,
        run: () => service.changePassword.call({ currentPassword: 'a', newPassword: 'b' })
      }
    ];

    for (const { command, run } of actions) {
      await expect(firstValueFrom(run())).rejects.toBe(failure);
      expect(command.$operation().status).toBe(API_STATUS.Failure);
    }
  });
});
