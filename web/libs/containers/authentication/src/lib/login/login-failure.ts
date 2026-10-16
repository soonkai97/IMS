import { API_ERROR_KIND, ApiError, HTTP_STATUS } from '@ims/models';
import { LOGIN_FAILURE, LoginFailure } from './login-failure.types';

const ACCOUNT_LOCKED_CODE = 'account_locked';
const RETRY_AFTER_MINUTES = 'retryAfterMinutes';
const RETRY_AFTER_SECONDS = 'retryAfterSeconds';

function positiveNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : undefined;
}

/**
 * What a failed sign-in call means. The API's own sentence is never used (it is English): the kind and the numbers it sends
 * (`retryAfterMinutes` of a lockout, `retryAfterSeconds` of the rate limit) are enough to write ours in any language. The
 * lockout is only reported for the right password (SEC-08), so a 401 without that code is the one generic sentence.
 */
export function loginFailureOf(error: ApiError): LoginFailure {
  if (error.kind === API_ERROR_KIND.Network) {
    return { kind: LOGIN_FAILURE.Network };
  }
  if (error.status === HTTP_STATUS.TooManyRequests) {
    return {
      kind: LOGIN_FAILURE.RateLimited,
      seconds: positiveNumber(error.extensions[RETRY_AFTER_SECONDS])
    };
  }
  if (error.kind === API_ERROR_KIND.Unauthorized) {
    const minutes = positiveNumber(error.extensions[RETRY_AFTER_MINUTES]);
    return error.extensions['code'] === ACCOUNT_LOCKED_CODE && minutes !== undefined
      ? { kind: LOGIN_FAILURE.Locked, minutes }
      : { kind: LOGIN_FAILURE.Invalid };
  }
  return { kind: LOGIN_FAILURE.Server };
}
