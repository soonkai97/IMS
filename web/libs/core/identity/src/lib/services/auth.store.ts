import { Injectable, inject } from '@angular/core';
import { AuthClient, AuthResult, ChangePasswordRequest, LoginRequest, UserInfo } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { ApiCommand } from '@ims/utils';

/** The refresh token travels in an HttpOnly cookie; the body of refresh and logout stays empty. */
const COOKIE_ONLY_BODY = { refreshToken: undefined } as const;

/** Which `GET /api/auth/me` this is: the follow-up of a sign-in raises its own toast on failure, a reload is handled inline. */
export interface MeRequest {
  readonly inline: boolean;
}

/**
 * The only place that calls the auth endpoints (every API call goes through a store action). It holds the wire behaviour:
 * cookie-only bodies and which answers a screen shows itself (`handledInline()`, so no toast). It keeps NO session state: that
 * stays in `AuthService`, which composes these actions (login then me, the single-flight refresh with its retry).
 *
 * The actions are used with `call(arg)`, not `run(arg)`: a failure must reach `AuthService` (it decides what a 401 or a 5xx means
 * for the session), so it is re-thrown after `$operation` records it. This store must not inject `AuthService` (the HTTP
 * interceptor injects `AuthService`; keeping the arrow one way avoids a cycle).
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly client = inject(AuthClient);

  /** The sign-in banner shows every answer (401 sentence, lockout, rate limit, server, network), so none raises a toast. */
  readonly login = new ApiCommand<LoginRequest, AuthResult>(credentials =>
    this.client.login(credentials, handledInline())
  );

  readonly me = new ApiCommand<MeRequest, UserInfo>(({ inline }) =>
    inline ? this.client.me(handledInline()) : this.client.me()
  );

  readonly refresh = new ApiCommand<void, AuthResult>(() => this.client.refresh(COOKIE_ONLY_BODY));

  readonly logout = new ApiCommand<void, void>(() => this.client.logout(COOKIE_ONLY_BODY));

  /** The screen shows every failure (a message at a field or a banner), so none raises a toast. */
  readonly changePassword = new ApiCommand<ChangePasswordRequest, void>(request =>
    this.client.changePassword(request, handledInline())
  );
}
