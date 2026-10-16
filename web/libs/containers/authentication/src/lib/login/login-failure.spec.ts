import { API_ERROR_KIND, ApiError } from '@ims/models';
import { loginFailureOf } from './login-failure';
import { LOGIN_FAILURE } from './login-failure.types';

const failure = (kind: ApiError['kind'], status: number, extensions = {}): ApiError => ({
  isApiError: true,
  kind,
  status,
  title: '',
  detail: 'The API sentence, in English.',
  fieldErrors: {},
  extensions
});

describe('loginFailureOf', () => {
  it('a 401 is the one generic sentence, whatever the cause', () => {
    expect(loginFailureOf(failure(API_ERROR_KIND.Unauthorized, 401))).toEqual({
      kind: LOGIN_FAILURE.Invalid
    });
  });

  it('a lockout keeps the minutes the API sent; without a number it is the generic sentence', () => {
    const locked = { code: 'account_locked', retryAfterMinutes: 14 };
    expect(loginFailureOf(failure(API_ERROR_KIND.Unauthorized, 401, locked))).toEqual({
      kind: LOGIN_FAILURE.Locked,
      minutes: 14
    });
    expect(
      loginFailureOf(failure(API_ERROR_KIND.Unauthorized, 401, { code: 'account_locked' }))
    ).toEqual({ kind: LOGIN_FAILURE.Invalid });
  });

  it('a 429 keeps the seconds, and an unreadable number is dropped', () => {
    const limited = (extensions: object): unknown =>
      loginFailureOf(failure(API_ERROR_KIND.Unknown, 429, extensions));

    expect(limited({ retryAfterSeconds: 42 })).toEqual({ kind: 'rate-limited', seconds: 42 });
    expect(limited({ retryAfterSeconds: 'soon' })).toEqual({
      kind: 'rate-limited',
      seconds: undefined
    });
  });

  it('no answer from the server is the network failure, anything else the server failure', () => {
    expect(loginFailureOf(failure(API_ERROR_KIND.Network, 0)).kind).toBe(LOGIN_FAILURE.Network);
    expect(loginFailureOf(failure(API_ERROR_KIND.Server, 500)).kind).toBe(LOGIN_FAILURE.Server);
    expect(loginFailureOf(failure(API_ERROR_KIND.Forbidden, 403)).kind).toBe(LOGIN_FAILURE.Server);
  });
});
