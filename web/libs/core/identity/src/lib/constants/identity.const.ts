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
