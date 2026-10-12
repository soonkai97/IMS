import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  finalize,
  map,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError
} from 'rxjs';
import { AuthClient, AuthResult, LoginRequest, UserInfo } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { HTTP_STATUS } from '@ims/models';
import { ApiCommand } from '@ims/utils';
import {
  LOGIN_ROUTE,
  RETURN_URL_PARAM,
  SESSION_EXPIRED_REASON,
  SESSION_REASON_PARAM
} from '../constants/identity.const';
import { removeLegacyRefreshToken } from './legacy-refresh-token';

/** The refresh token travels in an HttpOnly cookie; the body of refresh and logout stays empty. */
const COOKIE_ONLY_BODY = { refreshToken: undefined } as const;

/**
 * Session owner: login, logout and token refresh with rotation (SEC-34).
 *  - The access token lives in memory only. The refresh token is an HttpOnly cookie that scripts cannot read, so the
 *    app never stores, reads or sends it; it just calls refresh and the browser attaches the cookie.
 *  - refresh() is single-flight: concurrent 401s share one call, because rotation invalidates the old refresh token.
 *  - When refreshing fails the session ends and the user is sent to the login page with a return URL.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authClient = inject(AuthClient);
  private readonly router = inject(Router);

  private accessToken: string | undefined;
  private refreshInFlight: Observable<AuthResult> | undefined;
  private readonly sessionEnded = new Subject<void>();

  private readonly $currentUser = signal<UserInfo | undefined>(undefined);

  readonly $user = this.$currentUser.asReadonly();
  readonly $isAuthenticated = computed(() => this.$currentUser() !== undefined);
  /** Emits when the session ends (logout or expiry): caches holding user data should be dropped. */
  readonly sessionEnded$ = this.sessionEnded.asObservable();

  readonly login = new ApiCommand<LoginRequest, UserInfo>(credentials =>
    // The sign-in page shows a rate-limit answer in its banner, so that one raises no toast.
    this.authClient.login(credentials, handledInline(HTTP_STATUS.TooManyRequests)).pipe(
      tap(result => this.acceptTokens(result)),
      switchMap(() => this.authClient.me()),
      tap(user => this.$currentUser.set(user))
    )
  );

  get token(): string | undefined {
    return this.accessToken;
  }

  /**
   * App start: one refresh call turns the cookie back into a session. Nothing in the page can tell whether a cookie
   * exists, so a 401 (or any failure) just means "signed out": no redirect, no notice.
   */
  restoreSession(): Observable<void> {
    removeLegacyRefreshToken();
    return this.refreshSession(false).pipe(
      map(() => undefined),
      catchError(() => of(undefined))
    );
  }

  /** New access token (the refresh cookie is rotated by the server). Concurrent callers share one request. */
  refresh(): Observable<string> {
    return this.refreshSession(true).pipe(map(result => result.accessToken));
  }

  logout(): Observable<void> {
    return this.authClient.logout(COOKIE_ONLY_BODY).pipe(
      catchError(() => EMPTY),
      finalize(() => this.endSession(false))
    );
  }

  /** Clears the session; `expired` adds the "session expired" notice and remembers where the user was. */
  endSession(expired: boolean): void {
    this.accessToken = undefined;
    this.$currentUser.set(undefined);
    this.refreshInFlight = undefined;
    this.sessionEnded.next();
    const queryParams = expired
      ? {
          [RETURN_URL_PARAM]: this.router.url,
          [SESSION_REASON_PARAM]: SESSION_EXPIRED_REASON
        }
      : {};
    void this.router.navigate([LOGIN_ROUTE], { queryParams });
  }

  private refreshSession(endSessionOnFailure: boolean): Observable<AuthResult> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }
    this.refreshInFlight = this.authClient.refresh(COOKIE_ONLY_BODY).pipe(
      tap(result => this.acceptTokens(result)),
      catchError((error: unknown) => {
        if (endSessionOnFailure) {
          this.endSession(true);
        }
        return throwError(() => error);
      }),
      finalize(() => {
        this.refreshInFlight = undefined;
      }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.refreshInFlight;
  }

  private acceptTokens(result: AuthResult): void {
    this.accessToken = result.accessToken;
    this.$currentUser.set(result.user);
  }
}
