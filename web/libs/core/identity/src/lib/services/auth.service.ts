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
import { ApiCommand } from '@ims/utils';
import {
  LOGIN_ROUTE,
  RETURN_URL_PARAM,
  SESSION_EXPIRED_REASON,
  SESSION_REASON_PARAM
} from '../constants/identity.const';
import { TokenStorage } from './token-storage';

const NO_REFRESH_TOKEN = 'No refresh token';

/**
 * Session owner: login, logout and token refresh with rotation.
 *  - The access token lives in memory; the refresh token is stored by TokenStorage.
 *  - refresh() is single-flight: concurrent 401s share one call, because rotation invalidates the old refresh token.
 *  - When refreshing fails the session ends and the user is sent to the login page with a return URL.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authClient = inject(AuthClient);
  private readonly router = inject(Router);
  private readonly storage = inject(TokenStorage);

  private accessToken: string | undefined;
  private refreshInFlight: Observable<string> | undefined;
  private readonly sessionEnded = new Subject<void>();

  private readonly $currentUser = signal<UserInfo | undefined>(undefined);

  readonly $user = this.$currentUser.asReadonly();
  readonly $isAuthenticated = computed(() => this.$currentUser() !== undefined);
  /** Emits when the session ends (logout or expiry): caches holding user data should be dropped. */
  readonly sessionEnded$ = this.sessionEnded.asObservable();

  readonly login = new ApiCommand<LoginRequest, UserInfo>(credentials =>
    this.authClient.login(credentials).pipe(
      tap(result => this.acceptTokens(result)),
      switchMap(() => this.authClient.me()),
      tap(user => this.$currentUser.set(user))
    )
  );

  get token(): string | undefined {
    return this.accessToken;
  }

  /** App start: turn a stored refresh token back into a session; any failure just means "logged out". */
  restoreSession(): Observable<void> {
    if (!this.storage.readRefreshToken()) {
      return of(undefined);
    }
    return this.refresh().pipe(
      switchMap(() => this.authClient.me()),
      tap(user => this.$currentUser.set(user)),
      map(() => undefined),
      catchError(() => of(undefined))
    );
  }

  /** New access token (and rotated refresh token). Concurrent callers share one request. */
  refresh(): Observable<string> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }
    const refreshToken = this.storage.readRefreshToken();
    if (!refreshToken) {
      this.endSession(true);
      return throwError(() => new Error(NO_REFRESH_TOKEN));
    }
    this.refreshInFlight = this.authClient.refresh({ refreshToken }).pipe(
      tap(result => this.acceptTokens(result)),
      map(result => result.accessToken),
      catchError((error: unknown) => {
        this.endSession(true);
        return throwError(() => error);
      }),
      finalize(() => {
        this.refreshInFlight = undefined;
      }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.refreshInFlight;
  }

  logout(): Observable<void> {
    const refreshToken = this.storage.readRefreshToken();
    const revoke$ = refreshToken ? this.authClient.logout({ refreshToken }) : of(undefined);
    return revoke$.pipe(
      catchError(() => EMPTY),
      finalize(() => this.endSession(false))
    );
  }

  /** Clears the session; `expired` adds the "session expired" notice and remembers where the user was. */
  endSession(expired: boolean): void {
    this.accessToken = undefined;
    this.storage.clear();
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

  private acceptTokens(result: AuthResult): void {
    this.accessToken = result.accessToken;
    this.storage.writeRefreshToken(result.refreshToken);
    this.$currentUser.set(result.user);
  }
}
