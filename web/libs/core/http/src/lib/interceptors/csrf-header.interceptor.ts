import { HttpInterceptorFn } from '@angular/common/http';
import {
  COOKIE_AUTH_ENDPOINTS,
  CSRF_HEADER,
  CSRF_HEADER_VALUE
} from '../constants/cookie-session.const';

/** Refresh and logout read the refresh cookie, so the API only accepts them with a header a cross-site form cannot add. */
export const csrfHeaderInterceptor: HttpInterceptorFn = (request, next) => {
  const usesCookie = COOKIE_AUTH_ENDPOINTS.some(endpoint => request.url.includes(endpoint));
  return usesCookie
    ? next(request.clone({ setHeaders: { [CSRF_HEADER]: CSRF_HEADER_VALUE } }))
    : next(request);
};
