import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, map, switchMap, tap, throwError } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { API_ERROR_KIND, ApiError, HTTP_STATUS } from '@ims/models';
import { IF_MATCH } from '../concurrency/if-match';
import { isHandledInline } from '../errors/handled-inline';
import { problemToApiError, readErrorBody } from '../errors/problem-details';
import { TIMEOUT_STATUS_TEXT } from './timeout.interceptor';

/** A proxy or gateway answered instead of the API: its body is HTML, so there is no sentence of ours to show. */
const GATEWAY_STATUSES: readonly number[] = [502, 503, 504];

/**
 * One failure, one announcement: no toast when something else shows the failure. 401 is handled by the auth interceptor
 * (refresh or redirect); field validation is shown next to the fields; a 409 on a call that sent If-Match (a stale copy,
 * or a rule such as insufficient stock) is explained by the screen in a banner with the way out; a call flagged with
 * handledInline() is shown by its screen (a banner or a message at a field), which then shows `error.message`.
 */
function shouldToast(error: ApiError, request: HttpRequest<unknown>): boolean {
  if (
    error.kind === API_ERROR_KIND.Unauthorized ||
    isHandledInline(request.context, error.status)
  ) {
    return false;
  }
  if (error.kind === API_ERROR_KIND.Conflict && request.context.get(IF_MATCH) !== undefined) {
    return false;
  }
  return !(error.kind === API_ERROR_KIND.Validation && Object.keys(error.fieldErrors).length > 0);
}

/**
 * Turns every failed HTTP call into an ApiError (ProblemDetails mapped, the sentence to show in `message`) and shows that
 * sentence as an error toast unless the screen shows it (shouldToast). A call that changes something is the user's next
 * action, so an error toast left by the last failure goes first. Register it first in withInterceptors([...]) so it
 * sees the error after the auth interceptor tried to recover.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);
  const i18n = inject(I18nService);

  const messageOf = (error: ApiError, response: HttpErrorResponse): string => {
    // The API's own sentence names the wait ("Try again in 12 seconds."); ours is for a proxy that sends none (EA-24).
    if (response.status === HTTP_STATUS.TooManyRequests && error.detail.length === 0) {
      return i18n.t('error.tooManyRequests');
    }
    if (response.statusText === TIMEOUT_STATUS_TEXT) {
      return i18n.t('error.timeout');
    }
    if (error.kind === API_ERROR_KIND.Network) {
      return i18n.t('error.network');
    }
    // 502/504 from a gateway have no ProblemDetails; a 503 that carries the API's own sentence keeps it (EA-15).
    if (GATEWAY_STATUSES.includes(response.status) && error.detail.length === 0) {
      return i18n.t('error.network');
    }
    if (error.kind === API_ERROR_KIND.Forbidden) {
      return i18n.t('error.forbidden');
    }
    return error.detail.length > 0 ? error.detail : i18n.t('error.generic');
  };

  if (request.method !== 'GET') {
    toast.dismissErrors();
  }

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      return from(readErrorBody(error)).pipe(
        map(body => {
          const apiError = problemToApiError(error.status, body);
          return { ...apiError, message: messageOf(apiError, error) };
        }),
        tap(apiError => {
          if (shouldToast(apiError, request)) {
            toast.error(apiError.message);
          }
        }),
        switchMap(apiError => throwError(() => apiError))
      );
    })
  );
};
