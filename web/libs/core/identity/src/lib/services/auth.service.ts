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
  takeUntil,
  tap,
  throwError,
  throwIfEmpty,
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
  SESSION_EXPIRED_KEPT_REASON,
  SESSION_EXPIRED_REASON,
  SESSION_REASON_PARAM
} from '../constants/identity.const';
import { removeLegacyRefreshToken } from './legacy-refresh-token';
import { clearDrafts } from './session-draft-storage';

/** The refresh token travels in an HttpOnly cookie; the body of refresh and logout stays empty. */
const COOKIE_ONLY_BODY = { refreshToken: undefined } as const;

const REQUEST_TIMEOUT_STATUS = 408;
const FIRST_SERVER_ERROR_STATUS = 500;
const NETWORK_STATUS = 0;
const SESSION_CHANGED_MESSAGE = 'The session changed while the token was being refreshed.';

/**
 * A failure that says nothing about the cookie, so the session stays: no network (0), 408, 429 and 5xx. Everything else (401,
 * 403, but also 400, 404, 405, 413, 422 or an error without a status) is a definitive "no" and ends the session.
 */
function isTransient(error: unknown): boolean {
  const status = isApiError(error) ? error.status : (error as { status?: number } | null)?.status;
  return (
    status === NETWORK_STATUS ||
    status === HTTP_STATUS.TooManyRequests ||
    status === REQUEST_TIMEOUT_STATUS ||
    (status !== undefined && status >= FIRST_SERVER_ERROR_STATUS)
  );
}

/**
 * The server's Retry-After (the rate limiter also puts it in the body as retryAfterSeconds), else a doubling back-off; capped,
 * with +-25% jitter so the users who were limited together do not come back in the same instant. The limiter reports the whole
 * window (60 s), not the time left, so the cap usually makes the retry land inside the same window; that is accepted: a limited
 * refresh is rare now (300 a minute), and the retry costs one request.
 */
function retryDelayMs(error: unknown, attempt: number): number {
  const seconds = isApiError(error) ? error.extensions['retryAfterSeconds'] : undefined;
  const wanted =
    typeof seconds === 'number' && seconds > 0
      ? seconds * 1000
      : REFRESH_RETRY.baseDelayMs * 2 ** (attempt - 1);
  const jitter = 1 + (Math.random() * 2 - 1) * REFRESH_RETRY.jitter;
  return Math.min(wanted * jitter, REFRESH_RETRY.maxDelayMs);
}

