/** localStorage key of the refresh token before SEC-34 (only used to delete a leftover entry). */
export const LEGACY_REFRESH_TOKEN_STORAGE_KEY = 'ims.refreshToken';

export const LOGIN_ROUTE = '/login';
export const FORBIDDEN_ROUTE = '/forbidden';
export const HOME_ROUTE = '/';
export const RETURN_URL_PARAM = 'returnUrl';
export const SESSION_REASON_PARAM = 'reason';
export const SESSION_EXPIRED_REASON = 'expired';

/** Endpoints that must never get a bearer token or trigger the refresh-and-retry path. */
export const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'] as const;

/**
 * A refresh that fails for a reason other than "the cookie is no good" (rate limit, gateway error, network) is tried again
 * instead of ending the session: up to this many more tries, each after the server's Retry-After (or a doubling back-off),
 * never longer than the cap. After the last one the session stays; the next call that gets a 401 refreshes again.
 */
export const REFRESH_RETRY = { attempts: 2, baseDelayMs: 1000, maxDelayMs: 10_000 } as const;
