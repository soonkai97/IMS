/** localStorage key of the refresh token before SEC-34 (only used to delete a leftover entry). */
export const LEGACY_REFRESH_TOKEN_STORAGE_KEY = 'ims.refreshToken';

export const LOGIN_ROUTE = '/login';
export const FORBIDDEN_ROUTE = '/forbidden';
export const HOME_ROUTE = '/';
export const RETURN_URL_PARAM = 'returnUrl';
export const SESSION_REASON_PARAM = 'reason';
export const SESSION_EXPIRED_REASON = 'expired';
/** The sign-in notice after the user changed their own password (the server ended every session of theirs). */
export const SESSION_PASSWORD_CHANGED_REASON = 'password-changed';

/** Endpoints that must never get a bearer token or trigger the refresh-and-retry path. */
export const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'] as const;

/**
 * A refresh that fails for a transient reason (rate limit, 408, gateway error, no network) is tried again instead of ending the
 * session: up to `attempts` more tries, each after the server's Retry-After (or a doubling back-off), +-`jitter`, never longer
 * than `maxDelayMs`, and none that would end after `budgetMs` from the first request. After the last one the session stays; the
 * next call that gets a 401 refreshes again.
 */
export const REFRESH_RETRY = {
  attempts: 2,
  baseDelayMs: 1000,
  maxDelayMs: 10_000,
  budgetMs: 15_000,
  jitter: 0.25
} as const;

/** Same as SESSION_EXPIRED_REASON, and a screen kept what the user had not sent (SessionDraftService). */
export const SESSION_EXPIRED_KEPT_REASON = 'expired-kept';

/** How long a kept draft stays valid (the sign-in notice says so), and the most one draft may take in storage. */
export const DRAFT_TTL_MS = 15 * 60 * 1000;
export const DRAFT_MAX_CHARS = 1_000_000;

/** sessionStorage key prefix of unsent work kept across a session expiry: `ims.draft.<userId>.<screen key>`. */
export const DRAFT_STORAGE_PREFIX = 'ims.draft.';