/**
 * Session owner: login, logout and token refresh with rotation (SEC-34).
 *  - The access token lives in memory only. The refresh token is an HttpOnly cookie that scripts cannot read, so the
 *    app never stores, reads or sends it; it just calls refresh and the browser attaches the cookie.
 *  - refresh() is single-flight: concurrent 401s share one call, because rotation invalidates the old refresh token.
 *  - When the server refuses the refresh (401, 403, or any answer that is not transient) the session ends and the user is sent to the
 *    login page with a return URL. A transient failure (429, 408, 5xx, no network) is retried a little later, for at most
 *    REFRESH_RETRY.budgetMs in all, and if it persists the user stays signed in.
 *  - When the session expires, screens that hold unsent work keep it first (sessionExpiring$, SessionDraftService) and the guard that
 *    asks "Discard changes?" stands aside ($sessionExpired): the user is told the session ended, not asked about a page they cannot
 *    save any more. An explicit sign-out removes every kept draft; a sign-in removes those of other users.
 *  - Logout, a session end and a new sign-in cancel a pending retry and discard the result of a refresh already sent.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authClient = inject(AuthClient);
  private readonly router = inject(Router);

  private accessToken: string | undefined;
  private refreshInFlight: Observable<AuthResult> | undefined;
  private readonly sessionEnded = new Subject<void>();
  private readonly sessionExpiring = new Subject<void>();
  /** Drafts kept while sessionExpiring$ was being delivered (SessionDraftService reports each one). */
  private draftsKept = 0;
  /** Fires when the session ends or a new one starts: a refresh still waiting or running for the old session is dropped. */
  private readonly sessionChanged = new Subject<void>();

  private readonly $currentUser = signal<UserInfo | undefined>(undefined);
  private readonly $expired = signal(false);

  readonly $user = this.$currentUser.asReadonly();
  readonly $isAuthenticated = computed(() => this.$currentUser() !== undefined);
  /** Emits when the session ends (logout or expiry): caches holding user data should be dropped. */
  readonly sessionEnded$ = this.sessionEnded.asObservable();
  /** Emits when the session has expired, while the user is still known: a screen with unsent work keeps it now. */
  readonly sessionExpiring$ = this.sessionExpiring.asObservable();
  /** True from an expiry until the next sign-in: the page the user is on can no longer save, so leaving it needs no question. */
  readonly $sessionExpired = this.$expired.asReadonly();

  readonly login = new ApiCommand<LoginRequest, UserInfo>(credentials =>
    // The sign-in banner shows every answer (401 sentence, lockout, rate limit, server, network), so none raises a toast.
    this.authClient.login(credentials, handledInline()).pipe(
      tap(result => {
        this.sessionChanged.next();
        this.acceptTokens(result);
      }),
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

  /** Called by SessionDraftService when it kept a draft; endSession counts those made while a session expires. */
  noteDraftKept(): void {
    this.draftsKept++;
  }

  /** Clears the session; `expired` adds the "session expired" notice and remembers where the user was. */
  endSession(expired: boolean): void {
    // The notice says "kept" only for drafts saved NOW, not for an older one that happens to sit in storage.
    this.draftsKept = 0;
    if (expired) {
      this.$expired.set(true);
      this.sessionExpiring.next();
    } else {
      clearDrafts();
    }
    const draftKept = expired && this.draftsKept > 0;
    this.accessToken = undefined;
    this.$currentUser.set(undefined);
    this.refreshInFlight = undefined;
    this.sessionChanged.next();
    this.sessionEnded.next();
    const queryParams = expired
      ? {
          [RETURN_URL_PARAM]: this.router.url,
          [SESSION_REASON_PARAM]: draftKept ? SESSION_EXPIRED_KEPT_REASON : SESSION_EXPIRED_REASON
        }
      : {};
    void this.router.navigate([LOGIN_ROUTE], { queryParams });
  }

  private refreshSession(endSessionOnFailure: boolean): Observable<AuthResult> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }
    const startedAt = Date.now();
    this.refreshInFlight = defer(() => this.authClient.refresh(COOKIE_ONLY_BODY)).pipe(
      // App start (endSessionOnFailure false) never waits: an offline start must reach the login page at once.
      retry({
        count: endSessionOnFailure ? REFRESH_RETRY.attempts : 0,
        delay: (error, attempt) => {
          const wait = retryDelayMs(error, attempt);
          // The budget covers the requests too: a timed-out call must not keep the original request hanging for minutes.
          return isTransient(error) && Date.now() - startedAt + wait <= REFRESH_RETRY.budgetMs
            ? timer(wait)
            : throwError(() => error);
        }
      }),
      // Cancels the wait and drops a late answer. A caller still waiting gets an error (the original request fails as before).
      takeUntil(this.sessionChanged),
      tap(result => this.acceptTokens(result)),
      catchError((error: unknown) => {
        if (endSessionOnFailure && !isTransient(error)) {
          this.endSession(true);
        }
        return throwError(() => error);
      }),
      throwIfEmpty(() => new Error(SESSION_CHANGED_MESSAGE)),
      finalize(() => {
        this.refreshInFlight = undefined;
      }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.refreshInFlight;
  }

  private acceptTokens(result: AuthResult): void {
    this.$expired.set(false);
    clearDrafts(result.user.id);
    this.accessToken = result.accessToken;
    this.$currentUser.set(result.user);
  }
}
