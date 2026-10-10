import { WritableSignal } from '@angular/core';
import { defer, MonoTypeOperatorFunction, tap } from 'rxjs';
import {
  ApiError,
  ApiOperation,
  LOADING_OPERATION,
  SUCCESS_OPERATION,
  UNKNOWN_ERROR,
  failureOperation,
  isApiError
} from '@ims/models';

export function toApiError(error: unknown): ApiError {
  if (isApiError(error)) {
    return error;
  }
  return {
    ...UNKNOWN_ERROR,
    detail: error instanceof Error ? error.message : UNKNOWN_ERROR.detail
  };
}

/**
 * Mirrors the lifecycle of the source into an operation signal: loading on subscribe, success on completion,
 * failure (with the mapped ApiError) on error. The error is re-thrown so the caller decides how to continue.
 */
export function trackOperation<T>(
  operation: WritableSignal<ApiOperation>
): MonoTypeOperatorFunction<T> {
  return source$ =>
    defer(() => {
      operation.set(LOADING_OPERATION);
      return source$.pipe(
        tap({
          complete: () => operation.set(SUCCESS_OPERATION),
          error: (error: unknown) => operation.set(failureOperation(toApiError(error)))
        })
      );
    });
}
