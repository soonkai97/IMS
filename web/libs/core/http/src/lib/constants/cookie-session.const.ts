/** Header that proves our own page made a cookie-authenticated call (docs/security/refresh-cookie.md, section 4). */
export const CSRF_HEADER = 'X-Requested-With';
export const CSRF_HEADER_VALUE = 'XMLHttpRequest';

/** Calls that ride on the HttpOnly refresh cookie and therefore need the CSRF header. */
export const COOKIE_AUTH_ENDPOINTS = ['/api/auth/refresh', '/api/auth/logout'] as const;
