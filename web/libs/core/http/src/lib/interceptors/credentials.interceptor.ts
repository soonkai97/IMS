import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { isApiRequest } from '../config/api-request';
import { AppConfig } from '../config/app-config';

/**
 * Sends cookies with every API call. Same-origin deployments do it anyway; the dev server on :4200 calling the API on
 * another port needs it for the HttpOnly refresh cookie (the API lists the web origin in Cors:Origins).
 */
export const credentialsInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(AppConfig);
  return isApiRequest(request.url, config.$apiBaseUrl())
    ? next(request.clone({ withCredentials: true }))
    : next(request);
};
