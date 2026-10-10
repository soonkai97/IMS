import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AppConfig } from '../config/app-config';
import { isApiRequest } from '../config/api-request';

export const CORRELATION_HEADER = 'X-Correlation-Id';

/** Adds a fresh correlation id to every API call so a user report can be matched to the server log line. */
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(AppConfig);
  if (!isApiRequest(request.url, config.$apiBaseUrl()) || request.headers.has(CORRELATION_HEADER)) {
    return next(request);
  }
  const correlationId = crypto.randomUUID().replace(/-/g, '');
  return next(request.clone({ setHeaders: { [CORRELATION_HEADER]: correlationId } }));
};
