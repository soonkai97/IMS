import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  defer,
  finalize,
  map,
  of,
  retry,
  shareReplay,
  switchMap,
  tap,
  throwError,
  timer
} from 'rxjs';
import { AuthClient, AuthResult, LoginRequest, UserInfo } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { HTTP_STATUS, isApiError } from '@ims/models';
import { ApiCommand } from '@ims/utils';
import {
  LOGIN_ROUTE,
  REFRESH_RETRY,
  RETURN_URL_PARAM,
  SESSION_EXPIRED_REASON,
  SESSION_REASON_PARAM
} from '../constants/identity.const';
import { removeLegacyRefreshToken } from './legacy-refresh-token';

/** The refresh token travels in an HttpOnly cookie; the body of refresh and logout stays empty. */
const COOKIE_ONLY_BODY = { refreshToken: undefined } as const;

/** Only these answers say the cookie is no good (expired, revoked, refused); a 429, a gateway error or no network says nothing about it. */
function refusesSession(error: unknown): boolean {
  const status = isApiError(error) ? error.status : (error as { status?: number } | null)?.status;
  return status === HTTP_STATUS.Unauthorized || status === HTTP_STATUS.Forbidden;
}

/** The server's Retry-After (the rate limiter also puts it in the body as retryAfterSeconds), else a doubling back-off; capped. */
function retryDelayMs(error: unknown, attempt: number): number {
  const seconds = isApiError(error) ? error.extensions['retryAfterSeconds'] : undefined;
  const wanted =
    typeof seconds === 'number' && seconds > 0
      ? seconds * 1000
      : REFRESH_RETRY.baseDelayMs * 2 ** (attempt - 1);
  return Math.min(wanted, REFRESH_RETRY.maxDelayMs);
}

/**
 * Session owner: login, logout and token refresh with rotation (SEC-34).
 *  - The access token lives in memory only. The refresh token is an HttpOnly cookie that scripts cannot read, so the
 *    app never stores, reads or sends it; it just calls refresh and the browser attaches the cookie.
 *  - refresh() is single-flight: concurrent 401s share one call, because rotation invalidates the old refresh token.
 *  - When the server refuses the refresh (401, 403) the session ends and the user is sent to the login page with a return URL.
 *    Any other failure (429, 5xx, no network) is retried a little later and, if it persists, leaves the user signed in.
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
    // The sign-in banner shows every answer (401 sentence, lockout, rate limit, server, network), so none raises a toast.
    this.authClient.login(credentials, handledInline()).pipe(
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
    this.refreshInFlight = defer(() => this.authClient.refresh(COOKIE_ONLY_BODY)).pipe(
      // App start (endSessionOnFailure false) never waits: an offline start must reach the login page at once.
      retry({
        count: endSessionOnFailure ? REFRESH_RETRY.attempts : 0,
        delay: (error, attempt) =>
          refusesSession(error) ? throwError(() => error) : timer(retryDelayMs(error, attempt))
      }),
      tap(result => this.acceptTokens(result)),
      catchError((error: unknown) => {
        if (endSessionOnFailure && refusesSession(error)) {
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
