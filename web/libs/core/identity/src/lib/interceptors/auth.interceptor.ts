import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AppConfig, isApiRequest } from '@ims/core/http';
import { HTTP_STATUS } from '@ims/models';
import { AUTH_ENDPOINTS } from '../constants/identity.const';
import { AuthService } from '../services/auth.service';

function withBearer(
  request: HttpRequest<unknown>,
  token: string | undefined
): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

function isAuthEndpoint(url: string): boolean {
  return AUTH_ENDPOINTS.some(endpoint => url.includes(endpoint));
}

/**
 * Adds the bearer token. On a 401 it refreshes the token once and replays the request; if the refresh fails the
 * AuthService ends the session (redirect to login) and the original 401 is passed on.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const config = inject(AppConfig);

  if (!isApiRequest(request.url, config.$apiBaseUrl()) || isAuthEndpoint(request.url)) {
    return next(request);
  }

  return next(withBearer(request, auth.token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== HTTP_STATUS.Unauthorized) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(
        switchMap(token => next(withBearer(request, token))),
        catchError((refreshError: unknown) =>
          throwError(() => (refreshError instanceof HttpErrorResponse ? refreshError : error))
        )
      );
    })
  );
};
