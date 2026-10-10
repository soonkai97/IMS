import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, map, switchMap, tap, throwError } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError } from '@ims/models';
import { problemToApiError, readErrorBody } from '../errors/problem-details';
import { TIMEOUT_STATUS_TEXT } from './timeout.interceptor';

/** 401 is handled by the auth interceptor (refresh or redirect); field validation is shown next to the fields. */
function shouldToast(error: ApiError): boolean {
  if (error.kind === API_ERROR_KIND.Unauthorized) {
    return false;
  }
  return !(error.kind === API_ERROR_KIND.Validation && Object.keys(error.fieldErrors).length > 0);
}

/**
 * Turns every failed HTTP call into an ApiError (ProblemDetails mapped) and shows the server's `detail` as a toast
 * (including 503 "database unavailable" and 504 "timed out"). Register it first in withInterceptors([...]) so it
 * sees the error after the auth interceptor tried to recover.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);
  const i18n = inject(I18nService);

  const messageOf = (error: ApiError, response: HttpErrorResponse): string => {
    if (response.statusText === TIMEOUT_STATUS_TEXT) {
      return i18n.t('error.timeout');
    }
    if (error.kind === API_ERROR_KIND.Network) {
      return i18n.t('error.network');
    }
    if (error.kind === API_ERROR_KIND.Forbidden) {
      return i18n.t('error.forbidden');
    }
    return error.detail.length > 0 ? error.detail : i18n.t('error.generic');
  };

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      return from(readErrorBody(error)).pipe(
        map(body => problemToApiError(error.status, body)),
        tap(apiError => {
          if (shouldToast(apiError)) {
            toast.error(messageOf(apiError, error));
          }
        }),
        switchMap(apiError => throwError(() => apiError))
      );
    })
  );
};
