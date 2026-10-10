import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TimeoutError, catchError, throwError, timeout } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { isApiRequest } from '../config/api-request';

/** Slightly above the server's own limits (30 s normal requests, 120 s exports / imports). */
export const DEFAULT_TIMEOUT_MS = 35_000;
export const LONG_TIMEOUT_MS = 125_000;
export const TIMEOUT_STATUS_TEXT = 'timeout';

const LONG_RUNNING_PATHS = ['/export', '/api/import/'];

function timeoutFor(url: string): number {
  return LONG_RUNNING_PATHS.some(path => url.includes(path)) ? LONG_TIMEOUT_MS : DEFAULT_TIMEOUT_MS;
}

/** Gives up on a request the server never answered; reported as an HttpErrorResponse with status 0 / "timeout". */
export const timeoutInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(AppConfig);
  if (!isApiRequest(request.url, config.$apiBaseUrl())) {
    return next(request);
  }
  return next(request).pipe(
    timeout({ first: timeoutFor(request.url) }),
    catchError((error: unknown) =>
      throwError(() =>
        error instanceof TimeoutError
          ? new HttpErrorResponse({ status: 0, statusText: TIMEOUT_STATUS_TEXT, url: request.url })
          : error
      )
    )
  );
};
